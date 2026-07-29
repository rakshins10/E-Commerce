import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Auth } from '../auth/auth';
import { BasketService } from '../core/basket';
import { formatMoney } from '../core/formatting';
import { Icon } from '../icon';

/**
 * The basket.
 *
 * Behaviourally identical to the React `BasketPage`: same labels, same states, so the shared
 * Playwright specs pass against both.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * The optimistic quantity update lives in `BasketService` here and in a TanStack Query `onMutate`
 * handler in React. React's version gets cancellation of in-flight refetches and automatic rollback
 * from the library; Angular's is hand-written, including saving and restoring the previous value.
 * Around fifteen lines of difference, and the fifteen lines are the ones that are easy to get wrong.
 */
@Component({
  selector: 'app-basket-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon],
  template: `
    @if (auth.isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite"><p class="lede">Loading…</p></div>
    } @else if (!auth.isAuthenticated()) {
      <div class="centred">
        <div class="card stack">
          <h1 class="page-title">Your basket</h1>
          <p class="muted">Sign in to see the items in your basket.</p>
          <div>
            <button type="button" class="btn btn--primary" (click)="auth.signIn()">Sign in</button>
          </div>
        </div>
      </div>
    } @else if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading your basket…</p>
      </div>
    } @else if (error()) {
      <div class="centred">
        <div class="card stack" role="alert">
          <h1 class="page-title">Could not load your basket</h1>
          <p class="muted">{{ error() }}</p>
          <button type="button" class="btn btn--primary" (click)="reload()">Try again</button>
        </div>
      </div>
    } @else {
      <div class="stack">
        <h1 class="page-title">Your basket</h1>

        @if (basket(); as current) {
          @if (current.items.length === 0) {
            <div class="card stack empty-state">
              <app-icon name="cart" variant="empty-state__icon" />
              <p class="lede">Your basket is empty.</p>
              <div><a class="btn btn--primary" routerLink="/products">Browse products</a></div>
            </div>
          } @else {
            <div class="checkout-layout">
              <!-- A list, not a table.

                   This WAS a table, on the reasoning that a basket is tabular data. It is not: each
                   line is one product with a picture, a price and two controls, and the
                   "Price/Quantity/Total" headers a screen reader repeated on every cell added nothing
                   a shopper did not already read. What matters for accessibility is that every control
                   still names its own product - which the labels below do - not that the layout is a
                   grid. -->
              <div class="card">
                <ul class="plain-list" aria-label="Items in your basket">
                  @for (item of current.items; track item.productId) {
                    <li class="line-item">
                      <a [routerLink]="['/products', item.productId]" tabindex="-1" aria-hidden="true">
                        <img
                          class="line-item__media"
                          [src]="item.imageUrl ?? '/img/placeholder.svg'"
                          alt=""
                          loading="lazy"
                          width="80"
                          height="80"
                        />
                      </a>

                      <div class="line-item__body">
                        <div class="line-item__top">
                          <div class="stack--tight">
                            <a [routerLink]="['/products', item.productId]">{{ item.productName }}</a>
                            <span class="muted small">{{ item.sku }}</span>
                          </div>
                          <span class="price">{{ money(item.lineTotal, item.currency) }}</span>
                        </div>

                        <div class="line-item__actions">
                          <!-- Three real controls, not arrows drawn on a div. The buttons are the fast
                               path on a phone; the input is still there for someone typing 12. -->
                          <div class="stepper">
                            <button
                              type="button"
                              class="stepper__btn"
                              [attr.aria-label]="'Decrease quantity for ' + item.productName"
                              [disabled]="saving()"
                              (click)="setQuantity(item.productId, item.quantity - 1)"
                            >
                              <span aria-hidden="true">−</span>
                            </button>

                            <label class="visually-hidden" [attr.for]="'quantity-' + item.productId">
                              Quantity for {{ item.productName }}
                            </label>
                            <input
                              [id]="'quantity-' + item.productId"
                              type="number"
                              class="stepper__input"
                              min="0"
                              max="100"
                              [value]="item.quantity"
                              (input)="onQuantityChange(item.productId, $event)"
                            />

                            <button
                              type="button"
                              class="stepper__btn"
                              [attr.aria-label]="'Increase quantity for ' + item.productName"
                              [disabled]="saving()"
                              (click)="setQuantity(item.productId, item.quantity + 1)"
                            >
                              <span aria-hidden="true">+</span>
                            </button>
                          </div>

                          <span class="muted small">
                            {{ money(item.unitPrice, item.currency) }} each
                          </span>

                          <button
                            type="button"
                            class="btn btn--ghost btn--sm"
                            [disabled]="saving()"
                            (click)="remove(item.productId)"
                          >
                            <!-- The accessible name says WHAT is being removed. Twelve buttons all
                                 called "Remove" are useless to anyone navigating by button. -->
                            <app-icon name="trash" variant="icon--sm" />
                            <span aria-hidden="true">Remove</span>
                            <span class="visually-hidden">Remove {{ item.productName }}</span>
                          </button>
                        </div>
                      </div>
                    </li>
                  }
                </ul>
              </div>

              <aside class="checkout-layout__aside" aria-label="Order summary">
                <div class="card stack">
                  <h2 style="margin-top: 0">Summary</h2>

                  <div class="summary">
                    <p class="summary__row" role="status">
                      <span
                        >{{ current.totalUnits }}
                        {{ current.totalUnits === 1 ? 'item' : 'items' }}</span
                      >
                      <span>{{ money(current.estimatedTotal, current.currency) }}</span>
                    </p>
                    <p class="summary__row">
                      <span>Delivery</span>
                      <span>Calculated at checkout</span>
                    </p>
                    <p class="summary__total">
                      <span>Estimated total</span>
                      <span>{{ money(current.estimatedTotal, current.currency) }}</span>
                    </p>
                  </div>

                  <!-- Said plainly rather than hidden in small print. Prices are re-checked at
                       checkout, and a customer who sees a different total deserves to have been
                       warned. -->
                  <p class="muted small">
                    Prices are confirmed when you place your order, so this total may change.
                  </p>

                  <a class="btn btn--primary btn--block" routerLink="/checkout">Checkout</a>

                  <button
                    type="button"
                    class="btn btn--ghost btn--block"
                    [disabled]="saving()"
                    (click)="clear()"
                  >
                    Empty basket
                  </button>
                </div>
              </aside>
            </div>
          }
        }
      </div>
    }
  `,
})
export class BasketPage {
  protected readonly auth = inject(Auth);
  private readonly baskets = inject(BasketService);

  protected readonly basket = this.baskets.basket;
  protected readonly isLoading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly money = formatMoney2;

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);

    try {
      await this.baskets.load();
    } catch (cause) {
      this.error.set(message(cause));
    } finally {
      this.isLoading.set(false);
    }
  }

  protected onQuantityChange(productId: string, event: Event): void {
    const quantity = Number((event.target as HTMLInputElement).value);
    void this.run(() => this.baskets.setQuantity(productId, quantity));
  }

  protected setQuantity(productId: string, quantity: number): void {
    void this.run(() => this.baskets.setQuantity(productId, quantity));
  }

  protected remove(productId: string): void {
    void this.run(() => this.baskets.remove(productId));
  }

  protected clear(): void {
    void this.run(() => this.baskets.clear());
  }

  /**
   * The wrapper TanStack Query hands React for free, per mutation.
   *
   * Nine lines is not a crisis, but it is repeated on every screen that writes — and the component
   * owns `saving` and `error` for ALL operations at once, so one pending call disables every button
   * rather than just its own.
   */
  private async run(action: () => Promise<void>): Promise<void> {
    this.saving.set(true);
    this.error.set(null);

    try {
      await action();
    } catch (cause) {
      this.error.set(message(cause));
    } finally {
      this.saving.set(false);
    }
  }
}

function formatMoney2(amount: number, currency: string): string {
  return formatMoney({ amount, currency });
}

function message(cause: unknown): string {
  return cause instanceof Error ? cause.message : 'Something went wrong.';
}
