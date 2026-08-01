import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';

import { Auth } from '../auth/auth';
import { BasketService } from '../core/basket';
import {
  CatalogService,
  colourHasStock,
  coloursOf,
  findVariant,
  sizeHasStock,
  sizesOf,
  stockLevel,
  type ProductDetail,
  type ProductVariant,
} from '../core/catalog';
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

            <!-- A real radio group. Arrow-key navigation, one tab stop, and "Size, Medium, 2 of 4"
                 announced by a screen reader - all of which a div with a click handler would have to
                 reimplement, and usually does not.

                 A sold-out size is disabled AND struck through AND named in the stock line below.
                 Never colour alone (WCAG 1.4.1). -->
            @if (sizes().length > 0) {
              <fieldset class="option-group">
                <legend class="option-group__legend">
                  Size
                  @if (needsSize()) {
                    <span class="option-group__hint">— choose one</span>
                  }
                </legend>

                <div class="option-list">
                  @for (option of sizes(); track option) {
                    <span class="option">
                      <input
                        class="option__input"
                        type="radio"
                        name="size"
                        [id]="'size-' + option"
                        [value]="option"
                        [checked]="size() === option"
                        [disabled]="!hasSizeStock(option)"
                        (change)="size.set(option)"
                      />
                      <label class="option__label" [attr.for]="'size-' + option">
                        {{ option }}
                        @if (!hasSizeStock(option)) {
                          <span class="visually-hidden"> — sold out</span>
                        }
                      </label>
                    </span>
                  }
                </div>
              </fieldset>
            }

            @if (colours().length > 0) {
              <fieldset class="option-group">
                <legend class="option-group__legend">Colour</legend>

                <div class="option-list">
                  @for (option of colours(); track option.name) {
                    <span class="option">
                      <input
                        class="option__input"
                        type="radio"
                        name="colour"
                        [id]="'colour-' + option.name"
                        [value]="option.name"
                        [checked]="colour() === option.name"
                        [disabled]="!hasColourStock(option.name)"
                        (change)="colour.set(option.name)"
                      />
                      <label class="option__label" [attr.for]="'colour-' + option.name">
                        <!-- The swatch is decoration on top of the name, never instead of it. A colour
                             conveyed only as a colour is unreadable to plenty of people. -->
                        <span
                          class="swatch"
                          [style.background]="option.hex ?? 'transparent'"
                          aria-hidden="true"
                        ></span>
                        {{ option.name }}
                        @if (!hasColourStock(option.name)) {
                          <span class="visually-hidden"> — sold out</span>
                        }
                      </label>
                    </span>
                  }
                </div>
              </fieldset>
            }

            <!-- role="status", so choosing a size ANNOUNCES how many are left rather than silently
                 changing a number a sighted user happens to be looking at. Fixed height, so the button
                 below does not jump out from under the pointer when this appears. -->
            <p class="variant-stock" role="status">
              @if (needsSize()) {
                <span class="muted">Choose a size to see availability.</span>
              } @else if (selected(); as variant) {
                @if (variant.stockOnHand === 0) {
                  <span class="badge badge--out-of-stock">Sold out in this option</span>
                } @else if (variant.stockOnHand <= 5) {
                  <span class="badge badge--low-stock">Only {{ variant.stockOnHand }} left</span>
                } @else {
                  <span class="badge badge--in-stock">In stock</span>
                }
              } @else {
                <span class="badge badge--out-of-stock">That combination is not available</span>
              }
            </p>

            <!-- Disabled rather than hidden: a missing button looks like a
                 broken page, a disabled one with a reason explains itself. -->
            <div>
              <button
                type="button"
                class="btn btn--primary"
                [disabled]="needsSize() || !selected() || selected()!.stockOnHand === 0 || adding()"
                [title]="addButtonTitle()"
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

  /**
   * Which size and colour is chosen.
   *
   * `null` means "not chosen yet" for a product that HAS that axis, and also "this product has no
   * such axis" — the two collapse deliberately, because a mug's variants genuinely have `size: null`
   * and matching on null is then correct rather than a special case.
   */
  protected readonly size = signal<string | null>(null);
  protected readonly colour = signal<string | null>(null);

  protected readonly variants = computed<readonly ProductVariant[]>(
    () => this.product()?.variants ?? [],
  );
  protected readonly sizes = computed(() => sizesOf(this.variants()));
  protected readonly colours = computed(() => coloursOf(this.variants()));

  protected readonly selected = computed(() =>
    findVariant(this.variants(), this.size(), this.colour()),
  );

  /** A size axis not yet chosen — not the same as a combination that does not exist. */
  protected readonly needsSize = computed(() => this.sizes().length > 0 && this.size() === null);

  protected readonly addButtonTitle = computed(() => {
    if (this.needsSize()) return 'Choose a size';

    const variant = this.selected();
    return !variant || variant.stockOnHand === 0 ? 'Out of stock' : '';
  });

  protected hasSizeStock = (option: string) => sizeHasStock(this.variants(), option);

  protected hasColourStock = (option: string) => colourHasStock(this.variants(), option);

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
      const product = await this.catalog.getProduct(this.id());
      this.product.set(product);

      /*
       * Pre-selects a colour, never a size.
       *
       * This is what clothing retailers do, and the asymmetry is deliberate. The photograph already
       * shows a colour, so defaulting to one is honest; a size is a decision only the customer can
       * make, and defaulting it means someone buys a Small because it happened to be first. So Add to
       * basket stays disabled until a size is picked, and says why.
       *
       * The first colour WITH STOCK, so the default is something you can actually buy.
       */
      const options = coloursOf(product.variants);

      if (options.length > 0) {
        const available = options.find((option) => colourHasStock(product.variants, option.name));
        this.colour.set((available ?? options[0])!.name);
      }
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
      const variant = findVariant(product.variants, this.size(), this.colour());

      if (!variant) {
        // Should be unreachable - the button is disabled without a variant - but failing here beats
        // sending the style code, which Inventory has no stock row for.
        throw new Error('Choose an option before adding to your basket.');
      }

      // The VARIANT SKU, not the product's style code. This is what the warehouse picks by.
      await this.baskets.add({
        productId: product.id,
        sku: variant.sku,
        productName: product.name,
        size: variant.size,
        colourName: variant.colourName,
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
