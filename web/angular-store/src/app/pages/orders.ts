import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Auth } from '../auth/auth';
import {
  BasketService,
  ORDER_CANCELLATION_LABELS,
  ORDER_STATUS_LABELS,
  ORDER_TIMELINE,
  type Order,
  type OrderSummary,
} from '../core/basket';
import { formatDateTime, formatMoney } from '../core/formatting';

/**
 * "My orders".
 *
 * Behaviourally identical to the React `OrdersPage`, so the shared Playwright specs pass against both.
 */
@Component({
  selector: 'app-orders-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (auth.isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite"><p class="lede">Loading…</p></div>
    } @else if (!auth.isAuthenticated()) {
      <div class="centred">
        <div class="card stack">
          <h1 class="page-title">Your orders</h1>
          <p class="muted">Sign in to see your order history.</p>
          <div>
            <button type="button" class="btn btn--primary" (click)="auth.signIn()">Sign in</button>
          </div>
        </div>
      </div>
    } @else if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading your orders…</p>
      </div>
    } @else if (error()) {
      <div class="centred">
        <div class="card stack" role="alert">
          <h1 class="page-title">Could not load your orders</h1>
          <p class="muted">{{ error() }}</p>
          <button type="button" class="btn btn--primary" (click)="reload()">Try again</button>
        </div>
      </div>
    } @else {
      <div class="stack">
        <h1 class="page-title">Your orders</h1>

        @if (orders().length === 0) {
          <div class="card stack">
            <p class="lede">You have not placed any orders yet.</p>
            <div><a class="btn btn--primary" routerLink="/products">Browse products</a></div>
          </div>
        } @else {
          <div class="card">
            <table class="table">
              <caption class="visually-hidden">Your orders, newest first</caption>
              <thead>
                <tr>
                  <th scope="col">Order</th>
                  <th scope="col">Placed</th>
                  <th scope="col">Items</th>
                  <th scope="col">Total</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                @for (order of orders(); track order.id) {
                  <tr>
                    <th scope="row">
                      <a [routerLink]="['/orders', order.id]">{{ order.orderNumber }}</a>
                    </th>
                    <td>{{ dateTime(order.placedAt) }}</td>
                    <td>{{ order.totalUnits }}</td>
                    <td>{{ money(order.total, order.currency) }}</td>
                    <td>{{ statusLabel(order.status) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    }
  `,
})
export class OrdersPage {
  protected readonly auth = inject(Auth);
  private readonly baskets = inject(BasketService);

  protected readonly orders = signal<readonly OrderSummary[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly money = (amount: number, currency: string) => formatMoney({ amount, currency });
  protected readonly dateTime = (value: string) => formatDateTime(value);
  protected readonly statusLabel = (status: OrderSummary['status']) => ORDER_STATUS_LABELS[status];

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);

    try {
      const page = await this.baskets.getMyOrders();
      this.orders.set(page.items);
    } catch (cause) {
      this.error.set(message(cause));
    } finally {
      this.isLoading.set(false);
    }
  }
}

/**
 * One order, with its status timeline.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * The route parameter arrives as a signal `input()` because the route is configured with
 * `withComponentInputBinding()`. React reads it from `useParams`. Angular's version is typed, requires
 * no import, and updates automatically when the id changes — a genuine point for Angular's router.
 */
@Component({
  selector: 'app-order-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (auth.isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite"><p class="lede">Loading…</p></div>
    } @else if (!auth.isAuthenticated()) {
      <div class="centred">
        <div class="card stack">
          <h1 class="page-title">Order</h1>
          <p class="muted">Sign in to see this order.</p>
          <div>
            <button type="button" class="btn btn--primary" (click)="auth.signIn()">Sign in</button>
          </div>
        </div>
      </div>
    } @else if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading your order…</p>
      </div>
    } @else if (!order()) {
      <div class="centred">
        <div class="card stack" role="alert">
          <h1 class="page-title">Order not found</h1>
          <p class="muted">We could not find that order. It may belong to a different account.</p>
          <a class="btn btn--primary" routerLink="/orders">Your orders</a>
        </div>
      </div>
    } @else {
      <div class="stack">
        @if (justPlaced()) {
          <div class="card" role="status">
            <p class="lede">Thank you — your order is confirmed.</p>
            <p class="muted">We have sent the details to your email address.</p>
          </div>
        }

        <h1 class="page-title">Order {{ order()!.orderNumber }}</h1>

        <section class="card stack" aria-labelledby="status-heading">
          <h2 id="status-heading">Status</h2>

          <p class="lede" role="status">{{ statusLabel(order()!.status) }}</p>

          @if (order()!.status === 'Cancelled') {
            <p class="muted">{{ cancellationText() }}</p>
          } @else {
            <!-- An ordered list, because these steps have an order and a screen reader should announce
                 it. A row of styled divs would read as unrelated words. -->
            <ol class="timeline">
              @for (step of timeline; track step; let i = $index) {
                <li [class]="i <= reachedIndex() ? 'timeline__step is-done' : 'timeline__step'">
                  <span>{{ statusLabel(step) }}</span>
                  @if (i <= reachedIndex()) {
                    <span class="visually-hidden"> — completed</span>
                  }
                </li>
              }
            </ol>
          }

          @if (order()!.canBeCancelled) {
            <!-- Hidden once the aggregate says no. Hiding it is a courtesy; the server refusing it is
                 the actual rule — a dispatched order needs a return, not a cancellation. -->
            <div>
              <button
                type="button"
                class="btn btn--secondary"
                [disabled]="saving()"
                (click)="cancel()"
              >
                {{ saving() ? 'Cancelling…' : 'Cancel order' }}
              </button>
            </div>
          }

          @if (error()) {
            <p class="muted" role="alert">{{ error() }}</p>
          }
        </section>

        <section class="card stack" aria-labelledby="items-heading">
          <h2 id="items-heading">Items</h2>

          <table class="table">
            <thead>
              <tr>
                <th scope="col">Product</th>
                <th scope="col">Quantity</th>
                <th scope="col">Price</th>
                <th scope="col">Total</th>
              </tr>
            </thead>
            <tbody>
              @for (item of order()!.items; track item.productId) {
                <tr>
                  <th scope="row">{{ item.productName }}</th>
                  <td>{{ item.quantity }}</td>
                  <td>{{ money(item.unitPrice, order()!.currency) }}</td>
                  <td>{{ money(item.lineTotal, order()!.currency) }}</td>
                </tr>
              }
            </tbody>
          </table>

          <p class="lede">Total: {{ money(order()!.total, order()!.currency) }}</p>
        </section>

        <section class="card stack" aria-labelledby="delivery-heading">
          <h2 id="delivery-heading">Delivery address</h2>
          <address>
            {{ order()!.shippingAddress.recipient }}<br />
            {{ order()!.shippingAddress.line1 }}<br />
            @if (order()!.shippingAddress.line2) {
              {{ order()!.shippingAddress.line2 }}<br />
            }
            {{ order()!.shippingAddress.city }}<br />
            {{ order()!.shippingAddress.postcode }}<br />
            {{ order()!.shippingAddress.country }}
          </address>
          <p class="muted small">
            Recorded as it was when you ordered, so changing your address book will not alter this.
          </p>
        </section>
      </div>
    }
  `,
})
export class OrderDetailPage {
  /** Bound from the route by `withComponentInputBinding()`. */
  readonly id = input.required<string>();

  /** Set by checkout on redirect, so arriving straight from placing an order shows a confirmation. */
  readonly placed = input<string | undefined>(undefined);

  protected readonly auth = inject(Auth);
  private readonly baskets = inject(BasketService);

  protected readonly order = signal<Order | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly timeline = ORDER_TIMELINE;
  protected readonly money = (amount: number, currency: string) => formatMoney({ amount, currency });
  protected readonly statusLabel = (status: Order['status']) => ORDER_STATUS_LABELS[status];

  protected readonly justPlaced = computed(() => this.placed() === '1');

  protected readonly reachedIndex = computed(() => {
    const current = this.order();
    return current ? ORDER_TIMELINE.indexOf(current.status) : -1;
  });

  protected readonly cancellationText = computed(() => {
    const current = this.order();
    if (!current) return '';

    const reason = current.cancellationReason
      ? (ORDER_CANCELLATION_LABELS[current.cancellationReason] ?? 'This order was cancelled')
      : 'This order was cancelled';

    return current.cancelledAt
      ? `${reason} on ${formatDateTime(current.cancelledAt)}.`
      : `${reason}.`;
  });

  constructor() {
    // queueMicrotask, not a direct call. A required signal input is not populated until AFTER the
    // constructor runs, so reading this.id() here throws NG0950 - which the catch below would then
    // report to the customer as "order not found", hiding the real cause completely. Deferring by a
    // microtask lets the router bind the input first.
    //
    // Not an effect, because the component is recreated when the route id changes, so there is
    // nothing to react to. Same pattern as ProductDetailPage.
    queueMicrotask(() => void this.load());
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.order.set(await this.baskets.getOrder(this.id()));
    } catch {
      // A 404 and a network failure both mean "we cannot show you this order". The message stays
      // vague on purpose: confirming that an order exists but belongs to someone else is exactly the
      // information an attacker enumerating ids is looking for.
      this.order.set(null);
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async cancel(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);

    try {
      this.order.set(await this.baskets.cancelOrder(this.id()));
    } catch (cause) {
      this.error.set(message(cause));
    } finally {
      this.saving.set(false);
    }
  }
}

function message(cause: unknown): string {
  return cause instanceof Error ? cause.message : 'Something went wrong.';
}
