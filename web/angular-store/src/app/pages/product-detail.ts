import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';

import { CatalogService, stockLevel, type ProductDetail } from '../core/catalog';
import { formatMoney } from '../core/formatting';

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
  imports: [RouterLink],
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
          <div class="product-detail__image" aria-hidden="true">{{ p.name.charAt(0) }}</div>

          <div class="stack">
            <h1 class="page-title">{{ p.name }}</h1>

            <p class="muted">
              <a [routerLink]="['/products']" [queryParams]="{ brand: p.brandSlug }">{{ p.brandName }}</a>
              ·
              <span>SKU {{ p.sku }}</span>
            </p>

            <p class="product-detail__price">{{ money(p) }}</p>

            <span class="badge badge--{{ stock(p).level }}">{{ stock(p).label }}</span>

            <p>{{ p.description }}</p>

            <!-- Disabled rather than hidden: a missing button looks like a
                 broken page, a disabled one with a reason explains itself. -->
            <div>
              <button type="button" class="btn btn--primary" disabled title="Basket arrives in Phase 6">
                Add to basket
              </button>
            </div>
          </div>
        </div>
      </div>
    }
  `,
})
export class ProductDetailPage {
  private readonly catalog = inject(CatalogService);

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
}
