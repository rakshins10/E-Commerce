/**
 * My Account API types and calls.
 *
 * Owned by this application — the Angular storefront has its own equivalent in
 * `core/profile.ts`. See docs/adr/0018-self-contained-frontends.md.
 *
 * Unlike catalog browsing, every call here is **authenticated**, so the client
 * attaches the access token.
 */

import { ApiClient } from './api-client';

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
  readonly isDefaultShipping?: boolean;
  readonly isDefaultBilling?: boolean;
}

/**
 * Creates a profile client bound to the current access token.
 *
 * Takes a token *getter* rather than a token, because tokens expire and rotate —
 * capturing one at construction would attach a stale token to every later call.
 */
export function createProfileApi(getAccessToken: () => string | null) {
  const client = new ApiClient({
    baseUrl: import.meta.env.VITE_BFF_URL ?? 'http://localhost:6001',
    getAccessToken,
  });

  return {
    get: (signal?: AbortSignal) => client.get<Profile>('/api/profile/me', { signal }),

    updateContact: (body: { displayName: string | null; phoneNumber: string | null }) =>
      client.put<Profile>('/api/profile/me/contact', body),

    updatePreferences: (body: Preferences) =>
      client.put<Profile>('/api/profile/me/preferences', body),

    addAddress: (body: SaveAddressRequest) =>
      client.post<Profile>('/api/profile/me/addresses', body),

    updateAddress: (id: string, body: SaveAddressRequest) =>
      client.put<Profile>(`/api/profile/me/addresses/${id}`, body),

    removeAddress: (id: string) => client.delete<Profile>(`/api/profile/me/addresses/${id}`),

    setDefaultShipping: (id: string) =>
      client.post<Profile>(`/api/profile/me/addresses/${id}/default-shipping`),

    setDefaultBilling: (id: string) =>
      client.post<Profile>(`/api/profile/me/addresses/${id}/default-billing`),
  };
}

/** Countries offered in the address form. Deliberately short — this is a demo shop. */
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
