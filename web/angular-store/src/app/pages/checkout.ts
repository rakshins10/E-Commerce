import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { Auth } from '../auth/auth';
import { BasketService } from '../core/basket';
import { formatMoney } from '../core/formatting';
import { COUNTRIES, ProfileService } from '../core/profile';

/**
 * Checkout: confirm where it goes, then place the order.
 *
 * ---
 * **The address is copied, not linked.** Picking a saved address fills the form; what is sent is the
 * text, and the order stores its own snapshot. A customer who moves house next year must not silently
 * rewrite where last year's parcel was sent.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * Six address fields is where reactive forms pull ahead most visibly. React wires each input by hand
 * with a `value`, an `onChange` and an object spread, and derives "can submit" from four manual
 * `trim() !== ''` checks. Here the validators are declared once as data and `form.invalid` is the
 * whole answer.
 */
@Component({
  selector: 'app-checkout-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    @if (auth.isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite"><p class="lede">Loading…</p></div>
    } @else if (!auth.isAuthenticated()) {
      <div class="centred">
        <div class="card stack">
          <h1 class="page-title">Checkout</h1>
          <p class="muted">Sign in to place an order.</p>
          <div>
            <button type="button" class="btn btn--primary" (click)="auth.signIn()">Sign in</button>
          </div>
        </div>
      </div>
    } @else if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading your basket…</p>
      </div>
    } @else if (!basket() || basket()!.items.length === 0) {
      <div class="centred">
        <div class="card stack">
          <h1 class="page-title">Checkout</h1>
          <p class="lede">Your basket is empty.</p>
          <div><a class="btn btn--primary" routerLink="/products">Browse products</a></div>
        </div>
      </div>
    } @else {
      <div class="stack">
        <h1 class="page-title">Checkout</h1>

        @if (error()) {
          <div class="card" role="alert"><p class="muted">{{ error() }}</p></div>
        }

        <form class="stack" [formGroup]="form" (ngSubmit)="placeOrder()">
          <section class="card stack" aria-labelledby="delivery-heading">
            <h2 id="delivery-heading">Delivery address</h2>

            <div class="field">
              <label for="recipient">Recipient</label>
              <input id="recipient" class="input" formControlName="recipient" />
            </div>

            <div class="field">
              <label for="line1">Address line 1</label>
              <input id="line1" class="input" formControlName="line1" />
            </div>

            <div class="field">
              <label for="line2">Address line 2</label>
              <input id="line2" class="input" formControlName="line2" />
            </div>

            <div class="field">
              <label for="city">City</label>
              <input id="city" class="input" formControlName="city" />
            </div>

            <div class="field">
              <label for="postcode">Postcode</label>
              <input id="postcode" class="input" formControlName="postcode" />
            </div>

            <div class="field">
              <label for="country">Country</label>
              <select id="country" class="input" formControlName="country">
                @for (country of countries; track country.code) {
                  <option [value]="country.code">{{ country.name }}</option>
                }
              </select>
            </div>
          </section>

          <section class="card stack" aria-labelledby="summary-heading">
            <h2 id="summary-heading">Order summary</h2>

            <ul class="plain-list">
              @for (item of basket()!.items; track item.productId) {
                <li>
                  {{ item.productName }} × {{ item.quantity }} —
                  {{ money(item.lineTotal, item.currency) }}
                </li>
              }
            </ul>

            <p class="lede">
              Estimated total: {{ money(basket()!.estimatedTotal, basket()!.currency) }}
            </p>

            <p class="muted small">
              We confirm every price against the catalogue when you place your order, so the amount
              charged may differ from the estimate above.
            </p>

            <div>
              <button type="submit" class="btn btn--primary" [disabled]="form.invalid || saving()">
                {{ saving() ? 'Placing your order…' : 'Place order' }}
              </button>
            </div>
          </section>
        </form>
      </div>
    }
  `,
})
export class CheckoutPage {
  protected readonly auth = inject(Auth);
  private readonly baskets = inject(BasketService);
  private readonly profiles = inject(ProfileService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  protected readonly basket = this.baskets.basket;
  protected readonly countries = COUNTRIES;
  protected readonly isLoading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly money = (amount: number, currency: string) =>
    formatMoney({ amount, currency });

  // Typed, with the validators declared once as data. `form.invalid` then answers "can submit"
  // for the whole form, rather than four hand-written emptiness checks that must be kept in step.
  protected readonly form = this.fb.nonNullable.group({
    recipient: ['', [Validators.required, Validators.maxLength(200)]],
    line1: ['', [Validators.required, Validators.maxLength(200)]],
    line2: ['', Validators.maxLength(200)],
    city: ['', [Validators.required, Validators.maxLength(100)]],
    postcode: ['', [Validators.required, Validators.maxLength(20)]],
    country: ['GB', Validators.required],
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      await this.baskets.load();

      // Prefill from the saved default. A customer who has already told us where they live should not
      // have to type it again - the address book exists precisely so they do not.
      await this.profiles.load();

      const profile = this.profiles.profile();
      const preferred =
        profile?.addresses.find((address) => address.isDefaultShipping) ?? profile?.addresses[0];

      if (preferred && this.form.controls.line1.pristine) {
        this.form.patchValue({
          recipient: profile?.displayName ?? '',
          line1: preferred.line1,
          line2: preferred.line2 ?? '',
          city: preferred.city,
          postcode: preferred.postcode,
          country: preferred.country,
        });
      }
    } catch (cause) {
      this.error.set(message(cause));
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async placeOrder(): Promise<void> {
    if (this.form.invalid) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    try {
      const value = this.form.getRawValue();

      const order = await this.baskets.placeOrder({
        shippingAddress: {
          recipient: value.recipient,
          line1: value.line1,
          line2: value.line2 === '' ? null : value.line2,
          city: value.city,
          postcode: value.postcode,
          country: value.country,
        },
        currency: this.basket()?.currency ?? 'GBP',
      });

      // The basket is gone server-side, so clear it locally too rather than leaving the header
      // showing items the customer has just bought.
      await this.baskets.load();

      // replaceUrl, so the Back button does not return to a checkout page whose basket is now empty,
      // where pressing "Place order" again would fail confusingly.
      await this.router.navigate(['/orders', order.id], {
        queryParams: { placed: '1' },
        replaceUrl: true,
      });
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
