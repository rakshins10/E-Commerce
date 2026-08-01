import { Link } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useQuery } from '@tanstack/react-query';

import { getCategories, groupIntoDepartments, searchProducts, stockLevel } from '../lib/catalog';
import { formatMoney } from '../lib/formatting';
import { useCurrentUser } from '../auth/useCurrentUser';
import { Icon } from '../components/Icon';
import type { IconName } from '../components/Icon';

/**
 * The shopfront.
 *
 * ---
 * **Everything here is a real query.** The hero images, the category counts and the featured row all
 * come from the Catalog service — nothing is hard-coded to make the page look full. A landing page
 * built from fixtures is the one page that never catches a broken API.
 *
 * ---
 * **The permissions panel stays, below the shopping.** It is not filler: signing in as `customer` and
 * as `administrator` produces visibly different permission lists, which is the fastest way to see they
 * came from composite roles in Keycloak rather than from anything this application decided. It belongs
 * *under* the shop rather than instead of it.
 */
const REASSURANCE: readonly { icon: IconName; title: string; detail: string }[] = [
  { icon: 'truck', title: 'Free delivery over £50', detail: 'Dispatched within one working day.' },
  { icon: 'shield', title: '30-day returns', detail: 'Unused and in its original packaging.' },
  {
    icon: 'tag',
    title: 'Prices confirmed at checkout',
    detail: 'Always the current price, never a stale one.',
  },
];

export function HomePage() {
  const auth = useAuth();
  const { user, isAuthenticated, isLoading } = useCurrentUser();

  const featuredQuery = useQuery({
    queryKey: ['products', { featured: true }],
    // In stock only, because a shopfront leading with sold-out items wastes a click. Sorted by name
    // so the row is stable between visits rather than reshuffling.
    queryFn: ({ signal }) =>
      searchProducts({ inStockOnly: true, pageSize: 4, sortBy: 'name' }, signal),
  });

  const categoriesQuery = useQuery({
    queryKey: ['categories'],
    queryFn: ({ signal }) => getCategories(signal),
    staleTime: 5 * 60 * 1000,
  });

  if (isLoading) {
    // aria-busy + a live region, so a screen reader announces the wait rather than reading an empty
    // page.
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Signing you in…</p>
      </div>
    );
  }

  const featured = featuredQuery.data?.items ?? [];

  /**
   * The shop, by department.
   *
   * Departments only, each showing what is inside it — the way a shop is actually organised, and the
   * thing a flat list of six tiles could not express. A customer thinking "I want a hoodie" and one
   * thinking "show me the clothing" both find their way in one look.
   *
   * Empty departments are dropped rather than advertised. The counts include children now
   * (`GetCategoriesAsync` rolls them up), so a department reading zero genuinely has nothing in it.
   */
  const departments = groupIntoDepartments(categoriesQuery.data ?? []).filter(
    (department) => department.productCount > 0,
  );

  return (
    <div className="stack">
      {auth.error && (
        // role="alert" is announced immediately, which is what an authentication failure warrants.
        <div className="card" role="alert">
          <h2 style={{ marginTop: 0 }}>Sign-in failed</h2>
          <p className="muted">{auth.error.message}</p>
        </div>
      )}

      <section className="hero">
        <div>
          <h1 className="hero__title">Everyday things, properly made</h1>
          <p className="hero__lede">
            Clothing, drinkware and stationery from three independent brands.
          </p>

          <div className="row">
            <Link className="btn btn--primary" to="/products">
              Shop all products
            </Link>

            {!isAuthenticated && (
              <button
                type="button"
                className="btn btn--secondary"
                onClick={() => void auth.signinRedirect()}
              >
                Sign in
              </button>
            )}
          </div>
        </div>

        {/* Four real products, so the hero is never a picture of a shop that does not exist.
            aria-hidden because the featured section below says the same thing in text. */}
        <div className="hero__art" aria-hidden="true">
          {featured.slice(0, 4).map((product) => (
            <img key={product.id} src={product.imageUrl ?? '/img/placeholder.svg'} alt="" />
          ))}
        </div>
      </section>

      {/* --- Reassurance ------------------------------------------------------------------
          Three things a shopper looks for before browsing. The icons are decorative; the text
          carries the message. */}
      <ul className="grid grid--3 plain-list">
        {REASSURANCE.map((item) => (
          <li key={item.title} className="card row">
            <Icon name={item.icon} />
            <span className="stack--tight">
              <strong>{item.title}</strong>
              <span className="muted small">{item.detail}</span>
            </span>
          </li>
        ))}
      </ul>

      {/* --- Departments ------------------------------------------------------------------- */}
      {departments.length > 0 && (
        <section aria-labelledby="categories-heading">
          <div className="section-head">
            <h2 id="categories-heading">Shop by department</h2>
            <Link className="nav-link" to="/products">
              All products <Icon name="chevronRight" className="icon--sm" />
            </Link>
          </div>

          <ul className="grid grid--2 plain-list">
            {departments.map((department) => (
              <li key={department.id} className="card department">
                <h3 className="department__head">
                  <Link to={`/products?category=${department.slug}`}>{department.name}</Link>
                  <span className="muted small">{department.productCount} products</span>
                </h3>

                {/* The children, so the way in is one click rather than a category page and then a
                    filter. This is the whole reason a shop shows you its departments at all. */}
                <div className="department__links">
                  {department.children.map((child) => (
                    <Link
                      key={child.id}
                      className="department__chip"
                      to={`/products?category=${child.slug}`}
                    >
                      {child.name}
                      <span className="category-rail__count">{child.productCount}</span>
                    </Link>
                  ))}
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {/* --- Featured ---------------------------------------------------------------------- */}
      {featured.length > 0 && (
        <section aria-labelledby="featured-heading">
          <div className="section-head">
            <h2 id="featured-heading">In stock now</h2>
          </div>

          <ul className="grid grid--4 plain-list">
            {featured.map((product) => {
              const stock = stockLevel(product.stockOnHand);

              return (
                <li key={product.id} className="card product-card">
                  <div className="product-media">
                    <img
                      className="product-media__img"
                      src={product.imageUrl ?? '/img/placeholder.svg'}
                      alt=""
                      aria-hidden="true"
                      loading="lazy"
                      width={400}
                      height={300}
                    />
                  </div>

                  <div className="product-card__body">
                    <p className="product-card__brand">{product.brandName}</p>
                    <h3 className="product-card__name">
                      <Link to={`/products/${product.id}`} className="product-card__link">
                        {product.name}
                      </Link>
                    </h3>
                  </div>

                  <div className="product-card__footer">
                    <span className="price">
                      {formatMoney({ amount: product.price, currency: product.currency })}
                    </span>
                    <span className={`badge badge--${stock.level}`}>{stock.label}</span>
                  </div>
                </li>
              );
            })}
          </ul>
        </section>
      )}

      {/* --- What the token actually carries -----------------------------------------------
          Kept because it is the point of this repository; moved below the shopping because a
          customer did not come here for it. */}
      {isAuthenticated && (
        <section className="card stack" aria-labelledby="permissions-heading">
          <h2 id="permissions-heading" style={{ marginTop: 0 }}>
            Signed in as {user?.displayName ?? user?.username}{' '}
            <span className="badge badge--info">{user?.permissions.length ?? 0} permissions</span>
          </h2>

          <p className="muted">
            Granted by composite roles in Keycloak, not assigned to this account directly. They decide
            what this page shows — the server enforces the same rules independently.
          </p>

          <div className="chips">
            {[...(user?.permissions ?? [])].sort().map((permission) => (
              <span key={permission} className="chip">
                {permission}
              </span>
            ))}
          </div>
        </section>
      )}

      {!isAuthenticated && (
        <section className="card stack" aria-labelledby="try-heading">
          <h2 id="try-heading" style={{ marginTop: 0 }}>
            Try it
          </h2>
          <p className="muted">
            Every account uses the password <code>Passw0rd!</code>. Sign in as different users and watch
            what the shop lets you do change.
          </p>
          <ul className="muted" style={{ margin: 0, paddingInlineStart: '1.25rem' }}>
            <li>
              <code>customer</code> — browse, buy, track orders
            </li>
            <li>
              <code>support</code> — read-only, and deliberately cannot check out
            </li>
            <li>
              <code>administrator</code> — everything, including the back office
            </li>
          </ul>
        </section>
      )}
    </div>
  );
}
