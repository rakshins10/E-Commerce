import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Auth } from '../auth/auth';
import { AdminApi } from '../core/admin-api';
import { formatDateTime, formatMoney } from '../core/formatting';
import { Permissions } from '../core/permissions';
import type { AdminOrder, AdminOrderSummary, SagaTimeline } from '../core/admin-types';

/** All orders. Staff see everyone's, because their token carries `order:read`. */
@Component({
  selector: 'app-admin-orders-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading orders…</p>
      </div>
    } @else if (error()) {
      <div class="card stack" role="alert">
        <h2>Could not load this</h2>
        <p class="muted">{{ error() }}</p>
        <div><button type="button" class="btn btn--primary" (click)="reload()">Try again</button></div>
      </div>
    } @else {
      <div class="stack">
        <h1 class="page-title">Orders</h1>

        @if (orders().length === 0) {
          <div class="card"><p class="muted">No orders have been placed yet.</p></div>
        } @else {
          <!-- Rendered here rather than through the shared DataTable, because the first cell is a
               LINK. Angular has no equivalent of React's "a function that returns markup" - see
               components/data-table.ts. -->
          <div class="card">
            <table class="table">
              <caption class="visually-hidden">All orders, newest first</caption>
              <thead>
                <tr>
                  <th scope="col">Order</th>
                  <th scope="col">Placed</th>
                  <th scope="col">Status</th>
                  <th scope="col" style="text-align: right">Units</th>
                  <th scope="col" style="text-align: right">Total</th>
                </tr>
              </thead>
              <tbody>
                @for (order of orders(); track order.id) {
                  <tr>
                    <th scope="row"><a [routerLink]="['/orders', order.id]">{{ order.orderNumber }}</a></th>
                    <td>{{ dateTime(order.placedAt) }}</td>
                    <td>{{ order.status }}</td>
                    <td style="text-align: right">{{ order.totalUnits }}</td>
                    <td style="text-align: right">{{ money(order.total, order.currency) }}</td>
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
export class AdminOrdersPage {
  private readonly api = inject(AdminApi);

  protected readonly orders = signal<readonly AdminOrderSummary[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly money = (amount: number, currency: string) => formatMoney({ amount, currency });
  protected readonly dateTime = (value: string) => formatDateTime(value);

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);

    try {
      this.orders.set((await this.api.getOrders()).items);
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Something went wrong.');
    } finally {
      this.isLoading.set(false);
    }
  }
}

/** One order, with the saga's own step names for staff diagnosing a failure. */
@Component({
  selector: 'app-admin-order-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading the order…</p>
      </div>
    } @else if (!order()) {
      <div class="card stack" role="alert">
        <h2>Could not load this</h2>
        <p class="muted">{{ error() ?? 'Order not found.' }}</p>
      </div>
    } @else if (order(); as o) {
      <div class="stack">
        <h1 class="page-title">Order {{ o.orderNumber }}</h1>

        <section class="card stack" aria-labelledby="status-heading">
          <h2 id="status-heading">Status</h2>
          <p class="lede" role="status">
            {{ o.status }}{{ o.cancellationReason ? ' — ' + o.cancellationReason : '' }}
          </p>

          <!-- Each action is gated on its own permission AND on the aggregate's own answer. The server
               already decided what is legal; the client only honours it. -->
          <div class="row">
            @if (auth.can(canCancel) && o.status === 'Paid') {
              <button type="button" class="btn btn--primary" [disabled]="saving()" (click)="advance('ship')">
                Mark shipped
              </button>
            }
            @if (auth.can(canCancel) && o.status === 'Shipped') {
              <button type="button" class="btn btn--primary" [disabled]="saving()" (click)="advance('deliver')">
                Mark delivered
              </button>
            }
            @if (auth.can(canCancel) && o.canBeCancelled) {
              <button type="button" class="btn btn--secondary" [disabled]="saving()" (click)="advance('cancel')">
                Cancel order
              </button>
            }
          </div>

          @if (error()) {
            <p class="muted" role="alert">{{ error() }}</p>
          }
        </section>

        @if (saga(); as timeline) {
          @if (timeline.steps.length > 0) {
            <section class="card stack" aria-labelledby="saga-heading">
              <h2 id="saga-heading">Checkout process</h2>

              <!-- The step names are NOT softened here, unlike the storefront. Staff diagnosing a
                   failure want the real names - "CompensatingReleaseStock" is precise, and translating
                   it loses the signal that this was a compensation. -->
              <ol class="plain-list">
                @for (step of timeline.steps; track $index) {
                  <li>
                    <strong>{{ step.name }}</strong> — {{ step.detail }}
                    <span class="muted small"> · {{ dateTime(step.occurredAt) }}</span>
                  </li>
                }
              </ol>

              @if (timeline.failureReason) {
                <p class="muted">Failure: {{ timeline.failureReason }}</p>
              }
            </section>
          }
        }

        <section class="stack" aria-labelledby="items-heading">
          <h2 id="items-heading">Items</h2>

          <div class="card">
            <table class="table">
              <caption class="visually-hidden">Items on order {{ o.orderNumber }}</caption>
              <thead>
                <tr>
                  <th scope="col">Product</th>
                  <th scope="col">SKU</th>
                  <th scope="col" style="text-align: right">Qty</th>
                  <th scope="col" style="text-align: right">Unit price</th>
                  <th scope="col" style="text-align: right">Line total</th>
                </tr>
              </thead>
              <tbody>
                @for (item of o.items; track item.productId) {
                  <tr>
                    <th scope="row">{{ item.productName }}</th>
                    <td>{{ item.sku }}</td>
                    <td style="text-align: right">{{ item.quantity }}</td>
                    <td style="text-align: right">{{ money(item.unitPrice, o.currency) }}</td>
                    <td style="text-align: right">{{ money(item.lineTotal, o.currency) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <p class="lede">Total: {{ money(o.total, o.currency) }}</p>
        </section>

        <section class="card stack" aria-labelledby="delivery-heading">
          <h2 id="delivery-heading">Delivery address</h2>
          <address>
            {{ o.shippingAddress.recipient }}<br />
            {{ o.shippingAddress.line1 }}<br />
            {{ o.shippingAddress.city }}, {{ o.shippingAddress.postcode }}<br />
            {{ o.shippingAddress.country }}
          </address>
        </section>
      </div>
    }
  `,
})
export class AdminOrderDetailPage {
  readonly id = input.required<string>();

  protected readonly auth = inject(Auth);
  private readonly api = inject(AdminApi);

  protected readonly order = signal<AdminOrder | null>(null);
  protected readonly saga = signal<SagaTimeline | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly canCancel = Permissions.Order.Cancel;
  protected readonly money = (amount: number, currency: string) => formatMoney({ amount, currency });
  protected readonly dateTime = (value: string) => formatDateTime(value);

  constructor() {
    // A required signal input is not populated until AFTER the constructor, so reading this.id() here
    // directly throws NG0950. See docs/react-vs-angular.md.
    queueMicrotask(() => void this.load());
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.order.set(await this.api.getOrder(this.id()));

      try {
        this.saga.set(await this.api.getSagaTimeline(this.id()));
      } catch {
        // No saga is not an error worth showing: orders placed before Phase 7 have none.
        this.saga.set(null);
      }
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Something went wrong.');
      this.order.set(null);
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async advance(action: 'ship' | 'deliver' | 'cancel'): Promise<void> {
    this.saving.set(true);
    this.error.set(null);

    try {
      const updated =
        action === 'ship'
          ? await this.api.shipOrder(this.id())
          : action === 'deliver'
            ? await this.api.deliverOrder(this.id())
            : await this.api.cancelOrder(this.id());

      this.order.set(updated);
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Something went wrong.');
    } finally {
      this.saving.set(false);
    }
  }
}
