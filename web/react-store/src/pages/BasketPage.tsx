import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { createShopApi, type Basket } from '../lib/basket';
import { formatMoney } from '../lib/formatting';
import { useCurrentUser } from '../auth/useCurrentUser';

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
    mutationFn: ({ productId, quantity }: { productId: string; quantity: number }) =>
      api.setQuantity(productId, quantity),

    // --- The optimistic part -------------------------------------------------------------------
    onMutate: async ({ productId, quantity }) => {
      // Cancel any in-flight refetch first. Without this, a response that started before this change
      // can land after it and overwrite the optimistic value with the old one.
      await queryClient.cancelQueries({ queryKey: ['basket'] });

      const previous = queryClient.getQueryData<Basket>(['basket']);

      queryClient.setQueryData<Basket>(['basket'], (current) => {
        if (!current) return current;

        const items = current.items
          .map((item) =>
            item.productId === productId
              ? { ...item, quantity, lineTotal: item.unitPrice * quantity }
              : item,
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
    mutationFn: (productId: string) => api.removeFromBasket(productId),
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
        <div className="card stack">
          <p className="lede">Your basket is empty.</p>
          <div>
            <Link className="btn btn--primary" to="/products">
              Browse products
            </Link>
          </div>
        </div>
      ) : (
        <>
          {/* A table, because this is tabular data. A list of divs would lose the column headers a
              screen reader announces with each cell. */}
          <div className="card">
            <table className="table">
              <caption className="visually-hidden">Items in your basket</caption>
              <thead>
                <tr>
                  <th scope="col">Product</th>
                  <th scope="col">Price</th>
                  <th scope="col">Quantity</th>
                  <th scope="col">Total</th>
                  <th scope="col">
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {basket.items.map((item) => (
                  <tr key={item.productId}>
                    <th scope="row">
                      <Link to={`/products/${item.productId}`}>{item.productName}</Link>
                      <div className="muted small">{item.sku}</div>
                    </th>
                    <td>{formatMoney({ amount: item.unitPrice, currency: item.currency })}</td>
                    <td>
                      <label
                        className="visually-hidden"
                        htmlFor={`quantity-${item.productId}`}
                      >
                        Quantity for {item.productName}
                      </label>
                      <input
                        id={`quantity-${item.productId}`}
                        type="number"
                        className="input input--narrow"
                        min={0}
                        max={100}
                        value={item.quantity}
                        onChange={(event) =>
                          setQuantity.mutate({
                            productId: item.productId,
                            quantity: Number(event.target.value),
                          })
                        }
                      />
                    </td>
                    <td>{formatMoney({ amount: item.lineTotal, currency: item.currency })}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn--secondary"
                        onClick={() => remove.mutate(item.productId)}
                        disabled={remove.isPending}
                      >
                        {/* The accessible name says WHAT is being removed. Twelve buttons all called
                            "Remove" are useless to anyone navigating by button. */}
                        <span aria-hidden="true">Remove</span>
                        <span className="visually-hidden">Remove {item.productName}</span>
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="card stack">
            <p className="lede" role="status">
              {basket.totalUnits} {basket.totalUnits === 1 ? 'item' : 'items'} ·{' '}
              {formatMoney({ amount: basket.estimatedTotal, currency: basket.currency })}
            </p>

            {/* Said plainly rather than hidden in small print. Prices are re-checked at checkout, and a
                customer who sees a different total on the confirmation deserves to have been warned. */}
            <p className="muted small">
              Prices are confirmed when you place your order, so this total may change.
            </p>

            <div className="row">
              <Link className="btn btn--primary" to="/checkout">
                Checkout
              </Link>
              <button
                type="button"
                className="btn btn--secondary"
                onClick={() => clear.mutate()}
                disabled={clear.isPending}
              >
                Empty basket
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
