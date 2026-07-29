import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../environments/environment';

/**
 * My Account API types and access.
 *
 * Owned by this application — the React storefront has its own equivalent in
 * `src/lib/profile.ts`. See docs/adr/0018-self-contained-frontends.md.
 */

export interface Address {
  readonly id: string;
  readonly label: string;
  readonly line1: string;
  readonly line2: string | null;
  readonly city: string;
  readonly postcode: string;
  readonly country: string;
  readonly isDefaultShipping: boolean;
  readonly isDefaultBilling: boolean;
}

export interface Preferences {
  readonly locale: string;
  readonly currency: string;
  readonly theme: string;
  readonly marketingEmail: boolean;
  readonly marketingSms: boolean;
  readonly orderUpdatesEmail: boolean;
  readonly orderUpdatesSms: boolean;
}

export interface Profile {
  readonly id: string;
  readonly subject: string;
  readonly email: string | null;
  readonly displayName: string | null;
  readonly phoneNumber: string | null;
  readonly preferences: Preferences;
  readonly addresses: readonly Address[];
}

export interface SaveAddressRequest {
  readonly label: string;
  readonly line1: string;
  readonly line2?: string | null;
  readonly city: string;
  readonly postcode: string;
  readonly country: string;
}

export const COUNTRIES = [
  { code: 'GB', name: 'United Kingdom' },
  { code: 'IE', name: 'Ireland' },
  { code: 'FR', name: 'France' },
  { code: 'DE', name: 'Germany' },
  { code: 'ES', name: 'Spain' },
  { code: 'IN', name: 'India' },
  { code: 'US', name: 'United States' },
] as const;

export const LOCALES = [
  { code: 'en-GB', name: 'English (UK)' },
  { code: 'en-US', name: 'English (US)' },
  { code: 'fr-FR', name: 'Français' },
  { code: 'de-DE', name: 'Deutsch' },
] as const;

export const CURRENCIES = [
  { code: 'GBP', name: 'British Pound (£)' },
  { code: 'EUR', name: 'Euro (€)' },
  { code: 'USD', name: 'US Dollar ($)' },
  { code: 'INR', name: 'Indian Rupee (₹)' },
] as const;

/**
 * Profile data access.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * React uses TanStack Query mutations with `onSuccess` writing the returned
 * profile back into the cache. Angular holds the profile in a single signal that
 * every operation replaces — which is simpler here, because every endpoint
 * returns the *whole* updated profile and there is only one consumer.
 *
 * Note the auth token: Angular's `HttpClient` needs an interceptor to attach it
 * (see `core/auth-interceptor.ts`), where React's client takes a token getter.
 * Angular's is more ceremony but applies automatically to every request, which
 * is harder to forget.
 */
@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.bffBaseUrl}/api/profile/me`;

  /** The current profile. Replaced wholesale by every mutation. */
  readonly profile = signal<Profile | null>(null);

  async load(): Promise<void> {
    this.profile.set(await firstValueFrom(this.http.get<Profile>(this.baseUrl)));
  }

  async updateContact(displayName: string | null, phoneNumber: string | null): Promise<void> {
    this.profile.set(
      await firstValueFrom(
        this.http.put<Profile>(`${this.baseUrl}/contact`, { displayName, phoneNumber }),
      ),
    );
  }

  async updatePreferences(preferences: Preferences): Promise<void> {
    this.profile.set(
      await firstValueFrom(this.http.put<Profile>(`${this.baseUrl}/preferences`, preferences)),
    );
  }

  async addAddress(address: SaveAddressRequest): Promise<void> {
    this.profile.set(
      await firstValueFrom(this.http.post<Profile>(`${this.baseUrl}/addresses`, address)),
    );
  }

  async updateAddress(id: string, address: SaveAddressRequest): Promise<void> {
    this.profile.set(
      await firstValueFrom(this.http.put<Profile>(`${this.baseUrl}/addresses/${id}`, address)),
    );
  }

  async removeAddress(id: string): Promise<void> {
    this.profile.set(
      await firstValueFrom(this.http.delete<Profile>(`${this.baseUrl}/addresses/${id}`)),
    );
  }

  async setDefault(id: string, kind: 'shipping' | 'billing'): Promise<void> {
    this.profile.set(
      await firstValueFrom(
        this.http.post<Profile>(`${this.baseUrl}/addresses/${id}/default-${kind}`, {}),
      ),
    );
  }
}
