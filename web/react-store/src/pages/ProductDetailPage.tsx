import { useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  colourHasStock,
  coloursOf,
  findVariant,
  getProduct,
  sizeHasStock,
  sizesOf,
  stockLevel,
} from '../lib/catalog';
import { createShopApi } from '../lib/basket';
import { formatMoney } from '../lib/formatting';
import { ApiError } from '../lib/api-client';
import { useCurrentUser } from '../auth/useCurrentUser';
import { Icon } from '../components/Icon';

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

  /**
   * Which size and colour is chosen.
   *
   * `null` means "not chosen yet" for a product that HAS that axis, and also "this product has no such
   * axis" — the two collapse deliberately, because a mug's variants genuinely have `size: null` and
   * matching on null is then correct rather than a special case.
   */
  const [size, setSize] = useState<string | null>(null);
  const [colour, setColour] = useState<string | null>(null);

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

  const variants = query.data?.variants ?? [];
  const sizes = sizesOf(variants);
  const colours = coloursOf(variants);

  /**
   * Pre-selects a colour, never a size.
   *
   * This is what clothing retailers do, and the asymmetry is deliberate. The photograph already shows a
   * colour, so defaulting to one is honest; a size is a decision only the customer can make, and
   * defaulting it means someone buys a Small because it happened to be first. So Add to basket stays
   * disabled until a size is picked, and says why.
   *
   * The first colour WITH STOCK, so the default is something you can actually buy.
   */
  useEffect(() => {
    if (colours.length === 0 || colour !== null) return;

    const available = colours.find((option) => colourHasStock(variants, option.name));
    setColour((available ?? colours[0])!.name);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query.data]);

  const selected = findVariant(variants, size, colour);

  // A size axis that has not been chosen yet. Not the same as "no variant matches", which is a real
  // combination the customer picked that happens not to exist.
  const needsSize = sizes.length > 0 && size === null;

  // Declared BEFORE the early returns below. React requires every hook to run in the same order on
  // every render, so a hook placed after a conditional return would break the moment that branch is
  // taken - which is why this reads from query.data rather than from a narrowed `product`.
  const addToBasket = useMutation({
    mutationFn: () => {
      const product = query.data!;
      const variant = findVariant(product.variants, size, colour);

      if (!variant) {
        // Should be unreachable - the button is disabled without a variant - but throwing beats sending
        // the style code, which Inventory has no stock row for and which would fail two services later.
        throw new Error('Choose an option before adding to your basket.');
      }

      // The VARIANT SKU, not the product's style code. This is the string the warehouse picks by.
      //
      // The price sent here is what the customer is looking at, and the server treats it as display
      // information only - every line is re-priced from the catalogue at checkout. See the basket
      // service docs.
      return shop.addToBasket({
        productId: product.id,
        sku: variant.sku,
        productName: product.name,
        size: variant.size,
        colourName: variant.colourName,
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
        {/* alt="" because the h1 immediately beside it already names the product. A duplicate
            announcement is noise, not information. */}
        <div className="product-media">
          <img
            className="product-media__img"
            src={product.imageUrl ?? '/img/placeholder.svg'}
            alt=""
            aria-hidden="true"
            width={800}
            height={600}
          />
        </div>

        <div className="stack">
          <h1 className="page-title">{product.name}</h1>

          <p className="muted">
            <Link to={`/products?brand=${product.brandSlug}`}>{product.brandName}</Link>
            {' · '}
            <span>SKU {product.sku}</span>
          </p>

          <div className="row">
            <p className="product-detail__price">
              {formatMoney({ amount: product.price, currency: product.currency })}
            </p>
            <span className={`badge badge--${stock.level}`}>{stock.label}</span>
          </div>

          <p>{product.description}</p>

          {/* --- Size ------------------------------------------------------------------------
              A real radio group. Arrow-key navigation, one tab stop, and "Size, Medium, 2 of 4"
              announced by a screen reader — all of which a div with a click handler would have to
              reimplement, and usually does not.

              A sold-out size is disabled AND struck through AND named in the stock line below.
              Never colour alone (WCAG 1.4.1). */}
          {sizes.length > 0 && (
            <fieldset className="option-group">
              <legend className="option-group__legend">
                Size {needsSize && <span className="option-group__hint">— choose one</span>}
              </legend>

              <div className="option-list">
                {sizes.map((option) => {
                  const available = sizeHasStock(variants, option);

                  return (
                    <span key={option} className="option">
                      <input
                        className="option__input"
                        type="radio"
                        name="size"
                        id={`size-${option}`}
                        value={option}
                        checked={size === option}
                        disabled={!available}
                        onChange={() => setSize(option)}
                      />
                      <label className="option__label" htmlFor={`size-${option}`}>
                        {option}
                        {!available && <span className="visually-hidden"> — sold out</span>}
                      </label>
                    </span>
                  );
                })}
              </div>
            </fieldset>
          )}

          {/* --- Colour ---------------------------------------------------------------------- */}
          {colours.length > 0 && (
            <fieldset className="option-group">
              <legend className="option-group__legend">Colour</legend>

              <div className="option-list">
                {colours.map((option) => {
                  const available = colourHasStock(variants, option.name);

                  return (
                    <span key={option.name} className="option">
                      <input
                        className="option__input"
                        type="radio"
                        name="colour"
                        id={`colour-${option.name}`}
                        value={option.name}
                        checked={colour === option.name}
                        disabled={!available}
                        onChange={() => setColour(option.name)}
                      />
                      <label className="option__label" htmlFor={`colour-${option.name}`}>
                        {/* The swatch is decoration on top of the name, never instead of it. A colour
                            conveyed only as a colour is unreadable to plenty of people. */}
                        <span
                          className="swatch"
                          style={{ background: option.hex ?? 'transparent' }}
                          aria-hidden="true"
                        />
                        {option.name}
                        {!available && <span className="visually-hidden"> — sold out</span>}
                      </label>
                    </span>
                  );
                })}
              </div>
            </fieldset>
          )}

          {/* --- What the chosen combination actually has -------------------------------------
              role="status", so choosing a size ANNOUNCES how many are left rather than silently
              changing a number a sighted user happens to be looking at. Fixed height, so the button
              below does not jump out from under the pointer when this appears. */}
          <p className="variant-stock" role="status">
            {needsSize ? (
              <span className="muted">Choose a size to see availability.</span>
            ) : selected ? (
              selected.stockOnHand === 0 ? (
                <span className="badge badge--out-of-stock">Sold out in this option</span>
              ) : selected.stockOnHand <= 5 ? (
                <span className="badge badge--low-stock">Only {selected.stockOnHand} left</span>
              ) : (
                <span className="badge badge--in-stock">In stock</span>
              )
            ) : (
              <span className="badge badge--out-of-stock">That combination is not available</span>
            )}
          </p>

          {/* Disabled rather than hidden when out of stock: a missing button looks like a broken
              page, whereas a disabled one with a reason explains itself. */}
          <div className="row">
            <button
              type="button"
              className="btn btn--primary"
              disabled={
                needsSize || !selected || selected.stockOnHand === 0 || addToBasket.isPending
              }
              title={
                needsSize
                  ? 'Choose a size'
                  : !selected || selected.stockOnHand === 0
                    ? 'Out of stock'
                    : undefined
              }
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

          {/* The three things a shopper checks before committing. Repeated from the home page on
              purpose - this is the page where the question is actually asked. */}
          <ul className="stack--tight plain-list muted small">
            <li className="row">
              <Icon name="truck" className="icon--sm" /> Free delivery on orders over £50
            </li>
            <li className="row">
              <Icon name="shield" className="icon--sm" /> 30-day returns
            </li>
            <li className="row">
              <Icon name="boxOpen" className="icon--sm" /> Stock is reserved when you check out, not
              when you add to the basket
            </li>
          </ul>
        </div>
      </div>
    </div>
  );
}
