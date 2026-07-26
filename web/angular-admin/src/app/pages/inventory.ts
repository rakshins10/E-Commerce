import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { Auth } from '../auth/auth';
import { AdminApi } from '../core/admin-api';
import { Permissions } from '../core/permissions';
import type { StockItem } from '../core/admin-types';

/**
 * Stock levels, and manual adjustments.
 *
 * Behaviourally identical to the React `InventoryPage`.
 */
@Component({
  selector: 'app-inventory-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  template: `
    @if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading stock…</p>
      </div>
    } @else if (error() && !items().length) {
      <div class="card stack" role="alert">
        <h2>Could not load this</h2>
        <p class="muted">{{ error() }}</p>
        <div><button type="button" class="btn btn--primary" (click)="reload()">Try again</button></div>
      </div>
    } @else {
      <div class="stack">
        <h1 class="page-title">Inventory</h1>

        <p class="muted small">
          On hand is what is physically on the shelf. Reserved is spoken for by an order that has not
          shipped — still on the shelf. Available is what a new order may take.
        </p>

        @if (adjusting(); as sku) {
          <form class="card stack" [formGroup]="form" (ngSubmit)="save(sku)">
            <h2>Adjust {{ sku }}</h2>

            <div class="field">
              <label for="delta">Change (negative to reduce)</label>
              <input id="delta" type="number" class="input input--narrow" formControlName="delta" />
            </div>

            <div class="field">
              <label for="reason">Reason</label>
              <!-- Required, because an unexplained stock movement is impossible to audit later. -->
              <input
                id="reason"
                class="input"
                formControlName="reason"
                placeholder="Goods in, damage, stock take…"
              />
            </div>

            <div class="row">
              <button type="submit" class="btn btn--primary" [disabled]="form.invalid || saving()">
                Save adjustment
              </button>
              <button type="button" class="btn btn--secondary" (click)="adjusting.set(null)">
                Cancel
              </button>
            </div>

            @if (error()) {
              <p class="muted" role="alert">{{ error() }}</p>
            }
          </form>
        }

        @if (items().length === 0) {
          <div class="card"><p class="muted">No stock records.</p></div>
        } @else {
          <div class="card">
            <table class="table">
              <caption class="visually-hidden">Stock levels, most constrained first</caption>
              <thead>
                <tr>
                  <th scope="col">SKU</th>
                  <th scope="col">Product</th>
                  <th scope="col" style="text-align: right">On hand</th>
                  <th scope="col" style="text-align: right">Reserved</th>
                  <th scope="col" style="text-align: right">Available</th>
                  @if (auth.can(canAdjust)) {
                    <th scope="col">Adjust</th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (item of items(); track item.sku) {
                  <tr>
                    <th scope="row">{{ item.sku }}</th>
                    <td>{{ item.productName }}</td>
                    <td style="text-align: right">{{ item.onHand }}</td>
                    <td style="text-align: right">{{ item.reserved }}</td>
                    <td style="text-align: right">
                      {{ item.available }}
                      @if (item.available <= item.reorderLevel) {
                        <!-- Text, not just colour. WCAG 1.4.1 - a red number is invisible to a
                             colour-blind user, so the word carries the meaning. -->
                        <span class="badge badge--low"> Low</span>
                      }
                    </td>
                    @if (auth.can(canAdjust)) {
                      <td>
                        <button type="button" class="btn btn--secondary" (click)="startAdjust(item)">
                          <span aria-hidden="true">Adjust</span>
                          <span class="visually-hidden">Adjust stock for {{ item.productName }}</span>
                        </button>
                      </td>
                    }
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
export class InventoryPage {
  protected readonly auth = inject(Auth);
  private readonly api = inject(AdminApi);
  private readonly fb = inject(FormBuilder);

  protected readonly items = signal<readonly StockItem[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly adjusting = signal<string | null>(null);

  protected readonly canAdjust = Permissions.Inventory.Adjust;

  protected readonly form = this.fb.nonNullable.group({
    delta: [0, Validators.required],
    reason: ['', [Validators.required, Validators.maxLength(200)]],
  });

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);

    try {
      this.items.set(await this.api.getStock());
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Something went wrong.');
    } finally {
      this.isLoading.set(false);
    }
  }

  protected startAdjust(item: StockItem): void {
    this.adjusting.set(item.sku);
    this.form.reset({ delta: 0, reason: '' });
  }

  protected async save(sku: string): Promise<void> {
    if (this.form.invalid) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    try {
      const value = this.form.getRawValue();
      await this.api.adjustStock(sku, value.delta, value.reason);
      this.adjusting.set(null);
      await this.reload();
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Something went wrong.');
    } finally {
      this.saving.set(false);
    }
  }
}
