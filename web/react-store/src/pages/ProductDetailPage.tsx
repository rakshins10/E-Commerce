import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { getProduct, stockLevel } from '../lib/catalog';
import { createShopApi } from '../lib/basket';
import { formatMoney } from '../lib/formatting';
import { ApiError } from '../lib/api-client';
import { useCurrentUser } from '../auth/useCurrentUser';

/**
 * A single product.
 *
 * Note the distinction between a 404 and a genuine failure. "This product does
 * not exist" is a normal outcome deserving a helpful page; "the network is
 * down" is an error the user can retry. Collapsing both into one "something
 * went wrong" screen is the lazy option and gives the user nothing to act on.
 */
export function ProductDetailPage() {
  const { id } = useParams<{ id: string }>();
  const auth = useAuth();
  const queryClient = useQueryClient();
  const { isAuthenticated } = useCurrentUser();
  const [added, setAdded] = useState(false);

  const shop = useMemo(
    () => createShopApi(() => auth.user?.access_token ?? null),
    [auth.user?.access_token],
  );

  const query = useQuery({
    queryKey: ['product', id],
    queryFn: ({ signal }) => getProduct(id!, signal),
    enabled: Boolean(id),
    // A missing product will not start existing on retry, so do not waste the
    // user's time. Anything else is worth one retry.
    retry: (failureCount, error) =>
      error instanceof ApiError && error.isNotFound ? false : failureCount < 1,
  });

  // Declared BEFORE the early returns below. React requires every hook to run in the same order on
  // every render, so a hook placed after a conditional return would break the moment that branch is
  // taken - which is why this reads from query.data rather than from a narrowed `product`.
  const addToBasket = useMutation({
    mutationFn: () => {
      const product = query.data!;

      // The price sent here is what the customer is looking at, and the server treats it as display
      // information only - every line is re-priced from the catalogue at checkout. See the basket
      // service docs.
      return shop.addToBasket({
        productId: product.id,
        sku: product.sku,
        productName: product.name,
        imageUrl: product.imageUrl,
        unitPrice: product.price,
        currency: product.currency,
        quantity: 1,
      });
    },
    onSuccess: (basket) => {
      // Written straight into the cache, so the header count and the basket page are correct without
      // a refetch.
      queryClient.setQueryData(['basket'], basket);
      setAdded(true);
    },
  });

  if (query.isPending) {
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Loading product…</p>
      </div>
    );
  }

  if (query.isError) {
    const notFound = query.error instanceof ApiError && query.error.isNotFound;

    return (
      <div className="centred">
        <div className="card stack" role="alert">
          <h1 className="page-title">{notFound ? 'Product not found' : 'Could not load product'}</h1>
          <p className="muted">
            {notFound
              ? 'That product does not exist, or is no longer available.'
              : (query.error as Error).message}
          </p>
          <div className="row">
            <Link className="btn btn--primary" to="/products">
              Back to products
            </Link>
            {!notFound && (
              <button type="button" className="btn btn--secondary" onClick={() => query.refetch()}>
                Try again
              </button>
            )}
          </div>
        </div>
      </div>
    );
  }

  const product = query.data;
  const stock = stockLevel(product.stockOnHand);

  return (
    <div className="stack">
      {/* A breadcrumb rather than only browser Back: a user arriving from a
          shared link has no history to go back to. */}
      <nav aria-label="Breadcrumb" className="muted">
        <Link to="/products">Products</Link> <span aria-hidden="true">/</span>{' '}
        <Link to={`/products?category=${product.categorySlug}`}>{product.categoryName}</Link>
      </nav>

      <div className="product-detail">
        <div className="product-detail__image" aria-hidden="true">
          {product.name.charAt(0)}
        </div>

        <div className="stack">
          <h1 className="page-title">{product.name}</h1>

          <p className="muted">
            <Link to={`/products?brand=${product.brandSlug}`}>{product.brandName}</Link>
            {' · '}
            <span>SKU {product.sku}</span>
          </p>

          <p className="product-detail__price">
            {formatMoney({ amount: product.price, currency: product.currency })}
          </p>

          <span className={`badge badge--${stock.level}`}>{stock.label}</span>

          <p>{product.description}</p>

          {/* Disabled rather than hidden when out of stock: a missing button looks like a broken
              page, whereas a disabled one with a reason explains itself. */}
          <div className="row">
            <button
              type="button"
              className="btn btn--primary"
              disabled={product.stockOnHand === 0 || addToBasket.isPending}
              title={product.stockOnHand === 0 ? 'Out of stock' : undefined}
              onClick={() => {
                if (!isAuthenticated) {
                  void auth.signinRedirect();
                  return;
                }

                addToBasket.mutate();
              }}
            >
              {addToBasket.isPending ? 'Adding…' : 'Add to basket'}
            </button>

            {added && (
              // role="status" so a screen reader announces it. A visual-only confirmation leaves a
              // non-sighted customer with no evidence the click did anything.
              <p className="muted" role="status">
                Added to your basket. <Link to="/basket">View basket</Link>
              </p>
            )}
          </div>

          {addToBasket.isError && (
            <p className="muted" role="alert">
              {(addToBasket.error as Error).message}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
