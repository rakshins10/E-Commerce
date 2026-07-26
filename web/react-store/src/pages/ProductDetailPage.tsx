import { Link, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';

import { getProduct, stockLevel } from '../lib/catalog';
import { formatMoney } from '../lib/formatting';
import { ApiError } from '../lib/api-client';

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

  const query = useQuery({
    queryKey: ['product', id],
    queryFn: ({ signal }) => getProduct(id!, signal),
    enabled: Boolean(id),
    // A missing product will not start existing on retry, so do not waste the
    // user's time. Anything else is worth one retry.
    retry: (failureCount, error) =>
      error instanceof ApiError && error.isNotFound ? false : failureCount < 1,
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

          {/* Disabled rather than hidden when out of stock: a missing button
              looks like a broken page, whereas a disabled one with a reason
              explains itself. Basket arrives in Phase 6. */}
          <div>
            <button
              type="button"
              className="btn btn--primary"
              disabled
              title="Basket arrives in Phase 6"
            >
              Add to basket
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
