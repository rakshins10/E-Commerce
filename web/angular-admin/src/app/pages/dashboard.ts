import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';

import { AdminApi } from '../core/admin-api';
import { formatMoney } from '../core/formatting';
import type { Dashboard } from '../core/admin-types';

/**
 * Back-office dashboard.
 *
 * Behaviourally identical to the React `DashboardPage`, so the shared Playwright specs pass against
 * both.
 */
@Component({
  selector: 'app-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading the dashboard…</p>
      </div>
    } @else if (error()) {
      <div class="card stack" role="alert">
        <h2>Could not load this</h2>
        <p class="muted">{{ error() }}</p>
        <div><button type="button" class="btn btn--primary" (click)="reload()">Try again</button></div>
      </div>
    } @else if (data(); as d) {
      <div class="stack">
        <h1 class="page-title">Dashboard</h1>

        <div class="stat-grid">
          <div class="card stat">
            <div class="stat__label">Orders today</div>
            <div class="stat__value">{{ d.ordersToday }}</div>
          </div>
          <div class="card stat">
            <div class="stat__label">Revenue today</div>
            <div class="stat__value">{{ money(d.revenueToday, d.currency) }}</div>
          </div>
          <div class="card stat">
            <div class="stat__label">Orders in flight</div>
            <div class="stat__value">{{ d.ordersInFlight }}</div>
          </div>
          <div class="card stat">
            <div class="stat__label">Cancelled</div>
            <div class="stat__value">{{ d.ordersCancelled }}</div>
          </div>

          <!-- The two that matter operationally are marked, because a dashboard where every figure
               looks equally important is a dashboard nobody reads. -->
          <div [class]="d.sagasStuck > 0 ? 'card stat stat--danger' : 'card stat'">
            <div class="stat__label">Sagas stuck</div>
            <div class="stat__value">{{ d.sagasStuck }}</div>
          </div>
          <div [class]="d.lowStockItems > 0 ? 'card stat stat--warning' : 'card stat'">
            <div class="stat__label">Low stock items</div>
            <div class="stat__value">{{ d.lowStockItems }}</div>
          </div>
        </div>

        <section class="stack" aria-labelledby="by-status-heading">
          <h2 id="by-status-heading">Orders by status</h2>

          @if (d.byStatus.length === 0) {
            <div class="card"><p class="muted">No orders yet.</p></div>
          } @else {
            <div class="card">
              <table class="table">
                <caption class="visually-hidden">Order counts by status</caption>
                <thead>
                  <tr>
                    <th scope="col">Status</th>
                    <th scope="col" style="text-align: right">Count</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of d.byStatus; track row.status) {
                    <tr>
                      <th scope="row">{{ row.status }}</th>
                      <td style="text-align: right">{{ row.count }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>
      </div>
    }
  `,
})
export class DashboardPage {
  private readonly api = inject(AdminApi);

  protected readonly data = signal<Dashboard | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly money = (amount: number, currency: string) => formatMoney({ amount, currency });

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);

    try {
      this.data.set(await this.api.getDashboard());
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Something went wrong.');
    } finally {
      this.isLoading.set(false);
    }
  }
}
