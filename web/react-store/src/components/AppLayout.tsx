import { useMemo } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useQuery } from '@tanstack/react-query';

import { useCurrentUser } from '../auth/useCurrentUser';
import { createShopApi } from '../lib/basket';
import { Icon } from './Icon';
import { ThemeToggle } from './ThemeToggle';

/**
 * The application shell: header, navigation, sign-in state, footer.
 *
 * Rendered once and wrapped around every route via React Router's `<Outlet>`,
 * so navigating between pages does not remount the header.
 *
 * The Angular storefront's shell uses `<router-outlet>` and a `RouterLink`
 * directive. Structurally the same, idiomatically different — which is the
 * point of building both (docs/adr/0014).
 */
export function AppLayout() {
  const auth = useAuth();
  const { user, isAuthenticated } = useCurrentUser();

  const api = useMemo(
    () => createShopApi(() => auth.user?.access_token ?? null),
    [auth.user?.access_token],
  );

  /**
   * The basket, read here purely for the count on the cart button.
   *
   * The SAME query key the basket page uses, so adding an item anywhere in the app updates this badge
   * without a refetch - TanStack Query hands both consumers the same cache entry. A separate key would
   * mean the header showing 2 while the basket showed 3, which is the classic symptom of a count kept
   * in its own piece of state.
   */
  const basketQuery = useQuery({
    queryKey: ['basket'],
    queryFn: () => api.getBasket(),
    enabled: isAuthenticated,
  });

  const itemCount = basketQuery.data?.totalUnits ?? 0;

  return (
    <div className="app">
      {/* Must be the first focusable element on the page to be useful. */}
      <a className="skip-link" href="#main">
        Skip to content
      </a>

      <header className="app-header">
        <div className="container app-header__inner">
          <NavLink to="/" className="app-header__brand">
            E-Commerce
          </NavLink>

          {/* aria-label distinguishes this landmark from any other nav on the
              page, so a screen reader announces "Main navigation". */}
          <nav className="app-header__nav" aria-label="Main">
            <NavLink to="/" className="nav-link" end>
              Home
            </NavLink>
            <NavLink to="/products" className="nav-link">
              Products
            </NavLink>
            {isAuthenticated && (
              // A fragment, because JSX needs a single parent and a wrapper <div> here would break the
              // flex layout of the nav bar.
              <>
                <NavLink to="/orders" className="nav-link">
                  Orders
                </NavLink>
                <NavLink to="/account" className="nav-link">
                  My account
                </NavLink>
              </>
            )}
          </nav>

          <div className="app-header__spacer" />

          <div className="app-header__actions">
            <ThemeToggle />

            {isAuthenticated && (
              /* The cart moves out of the text nav and becomes an icon button, which is where a
                 shopper looks for it. The accessible name carries the count, so a screen reader hears
                 "Basket, 3 items" in one go rather than a link and then a stray number. */
              <NavLink
                to="/basket"
                className="nav-link cart-button"
                aria-label={
                  itemCount === 1 ? 'Basket, 1 item' : `Basket, ${itemCount} items`
                }
              >
                <Icon name="cart" />
                {itemCount > 0 && (
                  /* aria-hidden: the count is already in the button's accessible name above. */
                  <span className="cart-badge" aria-hidden="true">
                    {itemCount > 99 ? '99+' : itemCount}
                  </span>
                )}
              </NavLink>
            )}

            {isAuthenticated ? (
              <>
                <span className="muted">{user?.displayName ?? user?.username}</span>
                <button
                  type="button"
                  className="btn btn--secondary"
                  // Ends the session at Keycloak too, not just locally. Clearing
                  // only local tokens leaves the Keycloak session alive, so the
                  // next "Sign in" silently logs the same user straight back in
                  // and looks like a broken logout.
                  onClick={() => void auth.signoutRedirect()}
                >
                  Sign out
                </button>
              </>
            ) : (
              <button
                type="button"
                className="btn btn--primary"
                onClick={() => void auth.signinRedirect()}
              >
                Sign in
              </button>
            )}
          </div>
        </div>
      </header>

      {/* tabIndex={-1} makes this programmatically focusable so the skip link
          can move focus here. */}
      <main id="main" className="app-main" tabIndex={-1}>
        <div className="container">
          <Outlet />
        </div>
      </main>

      <footer className="app-footer">
        <div className="container">
          <p style={{ margin: 0 }}>
            Reference .NET microservices platform — React storefront. The Angular storefront at{' '}
            <code>:4200</code> is functionally identical.
          </p>
        </div>
      </footer>
    </div>
  );
}
