import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';

import { Auth } from '../auth/auth';
import { BasketService } from '../core/basket';
import { CatalogService, stockLevel, type ProductDetail } from '../core/catalog';
import { formatMoney } from '../core/formatting';
import { Icon } from '../icon';

/**
 * A single product.
 *
 * Behaviourally identical to the React `ProductDetailPage`, including the
 * distinction between a 404 (a normal outcome deserving a helpful page) and a
 * genuine failure (an error the user can retry). Collapsing both into one
 * "something went wrong" screen gives the user nothing to act on.
 *
 * ---
 * **React/Angular divergence:** the route parameter arrives as a signal input
 * via `withComponentInputBinding()`, so `id` is bound directly from the route
 * with no `useParams` equivalent and no manual subscription. Genuinely tidier
 * than React's hook.
 */
@Component({
  selector: 'app-product-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon],
  template: `
    @if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading product…</p>
      </div>
    } @else if (notFound()) {
      <div class="centred">
        <div class="card stack" role="alert">
          <h1 class="page-title">Product not found</h1>
          <p class="muted">That product does not exist, or is no longer available.</p>
          <div class="row"><a class="btn btn--primary" routerLink="/products">Back to products</a></div>
        </div>
      </div>
    } @else if (error(); as message) {
      <div class="centred">
        <div class="card stack" role="alert">
          <h1 class="page-title">Could not load product</h1>
          <p class="muted">{{ message }}</p>
          <div class="row">
            <a class="btn btn--primary" routerLink="/products">Back to products</a>
            <button type="button" class="btn btn--secondary" (click)="reload()">Try again</button>
          </div>
        </div>
      </div>
    } @else if (product(); as p) {
      <div class="stack">
        <nav aria-label="Breadcrumb" class="muted">
          <a routerLink="/products">Products</a> <span aria-hidden="true">/</span>
          <a [routerLink]="['/products']" [queryParams]="{ category: p.categorySlug }">{{ p.categoryName }}</a>
        </nav>

        <div class="product-detail">
          <!-- alt="" because the h1 immediately beside it already names the product. A duplicate
               announcement is noise, not information. -->
          <div class="product-media">
            <img
              class="product-media__img"
              [src]="p.imageUrl ?? '/img/placeholder.svg'"
              alt=""
              aria-hidden="true"
              width="800"
              height="600"
            />
          </div>

          <div class="stack">
            <h1 class="page-title">{{ p.name }}</h1>

            <p class="muted">
              <a [routerLink]="['/products']" [queryParams]="{ brand: p.brandSlug }">{{ p.brandName }}</a>
              ·
              <span>SKU {{ p.sku }}</span>
            </p>

            <div class="row">
              <p class="product-detail__price">{{ money(p) }}</p>
              <span class="badge badge--{{ stock(p).level }}">{{ stock(p).label }}</span>
            </div>

            <p>{{ p.description }}</p>

            <!-- Disabled rather than hidden: a missing button looks like a
                 broken page, a disabled one with a reason explains itself. -->
            <div>
              <button
                type="button"
                class="btn btn--primary"
                [disabled]="p.stockOnHand === 0 || adding()"
                [title]="p.stockOnHand === 0 ? 'Out of stock' : ''"
                (click)="addToBasket(p)"
              >
                {{ adding() ? 'Adding…' : 'Add to basket' }}
              </button>

              @if (added()) {
                <!-- role="status" so a screen reader announces it. A visual-only confirmation leaves
                     a non-sighted customer with no evidence the click did anything. -->
                <p class="muted" role="status">
                  Added to your basket. <a routerLink="/basket">View basket</a>
                </p>
              }

              @if (addError()) {
                <p class="muted" role="alert">{{ addError() }}</p>
              }
            </div>

            <!-- The three things a shopper checks before committing. Repeated from the home page on
                 purpose - this is the page where the question is actually asked. -->
            <ul class="stack--tight plain-list muted small">
              <li class="row">
                <app-icon name="truck" variant="icon--sm" /> Free delivery on orders over £50
              </li>
              <li class="row"><app-icon name="shield" variant="icon--sm" /> 30-day returns</li>
              <li class="row">
                <app-icon name="boxOpen" variant="icon--sm" /> Stock is reserved when you check out,
                not when you add to the basket
              </li>
            </ul>
          </div>
        </div>
      </div>
    }
  `,
})
export class ProductDetailPage {
  private readonly catalog = inject(CatalogService);
  private readonly baskets = inject(BasketService);
  private readonly auth = inject(Auth);

  protected readonly adding = signal(false);
  protected readonly added = signal(false);
  protected readonly addError = signal<string | null>(null);

  /** Bound straight from the route by withComponentInputBinding(). */
  readonly id = input.required<string>();

  protected readonly product = signal<ProductDetail | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    // Deliberately in the constructor rather than an effect: `id` comes from the
    // route and the component is recreated when it changes, so there is nothing
    // to react to.
    queueMicrotask(() => void this.load());
  }

  protected async load(): Promise<void> {
    this.isLoading.set(true);
    this.notFound.set(false);
    this.error.set(null);

    try {
      this.product.set(await this.catalog.getProduct(this.id()));
    } catch (cause) {
      if (cause instanceof HttpErrorResponse && cause.status === 404) {
        this.notFound.set(true);
      } else {
        this.error.set(cause instanceof Error ? cause.message : 'Unexpected error');
      }
    } finally {
      this.isLoading.set(false);
    }
  }

  protected reload(): void {
    void this.load();
  }

  protected money(product: ProductDetail): string {
    return formatMoney({ amount: product.price, currency: product.currency });
  }

  protected stock(product: ProductDetail) {
    return stockLevel(product.stockOnHand);
  }

  /**
   * Adds one of this product to the basket.
   *
   * The price sent here is what the customer is looking at, and the server treats it as display
   * information only - every line is re-priced from the catalogue at checkout.
   */
  protected async addToBasket(product: ProductDetail): Promise<void> {
    if (!this.auth.isAuthenticated()) {
      this.auth.signIn();
      return;
    }

    this.adding.set(true);
    this.addError.set(null);

    try {
      await this.baskets.add({
        productId: product.id,
        sku: product.sku,
        productName: product.name,
        imageUrl: product.imageUrl,
        unitPrice: product.price,
        currency: product.currency,
        quantity: 1,
      });

      this.added.set(true);
    } catch (cause) {
      this.addError.set(cause instanceof Error ? cause.message : 'Could not add to your basket.');
    } finally {
      this.adding.set(false);
    }
  }
}
