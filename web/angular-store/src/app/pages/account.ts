import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { Auth } from '../auth/auth';
import {
  COUNTRIES,
  CURRENCIES,
  LOCALES,
  ProfileService,
  type Address,
} from '../core/profile';

/**
 * My Account — profile, addresses and preferences.
 *
 * Behaviourally identical to the React `AccountPage`: same labels, same
 * sections, same states, so the shared Playwright specs pass against both.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * This is the first screen with real forms, and it is where Angular's
 * **reactive forms** earn their keep. React uses uncontrolled inputs read via
 * `FormData` for the two simple forms and a `useState` object for the address
 * draft — workable, but validation is manual and the types are hand-written.
 * Angular's `FormBuilder` gives a typed form group, declarative validators, and
 * `dirty`/`invalid` state for free.
 */
@Component({
  selector: 'app-account-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  template: `
    @if (auth.isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite"><p class="lede">Loading…</p></div>
    } @else if (!auth.isAuthenticated()) {
      <div class="centred">
        <div class="card stack">
          <h1 class="page-title">My account</h1>
          <p class="muted">Sign in to manage your profile, addresses and preferences.</p>
          <div><button type="button" class="btn btn--primary" (click)="auth.signIn()">Sign in</button></div>
        </div>
      </div>
    } @else if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading your account…</p>
      </div>
    } @else if (error(); as message) {
      <div class="centred">
        <div class="card stack" role="alert">
          <h1 class="page-title">Could not load your account</h1>
          <p class="muted">{{ message }}</p>
          <button type="button" class="btn btn--primary" (click)="reload()">Try again</button>
        </div>
      </div>
    } @else if (profiles.profile(); as profile) {
      <div class="stack">
        <h1 class="page-title">My account</h1>

        @if (banner(); as message) {
          <p class="badge badge--in-stock" role="status">{{ message }}</p>
        }

        <!-- Contact details -->
        <section class="card stack" aria-labelledby="contact-heading">
          <h2 id="contact-heading" style="margin-top: 0">Contact details</h2>

          <form class="stack" [formGroup]="contactForm" (ngSubmit)="saveContact()">
            <div class="filters">
              <div class="field">
                <label for="displayName">Display name</label>
                <input id="displayName" class="input" formControlName="displayName" maxlength="100" />
              </div>
              <div class="field">
                <label for="phoneNumber">Phone number</label>
                <input id="phoneNumber" class="input" formControlName="phoneNumber" maxlength="30" />
              </div>
              <div class="field">
                <label for="email">Email</label>
                <!-- Read-only: email is IDENTITY data owned by Keycloak, not
                     profile data owned by this service. See docs/adr/0004. -->
                <input id="email" class="input" [value]="profile.email ?? ''" readonly disabled />
              </div>
            </div>
            <div>
              <button type="submit" class="btn btn--primary" [disabled]="saving()">
                {{ saving() ? 'Saving…' : 'Save contact details' }}
              </button>
            </div>
          </form>
        </section>

        <!-- Addresses -->
        <section class="card stack" aria-labelledby="addresses-heading">
          <h2 id="addresses-heading" style="margin-top: 0">Addresses</h2>

          @if (profile.addresses.length === 0 && !showAddressForm()) {
            <p class="muted">You have no saved addresses yet.</p>
          }

          @if (profile.addresses.length > 0) {
            <ul class="grid grid--2 product-grid">
              @for (address of profile.addresses; track address.id) {
                <li class="card stack">
                  <div class="row">
                    <strong>{{ address.label }}</strong>
                    @if (address.isDefaultShipping) {
                      <span class="badge badge--info">Default shipping</span>
                    }
                    @if (address.isDefaultBilling) {
                      <span class="badge badge--info">Default billing</span>
                    }
                  </div>
                  <p class="muted" style="margin: 0">{{ formatAddress(address) }}</p>
                  <div class="row">
                    <button type="button" class="btn btn--secondary" (click)="startEdit(address)">Edit</button>
                    @if (!address.isDefaultShipping) {
                      <button type="button" class="btn btn--secondary" (click)="setDefault(address.id, 'shipping')">
                        Use for shipping
                      </button>
                    }
                    @if (!address.isDefaultBilling) {
                      <button type="button" class="btn btn--secondary" (click)="setDefault(address.id, 'billing')">
                        Use for billing
                      </button>
                    }
                    <button type="button" class="btn btn--secondary" (click)="remove(address.id)">Remove</button>
                  </div>
                </li>
              }
            </ul>
          }

          @if (!showAddressForm()) {
            <div><button type="button" class="btn btn--primary" (click)="startAdd()">Add address</button></div>
          } @else {
            <form
              class="stack"
              [formGroup]="addressForm"
              (ngSubmit)="saveAddress()"
              [attr.aria-label]="editingId() ? 'Edit address' : 'Add address'"
            >
              <div class="filters">
                <div class="field">
                  <label for="label">Label</label>
                  <input id="label" class="input" formControlName="label" maxlength="50" placeholder="Home, Work…" />
                </div>
                <div class="field">
                  <label for="line1">Address line 1</label>
                  <input id="line1" class="input" formControlName="line1" maxlength="200" />
                </div>
                <div class="field">
                  <label for="line2">Address line 2</label>
                  <input id="line2" class="input" formControlName="line2" maxlength="200" />
                </div>
                <div class="field">
                  <label for="city">City</label>
                  <input id="city" class="input" formControlName="city" maxlength="100" />
                </div>
                <div class="field">
                  <label for="postcode">Postcode</label>
                  <input id="postcode" class="input" formControlName="postcode" maxlength="20" />
                </div>
                <div class="field">
                  <label for="country">Country</label>
                  <select id="country" class="input" formControlName="country">
                    @for (country of countries; track country.code) {
                      <option [value]="country.code">{{ country.name }}</option>
                    }
                  </select>
                </div>
              </div>

              @if (addressError(); as message) {
                <p class="badge badge--out-of-stock" role="alert">{{ message }}</p>
              }

              <div class="row">
                <button type="submit" class="btn btn--primary" [disabled]="addressForm.invalid || saving()">
                  {{ saving() ? 'Saving…' : 'Save address' }}
                </button>
                <button type="button" class="btn btn--secondary" (click)="cancelAddress()">Cancel</button>
              </div>
            </form>
          }
        </section>

        <!-- Preferences -->
        <section class="card stack" aria-labelledby="preferences-heading">
          <h2 id="preferences-heading" style="margin-top: 0">Preferences</h2>

          <form class="stack" [formGroup]="preferencesForm" (ngSubmit)="savePreferences()">
            <div class="filters">
              <div class="field">
                <label for="locale">Language</label>
                <select id="locale" class="input" formControlName="locale">
                  @for (locale of locales; track locale.code) {
                    <option [value]="locale.code">{{ locale.name }}</option>
                  }
                </select>
              </div>
              <div class="field">
                <label for="currency">Currency</label>
                <select id="currency" class="input" formControlName="currency">
                  @for (currency of currencies; track currency.code) {
                    <option [value]="currency.code">{{ currency.name }}</option>
                  }
                </select>
              </div>
              <div class="field">
                <label for="theme">Theme</label>
                <select id="theme" class="input" formControlName="theme">
                  <option value="system">Follow system</option>
                  <option value="light">Light</option>
                  <option value="dark">Dark</option>
                </select>
              </div>
            </div>

            <!-- Real fieldset/legend, not a styled div: it groups the checkboxes
                 for a screen reader so each is announced with its group. -->
            <fieldset class="stack" style="border: 0; padding: 0; margin: 0">
              <legend class="muted">Order updates</legend>
              <div class="field field--checkbox">
                <input id="orderUpdatesEmail" type="checkbox" formControlName="orderUpdatesEmail" />
                <label for="orderUpdatesEmail">Email me about my orders</label>
              </div>
              <div class="field field--checkbox">
                <input id="orderUpdatesSms" type="checkbox" formControlName="orderUpdatesSms" />
                <label for="orderUpdatesSms">Text me about my orders</label>
              </div>
            </fieldset>

            <fieldset class="stack" style="border: 0; padding: 0; margin: 0">
              <legend class="muted">Marketing</legend>
              <!-- Separate from order updates on purpose: marketing needs opt-in
                   and can be withdrawn, an order confirmation is part of the
                   contract. Changing either records a consent entry server-side. -->
              <div class="field field--checkbox">
                <input id="marketingEmail" type="checkbox" formControlName="marketingEmail" />
                <label for="marketingEmail">Send me offers by email</label>
              </div>
              <div class="field field--checkbox">
                <input id="marketingSms" type="checkbox" formControlName="marketingSms" />
                <label for="marketingSms">Send me offers by text</label>
              </div>
            </fieldset>

            <div>
              <button type="submit" class="btn btn--primary" [disabled]="saving()">
                {{ saving() ? 'Saving…' : 'Save preferences' }}
              </button>
            </div>
          </form>
        </section>
      </div>
    }
  `,
})
export class AccountPage {
  protected readonly auth = inject(Auth);
  protected readonly profiles = inject(ProfileService);
  private readonly fb = inject(FormBuilder);

  protected readonly countries = COUNTRIES;
  protected readonly locales = LOCALES;
  protected readonly currencies = CURRENCIES;

  protected readonly isLoading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly addressError = signal<string | null>(null);
  protected readonly banner = signal<string | null>(null);
  protected readonly showAddressForm = signal(false);
  protected readonly editingId = signal<string | null>(null);

  // Typed reactive forms. Validators are declared once here rather than checked
  // by hand at submit time - the clearest advantage Angular has on this screen.
  protected readonly contactForm = this.fb.nonNullable.group({
    displayName: [''],
    phoneNumber: [''],
  });

  protected readonly addressForm = this.fb.nonNullable.group({
    label: ['', [Validators.required, Validators.maxLength(50)]],
    line1: ['', [Validators.required, Validators.maxLength(200)]],
    line2: [''],
    city: ['', [Validators.required, Validators.maxLength(100)]],
    postcode: ['', [Validators.required, Validators.maxLength(20)]],
    country: ['GB', Validators.required],
  });

  protected readonly preferencesForm = this.fb.nonNullable.group({
    locale: ['en-GB'],
    currency: ['GBP'],
    theme: ['system'],
    marketingEmail: [false],
    marketingSms: [false],
    orderUpdatesEmail: [true],
    orderUpdatesSms: [false],
  });

  constructor() {
    queueMicrotask(() => void this.load());
  }

  private async load(): Promise<void> {
    if (!this.auth.isAuthenticated()) {
      this.isLoading.set(false);
      return;
    }

    this.isLoading.set(true);
    this.error.set(null);

    try {
      await this.profiles.load();
      this.syncForms();
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Unexpected error');
    } finally {
      this.isLoading.set(false);
    }
  }

  private syncForms(): void {
    const profile = this.profiles.profile();
    if (!profile) return;

    this.contactForm.patchValue({
      displayName: profile.displayName ?? '',
      phoneNumber: profile.phoneNumber ?? '',
    });
    this.preferencesForm.patchValue(profile.preferences);
  }

  protected reload(): void {
    void this.load();
  }

  protected formatAddress(address: Address): string {
    return [address.line1, address.line2, address.city, address.postcode, address.country]
      .filter(Boolean)
      .join(', ');
  }

  protected async saveContact(): Promise<void> {
    await this.run(async () => {
      const { displayName, phoneNumber } = this.contactForm.getRawValue();
      await this.profiles.updateContact(displayName || null, phoneNumber || null);
      this.banner.set('Contact details saved');
    });
  }

  protected async savePreferences(): Promise<void> {
    await this.run(async () => {
      await this.profiles.updatePreferences(this.preferencesForm.getRawValue());
      this.banner.set('Preferences saved');
    });
  }

  protected startAdd(): void {
    this.editingId.set(null);
    this.addressForm.reset({ country: 'GB' });
    this.addressError.set(null);
    this.showAddressForm.set(true);
  }

  protected startEdit(address: Address): void {
    this.editingId.set(address.id);
    this.addressForm.setValue({
      label: address.label,
      line1: address.line1,
      line2: address.line2 ?? '',
      city: address.city,
      postcode: address.postcode,
      country: address.country,
    });
    this.addressError.set(null);
    this.showAddressForm.set(true);
  }

  protected cancelAddress(): void {
    this.showAddressForm.set(false);
    this.editingId.set(null);
  }

  protected async saveAddress(): Promise<void> {
    if (this.addressForm.invalid) return;

    this.addressError.set(null);
    const value = this.addressForm.getRawValue();
    const id = this.editingId();

    try {
      this.saving.set(true);
      if (id) {
        await this.profiles.updateAddress(id, value);
      } else {
        await this.profiles.addAddress(value);
      }
      this.showAddressForm.set(false);
      this.editingId.set(null);
      this.banner.set('Address saved');
    } catch (cause) {
      this.addressError.set(cause instanceof Error ? cause.message : 'Could not save the address');
    } finally {
      this.saving.set(false);
    }
  }

  protected async remove(id: string): Promise<void> {
    await this.run(async () => {
      await this.profiles.removeAddress(id);
      this.banner.set('Address removed');
    });
  }

  protected async setDefault(id: string, kind: 'shipping' | 'billing'): Promise<void> {
    await this.run(() => this.profiles.setDefault(id, kind));
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.saving.set(true);
    this.error.set(null);

    try {
      await action();
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Unexpected error');
    } finally {
      this.saving.set(false);
    }
  }
}
