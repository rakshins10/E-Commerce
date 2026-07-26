import { useMemo, useState } from 'react';
import { useAuth } from 'react-oidc-context';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { useCurrentUser } from '../auth/useCurrentUser';
import {
  COUNTRIES,
  CURRENCIES,
  LOCALES,
  createProfileApi,
  type Address,
  type Preferences,
  type SaveAddressRequest,
} from '../lib/profile';

const EMPTY_ADDRESS: SaveAddressRequest = {
  label: '',
  line1: '',
  line2: '',
  city: '',
  postcode: '',
  country: 'GB',
};

/**
 * My Account — profile, addresses and preferences.
 *
 * ---
 * **The permissions this screen relies on are UI convenience only.** The server
 * scopes every call to the caller's own `sub` claim, so there is no id to tamper
 * with. Hiding a control here stops an honest user attempting something that
 * would fail; it stops a dishonest one from nothing.
 *
 * **Mutations invalidate the query rather than patching local state.** Every
 * endpoint returns the whole updated profile, so the server stays the single
 * source of truth — and the default-address invariant (adding a default
 * shipping address removes the flag from the previous one) arrives correctly
 * without the client having to replicate that rule.
 */
export function AccountPage() {
  const auth = useAuth();
  const { isAuthenticated, isLoading: authLoading } = useCurrentUser();
  const queryClient = useQueryClient();

  const api = useMemo(
    () => createProfileApi(() => auth.user?.access_token ?? null),
    [auth.user?.access_token],
  );

  const profileQuery = useQuery({
    queryKey: ['profile'],
    queryFn: ({ signal }) => api.get(signal),
    enabled: isAuthenticated,
  });

  const [editingAddress, setEditingAddress] = useState<string | null>(null);
  const [addressDraft, setAddressDraft] = useState<SaveAddressRequest>(EMPTY_ADDRESS);
  const [showAddressForm, setShowAddressForm] = useState(false);
  const [banner, setBanner] = useState<string | null>(null);

  const contactMutation = useMutation({
    mutationFn: (body: { displayName: string | null; phoneNumber: string | null }) =>
      api.updateContact(body),
    onSuccess: (updated) => {
      queryClient.setQueryData(['profile'], updated);
      setBanner('Contact details saved');
    },
  });

  const preferencesMutation = useMutation({
    mutationFn: (body: Preferences) => api.updatePreferences(body),
    onSuccess: (updated) => {
      queryClient.setQueryData(['profile'], updated);
      setBanner('Preferences saved');
    },
  });

  const addressMutation = useMutation({
    mutationFn: (input: { id: string | null; body: SaveAddressRequest }) =>
      input.id ? api.updateAddress(input.id, input.body) : api.addAddress(input.body),
    onSuccess: (updated) => {
      queryClient.setQueryData(['profile'], updated);
      setShowAddressForm(false);
      setEditingAddress(null);
      setAddressDraft(EMPTY_ADDRESS);
      setBanner('Address saved');
    },
  });

  const removeMutation = useMutation({
    mutationFn: (id: string) => api.removeAddress(id),
    onSuccess: (updated) => {
      queryClient.setQueryData(['profile'], updated);
      setBanner('Address removed');
    },
  });

  const defaultMutation = useMutation({
    mutationFn: (input: { id: string; kind: 'shipping' | 'billing' }) =>
      input.kind === 'shipping' ? api.setDefaultShipping(input.id) : api.setDefaultBilling(input.id),
    onSuccess: (updated) => queryClient.setQueryData(['profile'], updated),
  });

  if (authLoading) {
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Loading…</p>
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="centred">
        <div className="card stack">
          <h1 className="page-title">My account</h1>
          <p className="muted">Sign in to manage your profile, addresses and preferences.</p>
          <div>
            <button type="button" className="btn btn--primary" onClick={() => void auth.signinRedirect()}>
              Sign in
            </button>
          </div>
        </div>
      </div>
    );
  }

  if (profileQuery.isPending) {
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Loading your account…</p>
      </div>
    );
  }

  if (profileQuery.isError) {
    return (
      <div className="centred">
        <div className="card stack" role="alert">
          <h1 className="page-title">Could not load your account</h1>
          <p className="muted">{(profileQuery.error as Error).message}</p>
          <button type="button" className="btn btn--primary" onClick={() => profileQuery.refetch()}>
            Try again
          </button>
        </div>
      </div>
    );
  }

  const profile = profileQuery.data;

  function startEdit(address: Address) {
    setEditingAddress(address.id);
    setAddressDraft({
      label: address.label,
      line1: address.line1,
      line2: address.line2 ?? '',
      city: address.city,
      postcode: address.postcode,
      country: address.country,
    });
    setShowAddressForm(true);
  }

  return (
    <div className="stack">
      <h1 className="page-title">My account</h1>

      {/* role="status" so a saved change is announced, not just shown. */}
      {banner && (
        <p className="badge badge--in-stock" role="status">
          {banner}
        </p>
      )}

      {/* --- Contact details --- */}
      <section className="card stack" aria-labelledby="contact-heading">
        <h2 id="contact-heading" style={{ marginTop: 0 }}>
          Contact details
        </h2>

        <form
          className="stack"
          onSubmit={(event) => {
            event.preventDefault();
            const form = new FormData(event.currentTarget);
            contactMutation.mutate({
              displayName: (form.get('displayName') as string) || null,
              phoneNumber: (form.get('phoneNumber') as string) || null,
            });
          }}
        >
          <div className="filters">
            <div className="field">
              <label htmlFor="displayName">Display name</label>
              <input
                id="displayName"
                name="displayName"
                className="input"
                defaultValue={profile.displayName ?? ''}
                maxLength={100}
              />
            </div>

            <div className="field">
              <label htmlFor="phoneNumber">Phone number</label>
              <input
                id="phoneNumber"
                name="phoneNumber"
                className="input"
                defaultValue={profile.phoneNumber ?? ''}
                maxLength={30}
              />
            </div>

            <div className="field">
              <label htmlFor="email">Email</label>
              {/* Read-only: email is IDENTITY data owned by Keycloak, not profile
                  data owned by this service. Changing it means changing your
                  login, which belongs in account security, not here.
                  See docs/adr/0004. */}
              <input id="email" className="input" value={profile.email ?? ''} readOnly disabled />
            </div>
          </div>

          <div>
            <button type="submit" className="btn btn--primary" disabled={contactMutation.isPending}>
              {contactMutation.isPending ? 'Saving…' : 'Save contact details'}
            </button>
          </div>
        </form>
      </section>

      {/* --- Addresses --- */}
      <section className="card stack" aria-labelledby="addresses-heading">
        <h2 id="addresses-heading" style={{ marginTop: 0 }}>
          Addresses
        </h2>

        {profile.addresses.length === 0 && !showAddressForm && (
          <p className="muted">You have no saved addresses yet.</p>
        )}

        {profile.addresses.length > 0 && (
          <ul className="grid grid--2 product-grid">
            {profile.addresses.map((address) => (
              <li key={address.id} className="card stack">
                <div className="row">
                  <strong>{address.label}</strong>
                  {address.isDefaultShipping && <span className="badge badge--info">Default shipping</span>}
                  {address.isDefaultBilling && <span className="badge badge--info">Default billing</span>}
                </div>

                <p className="muted" style={{ margin: 0 }}>
                  {[address.line1, address.line2, address.city, address.postcode, address.country]
                    .filter(Boolean)
                    .join(', ')}
                </p>

                <div className="row">
                  <button type="button" className="btn btn--secondary" onClick={() => startEdit(address)}>
                    Edit
                  </button>

                  {!address.isDefaultShipping && (
                    <button
                      type="button"
                      className="btn btn--secondary"
                      onClick={() => defaultMutation.mutate({ id: address.id, kind: 'shipping' })}
                    >
                      Use for shipping
                    </button>
                  )}

                  {!address.isDefaultBilling && (
                    <button
                      type="button"
                      className="btn btn--secondary"
                      onClick={() => defaultMutation.mutate({ id: address.id, kind: 'billing' })}
                    >
                      Use for billing
                    </button>
                  )}

                  <button
                    type="button"
                    className="btn btn--secondary"
                    onClick={() => removeMutation.mutate(address.id)}
                  >
                    Remove
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}

        {!showAddressForm ? (
          <div>
            <button
              type="button"
              className="btn btn--primary"
              onClick={() => {
                setAddressDraft(EMPTY_ADDRESS);
                setEditingAddress(null);
                setShowAddressForm(true);
              }}
            >
              Add address
            </button>
          </div>
        ) : (
          <form
            className="stack"
            aria-label={editingAddress ? 'Edit address' : 'Add address'}
            onSubmit={(event) => {
              event.preventDefault();
              addressMutation.mutate({ id: editingAddress, body: addressDraft });
            }}
          >
            <div className="filters">
              <div className="field">
                <label htmlFor="label">Label</label>
                <input
                  id="label"
                  className="input"
                  required
                  maxLength={50}
                  placeholder="Home, Work…"
                  value={addressDraft.label}
                  onChange={(e) => setAddressDraft({ ...addressDraft, label: e.target.value })}
                />
              </div>

              <div className="field">
                <label htmlFor="line1">Address line 1</label>
                <input
                  id="line1"
                  className="input"
                  required
                  maxLength={200}
                  value={addressDraft.line1}
                  onChange={(e) => setAddressDraft({ ...addressDraft, line1: e.target.value })}
                />
              </div>

              <div className="field">
                <label htmlFor="line2">Address line 2</label>
                <input
                  id="line2"
                  className="input"
                  maxLength={200}
                  value={addressDraft.line2 ?? ''}
                  onChange={(e) => setAddressDraft({ ...addressDraft, line2: e.target.value })}
                />
              </div>

              <div className="field">
                <label htmlFor="city">City</label>
                <input
                  id="city"
                  className="input"
                  required
                  maxLength={100}
                  value={addressDraft.city}
                  onChange={(e) => setAddressDraft({ ...addressDraft, city: e.target.value })}
                />
              </div>

              <div className="field">
                <label htmlFor="postcode">Postcode</label>
                <input
                  id="postcode"
                  className="input"
                  required
                  maxLength={20}
                  value={addressDraft.postcode}
                  onChange={(e) => setAddressDraft({ ...addressDraft, postcode: e.target.value })}
                />
              </div>

              <div className="field">
                <label htmlFor="country">Country</label>
                <select
                  id="country"
                  className="input"
                  value={addressDraft.country}
                  onChange={(e) => setAddressDraft({ ...addressDraft, country: e.target.value })}
                >
                  {COUNTRIES.map((country) => (
                    <option key={country.code} value={country.code}>
                      {country.name}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            {addressMutation.isError && (
              <p className="badge badge--out-of-stock" role="alert">
                {(addressMutation.error as Error).message}
              </p>
            )}

            <div className="row">
              <button type="submit" className="btn btn--primary" disabled={addressMutation.isPending}>
                {addressMutation.isPending ? 'Saving…' : 'Save address'}
              </button>
              <button
                type="button"
                className="btn btn--secondary"
                onClick={() => {
                  setShowAddressForm(false);
                  setEditingAddress(null);
                }}
              >
                Cancel
              </button>
            </div>
          </form>
        )}
      </section>

      {/* --- Preferences --- */}
      <section className="card stack" aria-labelledby="preferences-heading">
        <h2 id="preferences-heading" style={{ marginTop: 0 }}>
          Preferences
        </h2>

        <form
          className="stack"
          onSubmit={(event) => {
            event.preventDefault();
            const form = new FormData(event.currentTarget);
            preferencesMutation.mutate({
              locale: form.get('locale') as string,
              currency: form.get('currency') as string,
              theme: form.get('theme') as string,
              marketingEmail: form.get('marketingEmail') === 'on',
              marketingSms: form.get('marketingSms') === 'on',
              orderUpdatesEmail: form.get('orderUpdatesEmail') === 'on',
              orderUpdatesSms: form.get('orderUpdatesSms') === 'on',
            });
          }}
        >
          <div className="filters">
            <div className="field">
              <label htmlFor="locale">Language</label>
              <select id="locale" name="locale" className="input" defaultValue={profile.preferences.locale}>
                {LOCALES.map((locale) => (
                  <option key={locale.code} value={locale.code}>
                    {locale.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="field">
              <label htmlFor="currency">Currency</label>
              <select
                id="currency"
                name="currency"
                className="input"
                defaultValue={profile.preferences.currency}
              >
                {CURRENCIES.map((currency) => (
                  <option key={currency.code} value={currency.code}>
                    {currency.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="field">
              <label htmlFor="theme">Theme</label>
              <select id="theme" name="theme" className="input" defaultValue={profile.preferences.theme}>
                <option value="system">Follow system</option>
                <option value="light">Light</option>
                <option value="dark">Dark</option>
              </select>
            </div>
          </div>

          <fieldset className="stack" style={{ border: 0, padding: 0, margin: 0 }}>
            {/* A real fieldset/legend, not a styled div: it groups the checkboxes
                for a screen reader so each one is announced with its group. */}
            <legend className="muted">Order updates</legend>
            <div className="field field--checkbox">
              <input
                id="orderUpdatesEmail"
                name="orderUpdatesEmail"
                type="checkbox"
                defaultChecked={profile.preferences.orderUpdatesEmail}
              />
              <label htmlFor="orderUpdatesEmail">Email me about my orders</label>
            </div>
            <div className="field field--checkbox">
              <input
                id="orderUpdatesSms"
                name="orderUpdatesSms"
                type="checkbox"
                defaultChecked={profile.preferences.orderUpdatesSms}
              />
              <label htmlFor="orderUpdatesSms">Text me about my orders</label>
            </div>
          </fieldset>

          <fieldset className="stack" style={{ border: 0, padding: 0, margin: 0 }}>
            <legend className="muted">Marketing</legend>
            {/* Separate from order updates on purpose. Marketing needs opt-in and
                can be withdrawn; an order confirmation is part of the contract
                and is sent regardless. Changing either records a consent entry
                server-side. */}
            <div className="field field--checkbox">
              <input
                id="marketingEmail"
                name="marketingEmail"
                type="checkbox"
                defaultChecked={profile.preferences.marketingEmail}
              />
              <label htmlFor="marketingEmail">Send me offers by email</label>
            </div>
            <div className="field field--checkbox">
              <input
                id="marketingSms"
                name="marketingSms"
                type="checkbox"
                defaultChecked={profile.preferences.marketingSms}
              />
              <label htmlFor="marketingSms">Send me offers by text</label>
            </div>
          </fieldset>

          <div>
            <button type="submit" className="btn btn--primary" disabled={preferencesMutation.isPending}>
              {preferencesMutation.isPending ? 'Saving…' : 'Save preferences'}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}
