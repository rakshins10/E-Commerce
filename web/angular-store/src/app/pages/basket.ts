import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Auth } from '../auth/auth';
import { BasketService } from '../core/basket';
import { formatMoney } from '../core/formatting';

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
  imports: [RouterLink],
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
            <div class="card stack">
              <p class="lede">Your basket is empty.</p>
              <div><a class="btn btn--primary" routerLink="/products">Browse products</a></div>
            </div>
          } @else {
            <!-- A table, because this is tabular data. A list of divs would lose the column headers a
                 screen reader announces with each cell. -->
            <div class="card">
              <table class="table">
                <caption class="visually-hidden">Items in your basket</caption>
                <thead>
                  <tr>
                    <th scope="col">Product</th>
                    <th scope="col">Price</th>
                    <th scope="col">Quantity</th>
                    <th scope="col">Total</th>
                    <th scope="col"><span class="visually-hidden">Actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (item of current.items; track item.productId) {
                    <tr>
                      <th scope="row">
                        <a [routerLink]="['/products', item.productId]">{{ item.productName }}</a>
                        <div class="muted small">{{ item.sku }}</div>
                      </th>
                      <td>{{ money(item.unitPrice, item.currency) }}</td>
                      <td>
                        <label class="visually-hidden" [attr.for]="'quantity-' + item.productId">
                          Quantity for {{ item.productName }}
                        </label>
                        <input
                          [id]="'quantity-' + item.productId"
                          type="number"
                          class="input input--narrow"
                          min="0"
                          max="100"
                          [value]="item.quantity"
                          (input)="onQuantityChange(item.productId, $event)"
                        />
                      </td>
                      <td>{{ money(item.lineTotal, item.currency) }}</td>
                      <td>
                        <button
                          type="button"
                          class="btn btn--secondary"
                          [disabled]="saving()"
                          (click)="remove(item.productId)"
                        >
                          <!-- The accessible name says WHAT is being removed. Twelve buttons all
                               called "Remove" are useless to anyone navigating by button. -->
                          <span aria-hidden="true">Remove</span>
                          <span class="visually-hidden">Remove {{ item.productName }}</span>
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <div class="card stack">
              <p class="lede" role="status">
                {{ current.totalUnits }} {{ current.totalUnits === 1 ? 'item' : 'items' }} ·
                {{ money(current.estimatedTotal, current.currency) }}
              </p>

              <!-- Said plainly rather than hidden in small print. Prices are re-checked at checkout,
                   and a customer who sees a different total deserves to have been warned. -->
              <p class="muted small">
                Prices are confirmed when you place your order, so this total may change.
              </p>

              <div class="row">
                <a class="btn btn--primary" routerLink="/checkout">Checkout</a>
                <button
                  type="button"
                  class="btn btn--secondary"
                  [disabled]="saving()"
                  (click)="clear()"
                >
                  Empty basket
                </button>
              </div>
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
