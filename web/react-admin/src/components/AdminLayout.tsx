import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';

import { useCurrentUser } from '../auth/useCurrentUser';
import { Permissions } from '../lib/permissions';
import { ThemeToggle } from './ThemeToggle';
import type { Permission } from '../lib/permissions';

/**
 * The navigation, and the permission each item needs.
 *
 * ---
 * **Declared as data, not as a wall of `{can(x) && <NavLink/>}`.** With six items the conditional
 * version is already hard to scan, and it is the shape where somebody eventually adds an item and
 * forgets the guard — producing a link that leads straight to a 403.
 *
 * Keeping the permission next to the label means the two cannot drift apart, and the nav's entire
 * authorization surface is readable in one glance.
 */
const NAVIGATION: readonly { to: string; label: string; permission: Permission }[] = [
  { to: '/', label: 'Dashboard', permission: Permissions.Admin.DashboardRead },
  { to: '/catalog', label: 'Catalogue', permission: Permissions.Catalog.Read },
  { to: '/orders', label: 'Orders', permission: Permissions.Order.Read },
  { to: '/inventory', label: 'Inventory', permission: Permissions.Inventory.Read },
  { to: '/users', label: 'Users', permission: Permissions.Users.Read },
  { to: '/audit', label: 'Audit log', permission: Permissions.Admin.AuditRead },
];

/**
 * The back-office shell.
 *
 * ---
 * **Different roles see genuinely different navigation.** A support agent signing in sees Orders,
 * Inventory and Users; an order manager sees Orders and Inventory but no Users; an administrator sees
 * everything. Same application, same build — the token decides.
 *
 * That is the practical payoff of guarding on permissions rather than roles: adding a permission to a
 * composite in Keycloak changes what people see with **no deployment**.
 */
export function AdminLayout() {
  const auth = useAuth();
  const { can, isAuthenticated, user } = useCurrentUser();

  const visible = NAVIGATION.filter((item) => can(item.permission));

  return (
    <div className="app">
      {/* Must be the first focusable element to be useful. WCAG 2.4.1. */}
      <a className="skip-link" href="#main">
        Skip to content
      </a>

      <header className="app-header">
        <div className="container app-header__inner">
          <NavLink to="/" className="app-header__brand">
            Back office
          </NavLink>

          <nav className="app-header__nav" aria-label="Main">
            {visible.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.to === '/'}
                className="nav-link"
                // aria-current is set from the SAME source as the visual state, so the two cannot
                // disagree - the mistake that makes an active link invisible to a screen reader.
                aria-current={undefined}
              >
                {item.label}
              </NavLink>
            ))}
          </nav>

          <div className="app-header__spacer" />

          <div className="app-header__actions">
            <ThemeToggle />

            {isAuthenticated ? (
              <>
                <span className="muted">{user?.username}</span>
                <button
                  type="button"
                  className="btn btn--secondary"
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

      <main id="main" className="app-main" tabIndex={-1}>
        <div className="container">
          <Outlet />
        </div>
      </main>

      <footer className="app-footer">
        <div className="container">
          <p style={{ margin: 0 }}>
            Back office — React. The Angular back office at <code>:4201</code> is functionally
            identical.
          </p>
        </div>
      </footer>
    </div>
  );
}
