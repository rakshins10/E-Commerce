import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useCurrentUser } from '../auth/useCurrentUser';
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
              <NavLink to="/account" className="nav-link">
                My account
              </NavLink>
            )}
          </nav>

          <div className="app-header__spacer" />

          <div className="app-header__actions">
            <ThemeToggle />

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
