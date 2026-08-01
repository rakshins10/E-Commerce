import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useMutation, useQuery } from '@tanstack/react-query';

import { createShopApi, type OrderAddress } from '../lib/basket';
import { createProfileApi, COUNTRIES } from '../lib/profile';
import { formatMoney } from '../lib/formatting';
import { useCurrentUser } from '../auth/useCurrentUser';

const EMPTY_ADDRESS: OrderAddress = {
  recipient: '',
  line1: '',
  line2: null,
  city: '',
  postcode: '',
  country: 'GB',
};

/**
 * Checkout: confirm where it goes, then place the order.
 *
 * ---
 * **The address is copied, not linked.** Picking a saved address fills the form; what is sent is the
 * text, and the order stores its own snapshot. A customer who moves house next year must not silently
 * rewrite where last year's parcel was sent. See the ShippingAddress value object.
 */
export function CheckoutPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const { isAuthenticated, isLoading: authLoading } = useCurrentUser();

  const shop = useMemo(
    () => createShopApi(() => auth.user?.access_token ?? null),
    [auth.user?.access_token],
  );

  const profiles = useMemo(
    () => createProfileApi(() => auth.user?.access_token ?? null),
    [auth.user?.access_token],
  );

  const [address, setAddress] = useState<OrderAddress>(EMPTY_ADDRESS);
  const [error, setError] = useState<string | null>(null);

  const basketQuery = useQuery({
    queryKey: ['basket'],
    queryFn: () => shop.getBasket(),
    enabled: isAuthenticated,
  });

  const profileQuery = useQuery({
    queryKey: ['profile'],
    queryFn: () => profiles.get(),
    enabled: isAuthenticated,
  });

  // Prefill from the saved default. A customer who has already told us where they live should not have
  // to type it again — and the address book exists precisely so they do not.
  useEffect(() => {
    const profile = profileQuery.data;
    if (!profile) return;

    const preferred =
      profile.addresses.find((candidate) => candidate.isDefaultShipping) ?? profile.addresses[0];

    if (!preferred) return;

    setAddress((current) =>
      // Only if the form is untouched, so a refetch cannot overwrite what the customer is typing.
      current.line1 === ''
        ? {
            recipient: profile.displayName ?? '',
            line1: preferred.line1,
            line2: preferred.line2,
            city: preferred.city,
            postcode: preferred.postcode,
            country: preferred.country,
          }
        : current,
    );
  }, [profileQuery.data]);

  const placeOrder = useMutation({
    mutationFn: () =>
      shop.placeOrder({ shippingAddress: address, currency: basketQuery.data?.currency ?? 'GBP' }),

    onSuccess: (order) => {
      // Replace, not push: the Back button must not return to a checkout page whose basket is now
      // empty, where pressing "Place order" again would fail confusingly.
      navigate(`/orders/${order.id}?placed=1`, { replace: true });
    },

    onError: (mutationError: Error) => setError(mutationError.message),
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
          <h1 className="page-title">Checkout</h1>
          <p className="muted">Sign in to place an order.</p>
          <div>
            <button
              type="button"
              className="btn btn--primary"
              onClick={() => void auth.signinRedirect()}
            >
              Sign in
            </button>
          </div>
        </div>
      </div>
    );
  }

  if (basketQuery.isPending) {
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Loading your basket…</p>
      </div>
    );
  }

  const basket = basketQuery.data;

  if (!basket || basket.items.length === 0) {
    return (
      <div className="centred">
        <div className="card stack">
          <h1 className="page-title">Checkout</h1>
          <p className="lede">Your basket is empty.</p>
          <div>
            <Link className="btn btn--primary" to="/products">
              Browse products
            </Link>
          </div>
        </div>
      </div>
    );
  }

  const canSubmit =
    address.recipient.trim() !== '' &&
    address.line1.trim() !== '' &&
    address.city.trim() !== '' &&
    address.postcode.trim() !== '';

  return (
    <div className="stack">
      <h1 className="page-title">Checkout</h1>

      {error && (
        <div className="card" role="alert">
          <p className="muted">{error}</p>
        </div>
      )}

      {/* Two columns on a wide screen, with the summary sticky beside the form: the total a customer
          is about to commit to should never be scrolled off the page while they fill in an address.
          One column below 64rem, where sticky would eat the viewport. */}
      <form
        className="checkout-layout"
        onSubmit={(event) => {
          event.preventDefault();
          setError(null);
          placeOrder.mutate();
        }}
      >
        <section className="card stack" aria-labelledby="delivery-heading">
          <h2 id="delivery-heading">Delivery address</h2>

          <div className="field">
            <label htmlFor="recipient">Recipient</label>
            <input
              id="recipient"
              className="input"
              required
              maxLength={200}
              value={address.recipient}
              onChange={(event) => setAddress({ ...address, recipient: event.target.value })}
            />
          </div>

          <div className="field">
            <label htmlFor="line1">Address line 1</label>
            <input
              id="line1"
              className="input"
              required
              maxLength={200}
              value={address.line1}
              onChange={(event) => setAddress({ ...address, line1: event.target.value })}
            />
          </div>

          <div className="field">
            <label htmlFor="line2">Address line 2</label>
            <input
              id="line2"
              className="input"
              maxLength={200}
              value={address.line2 ?? ''}
              onChange={(event) => setAddress({ ...address, line2: event.target.value })}
            />
          </div>

          <div className="field">
            <label htmlFor="city">City</label>
            <input
              id="city"
              className="input"
              required
              maxLength={100}
              value={address.city}
              onChange={(event) => setAddress({ ...address, city: event.target.value })}
            />
          </div>

          <div className="field">
            <label htmlFor="postcode">Postcode</label>
            <input
              id="postcode"
              className="input"
              required
              maxLength={20}
              value={address.postcode}
              onChange={(event) => setAddress({ ...address, postcode: event.target.value })}
            />
          </div>

          <div className="field">
            <label htmlFor="country">Country</label>
            <select
              id="country"
              className="input"
              value={address.country}
              onChange={(event) => setAddress({ ...address, country: event.target.value })}
            >
              {COUNTRIES.map((country) => (
                <option key={country.code} value={country.code}>
                  {country.name}
                </option>
              ))}
            </select>
          </div>
        </section>

        <aside className="checkout-layout__aside">
          <section className="card stack" aria-labelledby="summary-heading">
            <h2 id="summary-heading" style={{ marginTop: 0 }}>
              Order summary
            </h2>

            <ul className="plain-list stack--tight">
              {basket.items.map((item) => (
                <li key={item.productId} className="row">
                  <img
                    className="thumb"
                    src={item.imageUrl ?? '/img/placeholder.svg'}
                    alt=""
                    aria-hidden="true"
                    loading="lazy"
                    width={40}
                    height={40}
                  />
                  <span>
                    {item.productName}
                    {(item.size || item.colourName) &&
                      ` (${[item.size, item.colourName].filter(Boolean).join(' · ')})`}{' '}
                    × {item.quantity} —{' '}
                    {formatMoney({ amount: item.lineTotal, currency: item.currency })}
                  </span>
                </li>
              ))}
            </ul>

            <div className="summary">
              <p className="summary__row">
                <span>Delivery</span>
                <span>Free</span>
              </p>
              <p className="summary__total">
                <span>Estimated total</span>
                <span>
                  {formatMoney({ amount: basket.estimatedTotal, currency: basket.currency })}
                </span>
              </p>
            </div>

            <p className="muted small">
              We confirm every price against the catalogue when you place your order, so the amount
              charged may differ from the estimate above.
            </p>

            <button
              type="submit"
              className="btn btn--primary btn--block"
              disabled={!canSubmit || placeOrder.isPending}
            >
              {placeOrder.isPending ? 'Placing your order…' : 'Place order'}
            </button>
          </section>
        </aside>
      </form>
    </div>
  );
}
