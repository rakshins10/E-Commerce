import { useAuth } from 'react-oidc-context';
import { useCurrentUser } from '../auth/useCurrentUser';

/**
 * Home page.
 *
 * Phase 3 shows the auth state and the token's contents, because that is what
 * this phase actually built. Real product browsing arrives in Phase 4 and this
 * becomes the storefront landing page.
 *
 * Showing the permissions on screen is not just filler - it is the fastest way
 * to see that logging in as `customer` and as `administrator` produces
 * genuinely different capabilities, and that they came from composite roles in
 * Keycloak rather than from anything this app decided.
 */
export function HomePage() {
  const auth = useAuth();
  const { user, isAuthenticated, isLoading } = useCurrentUser();

  if (isLoading) {
    // aria-busy + a live region so a screen reader announces the wait rather
    // than reading an empty page.
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Signing you in…</p>
      </div>
    );
  }

  return (
    <div className="stack">
      <h1 className="page-title">Storefront</h1>

      {auth.error && (
        // role="alert" is announced immediately, which is what you want for an
        // authentication failure.
        <div className="card" role="alert">
          <h2 style={{ marginTop: 0 }}>Sign-in failed</h2>
          <p className="muted">{auth.error.message}</p>
        </div>
      )}

      {isAuthenticated ? (
        <>
          <p className="lede">
            Signed in as <strong>{user?.displayName ?? user?.username}</strong>
          </p>

          <div className="grid grid--2">
            <section className="card stack" aria-labelledby="roles-heading">
              <h2 id="roles-heading" style={{ marginTop: 0 }}>
                Roles
              </h2>
              <p className="muted">
                Coarse job titles from Keycloak. Nothing in this app is gated on these.
              </p>
              <div className="chips">
                {user?.roles
                  .filter((role) => !role.startsWith('default-roles') && role !== 'offline_access' && role !== 'uma_authorization')
                  .map((role) => (
                    <span key={role} className="chip">
                      {role}
                    </span>
                  ))}
              </div>
            </section>

            <section className="card stack" aria-labelledby="permissions-heading">
              <h2 id="permissions-heading" style={{ marginTop: 0 }}>
                Permissions <span className="badge badge--info">{user?.permissions.length ?? 0}</span>
              </h2>
              <p className="muted">
                Granted by composite roles, not assigned to you directly. These gate what the UI shows —
                the server enforces the same rules independently.
              </p>
              <div className="chips">
                {[...(user?.permissions ?? [])].sort().map((permission) => (
                  <span key={permission} className="chip">
                    {permission}
                  </span>
                ))}
              </div>
            </section>
          </div>

          <section className="card stack" aria-labelledby="whats-next">
            <h2 id="whats-next" style={{ marginTop: 0 }}>
              What arrives next
            </h2>
            <p className="muted">
              Phase 4 adds the Catalog service and the Storefront BFF, and this page becomes product
              browsing with search and filtering — built in React and Angular simultaneously.
            </p>
          </section>
        </>
      ) : (
        <>
          <p className="lede">
            A reference .NET microservices platform. Sign in to see how roles and permissions reach the
            browser.
          </p>

          <section className="card stack" aria-labelledby="try-heading">
            <h2 id="try-heading" style={{ marginTop: 0 }}>
              Try it
            </h2>
            <p className="muted">
              Every account uses the password <code>Passw0rd!</code>. Sign in as different users and watch
              the permission list change.
            </p>
            <ul className="muted" style={{ margin: 0, paddingInlineStart: '1.25rem' }}>
              <li>
                <code>customer</code> — 5 permissions
              </li>
              <li>
                <code>support</code> — 4, all read-only
              </li>
              <li>
                <code>catalogmgr</code> — 5, catalog and pricing
              </li>
              <li>
                <code>ordermgr</code> — 7, orders and refunds
              </li>
              <li>
                <code>administrator</code> — 15
              </li>
            </ul>
            <div>
              <button
                type="button"
                className="btn btn--primary"
                onClick={() => void auth.signinRedirect()}
              >
                Sign in
              </button>
            </div>
          </section>
        </>
      )}
    </div>
  );
}
