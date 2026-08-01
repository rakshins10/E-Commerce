import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { createShopApi, type Basket } from '../lib/basket';
import { formatMoney } from '../lib/formatting';
import { useCurrentUser } from '../auth/useCurrentUser';
import { Icon } from '../components/Icon';

/**
 * The basket.
 *
 * ---
 * **Optimistic updates, and why they are used HERE but not on My Account.**
 *
 * Changing a quantity is a click a customer repeats several times in a row, and waiting for a round trip
 * after each one feels broken. The outcome is also completely predictable: setting the quantity to 3
 * results in a quantity of 3. So the cache is updated immediately and reconciled with the server's
 * answer afterwards.
 *
 * My Account deliberately does *not* do this, because there the server's answer legitimately differs
 * from the request — adding your first address silently makes it the default for both shipping and
 * billing. Guessing that outcome would mean reimplementing the aggregate's rules in TypeScript.
 *
 * The distinction is the point: optimism is right when you can predict the result, and wrong when you
 * cannot. See docs/react-vs-angular.md.
 */
export function BasketPage() {
  const auth = useAuth();
  const { isAuthenticated, isLoading: authLoading } = useCurrentUser();
  const queryClient = useQueryClient();

  const api = useMemo(
    () => createShopApi(() => auth.user?.access_token ?? null),
    [auth.user?.access_token],
  );

  const basketQuery = useQuery({
    queryKey: ['basket'],
    queryFn: () => api.getBasket(),
    enabled: isAuthenticated,
  });

  const setQuantity = useMutation({
    mutationFn: ({ sku, quantity }: { sku: string; quantity: number }) => api.setQuantity(sku, quantity),

    // --- The optimistic part -------------------------------------------------------------------
    onMutate: async ({ sku, quantity }) => {
      // Cancel any in-flight refetch first. Without this, a response that started before this change
      // can land after it and overwrite the optimistic value with the old one.
      await queryClient.cancelQueries({ queryKey: ['basket'] });

      const previous = queryClient.getQueryData<Basket>(['basket']);

      queryClient.setQueryData<Basket>(['basket'], (current) => {
        if (!current) return current;

        const items = current.items
          .map((item) =>
            item.sku === sku ? { ...item, quantity, lineTotal: item.unitPrice * quantity } : item,
          )
          .filter((item) => item.quantity > 0);

        return {
          ...current,
          items,
          estimatedTotal: items.reduce((sum, item) => sum + item.lineTotal, 0),
          totalUnits: items.reduce((sum, item) => sum + item.quantity, 0),
        };
      });

      // Returned so onError can put it back. Optimism without a rollback is just being wrong quickly.
      return { previous };
    },

    onError: (_error, _variables, context) => {
      if (context?.previous) {
        queryClient.setQueryData(['basket'], context.previous);
      }
    },

    // The server's answer is authoritative, always. It applies the quantity limits and the zero-removes
    // rule, so the guess above is replaced by the truth even when the two agree.
    onSuccess: (updated) => queryClient.setQueryData(['basket'], updated),
  });

  const remove = useMutation({
    mutationFn: (sku: string) => api.removeFromBasket(sku),
    onSuccess: (updated) => queryClient.setQueryData(['basket'], updated),
  });

  const clear = useMutation({
    mutationFn: () => api.clearBasket(),
    onSuccess: (updated) => queryClient.setQueryData(['basket'], updated),
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
          <h1 className="page-title">Your basket</h1>
          <p className="muted">Sign in to see the items in your basket.</p>
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

  if (basketQuery.isError) {
    return (
      <div className="centred">
        <div className="card stack" role="alert">
          <h1 className="page-title">Could not load your basket</h1>
          <p className="muted">{(basketQuery.error as Error).message}</p>
          <button type="button" className="btn btn--primary" onClick={() => basketQuery.refetch()}>
            Try again
          </button>
        </div>
      </div>
    );
  }

  const basket = basketQuery.data;
  const isEmpty = basket.items.length === 0;

  return (
    <div className="stack">
      <h1 className="page-title">Your basket</h1>

      {isEmpty ? (
        <div className="card stack empty-state">
          <Icon name="cart" className="empty-state__icon" />
          <p className="lede">Your basket is empty.</p>
          <div>
            <Link className="btn btn--primary" to="/products">
              Browse products
            </Link>
          </div>
        </div>
      ) : (
        <div className="checkout-layout">
          {/* --- The lines ------------------------------------------------------------------
              A list, not a table.

              This WAS a table, on the reasoning that a basket is tabular data. It is not: each line
              is one product with a picture, a price and two controls, and the "Price/Quantity/Total"
              headers a screen reader repeated on every cell added nothing a shopper did not already
              read. What matters for accessibility is that every control still names its own product -
              which the labels below do - not that the layout is a grid. */}
          <div className="card">
            <ul className="plain-list" aria-label="Items in your basket">
              {basket.items.map((item) => {
                /**
                 * What every control on this line is called.
                 *
                 * "Quantity for Classic Cotton T-shirt" was unique until a basket could hold the same
                 * shirt twice. Two controls with one accessible name is not a cosmetic problem: it is
                 * how someone navigating by control ends up changing the wrong line, and Playwright's
                 * strict mode is the cheap version of the same complaint.
                 */
                const label = [item.productName, item.size, item.colourName]
                  .filter(Boolean)
                  .join(', ');

                return (
                <li key={item.sku} className="line-item">
                  <Link to={`/products/${item.productId}`} tabIndex={-1} aria-hidden="true">
                    <img
                      className="line-item__media"
                      src={item.imageUrl ?? '/img/placeholder.svg'}
                      alt=""
                      loading="lazy"
                      width={80}
                      height={80}
                    />
                  </Link>

                  <div className="line-item__body">
                    <div className="line-item__top">
                      <div className="stack--tight">
                        <Link to={`/products/${item.productId}`}>{item.productName}</Link>
                        {/* The option a customer chose, in the words they chose it in. The SKU is
                            underneath because it is what support and the warehouse quote. */}
                        {(item.size || item.colourName) && (
                          <span className="small">
                            {[item.size, item.colourName].filter(Boolean).join(' · ')}
                          </span>
                        )}
                        <span className="muted small">{item.sku}</span>
                      </div>
                      <span className="price">
                        {formatMoney({ amount: item.lineTotal, currency: item.currency })}
                      </span>
                    </div>

                    <div className="line-item__actions">
                      {/* Three real controls, not arrows drawn on a div. The buttons are the fast path
                          on a phone; the input is still there for someone typing 12. */}
                      <div className="stepper">
                        <button
                          type="button"
                          className="stepper__btn"
                          aria-label={`Decrease quantity for ${label}`}
                          disabled={setQuantity.isPending}
                          onClick={() =>
                            setQuantity.mutate({ sku: item.sku, quantity: item.quantity - 1 })
                          }
                        >
                          <span aria-hidden="true">−</span>
                        </button>

                        <label className="visually-hidden" htmlFor={`quantity-${item.sku}`}>
                          Quantity for {label}
                        </label>
                        <input
                          id={`quantity-${item.sku}`}
                          type="number"
                          className="stepper__input"
                          min={0}
                          max={100}
                          value={item.quantity}
                          onChange={(event) =>
                            setQuantity.mutate({
                              sku: item.sku,
                              quantity: Number(event.target.value),
                            })
                          }
                        />

                        <button
                          type="button"
                          className="stepper__btn"
                          aria-label={`Increase quantity for ${label}`}
                          disabled={setQuantity.isPending}
                          onClick={() =>
                            setQuantity.mutate({ sku: item.sku, quantity: item.quantity + 1 })
                          }
                        >
                          <span aria-hidden="true">+</span>
                        </button>
                      </div>

                      <span className="muted small">
                        {formatMoney({ amount: item.unitPrice, currency: item.currency })} each
                      </span>

                      <button
                        type="button"
                        className="btn btn--ghost btn--sm"
                        onClick={() => remove.mutate(item.sku)}
                        disabled={remove.isPending}
                      >
                        {/* The accessible name says WHAT is being removed. Twelve buttons all called
                            "Remove" are useless to anyone navigating by button. */}
                        <Icon name="trash" className="icon--sm" />
                        <span aria-hidden="true">Remove</span>
                        <span className="visually-hidden">Remove {label}</span>
                      </button>
                    </div>
                  </div>
                </li>
                );
              })}
            </ul>
          </div>

          {/* --- The summary ---------------------------------------------------------------- */}
          <aside className="checkout-layout__aside" aria-label="Order summary">
            <div className="card stack">
              <h2 style={{ marginTop: 0 }}>Summary</h2>

              <div className="summary">
                <p className="summary__row" role="status">
                  <span>
                    {basket.totalUnits} {basket.totalUnits === 1 ? 'item' : 'items'}
                  </span>
                  <span>
                    {formatMoney({ amount: basket.estimatedTotal, currency: basket.currency })}
                  </span>
                </p>
                <p className="summary__row">
                  <span>Delivery</span>
                  <span>Calculated at checkout</span>
                </p>
                <p className="summary__total">
                  <span>Estimated total</span>
                  <span>
                    {formatMoney({ amount: basket.estimatedTotal, currency: basket.currency })}
                  </span>
                </p>
              </div>

              {/* Said plainly rather than hidden in small print. Prices are re-checked at checkout, and
                  a customer who sees a different total on the confirmation deserves to have been
                  warned. */}
              <p className="muted small">
                Prices are confirmed when you place your order, so this total may change.
              </p>

              <Link className="btn btn--primary btn--block" to="/checkout">
                Checkout
              </Link>

              <button
                type="button"
                className="btn btn--ghost btn--block"
                onClick={() => clear.mutate()}
                disabled={clear.isPending}
              >
                Empty basket
              </button>
            </div>
          </aside>
        </div>
      )}
    </div>
  );
}
