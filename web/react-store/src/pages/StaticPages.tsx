import { Link } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';

/**
 * Placeholder for the product list. Real browsing arrives in Phase 4 with the
 * Catalog service and the Storefront BFF.
 *
 * It exists now so the navigation is complete and the shared e2e specs have a
 * second route to exercise - a nav link that goes nowhere cannot be tested.
 */
export function ProductsPage() {
  return (
    <div className="stack">
      <h1 className="page-title">Products</h1>
      <p className="lede">Browsing arrives in Phase 4.</p>
      <section className="card">
        <p className="muted" style={{ margin: 0 }}>
          This page will list products from the Catalog service via the Storefront BFF, with search,
          filtering and server-side pagination — built in React and Angular in the same pull request.
        </p>
      </section>
    </div>
  );
}

/**
 * OIDC redirect target.
 *
 * Keycloak sends the browser back here with `?code=&state=`. The auth library
 * exchanges the code for tokens (using the PKCE verifier it kept), then
 * `onSigninCallback` strips the query string so a refresh cannot try to redeem
 * an already-used code.
 *
 * This renders only for the moment that exchange takes.
 */
export function AuthCallbackPage() {
  const auth = useAuth();

  if (auth.error) {
    return (
      <div className="centred">
        <div className="card stack" role="alert">
          <h1 className="page-title">Sign-in failed</h1>
          <p className="muted">{auth.error.message}</p>
          <div>
            <Link className="btn btn--primary" to="/">
              Back to the storefront
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="centred" aria-busy="true" aria-live="polite">
      <p className="lede">Completing sign-in…</p>
    </div>
  );
}

/**
 * Target of the hidden silent-renew iframe.
 *
 * Access tokens last five minutes. Rather than interrupting the user with a
 * redirect, the library loads this route in an invisible iframe, Keycloak
 * recognises the still-valid session cookie, and a fresh token comes back with
 * no interaction at all. Deliberately renders nothing.
 */
export function SilentRenewPage() {
  return null;
}

/** 404. */
export function NotFoundPage() {
  return (
    <div className="centred">
      <div className="card stack">
        <h1 className="page-title">Page not found</h1>
        <p className="muted">That page does not exist.</p>
        <div>
          <Link className="btn btn--primary" to="/">
            Back to the storefront
          </Link>
        </div>
      </div>
    </div>
  );
}

/**
 * 403.
 *
 * Unused until the admin panel in Phase 8, but the route exists now so both
 * storefronts share the same shape. Worth stating plainly on screen: hiding a
 * page is a courtesy, and the server is what actually refuses.
 */
export function ForbiddenPage() {
  return (
    <div className="centred">
      <div className="card stack" role="alert">
        <h1 className="page-title">Not permitted</h1>
        <p className="muted">
          Your account does not have the permission this page requires. The server enforces this
          independently of what the interface shows.
        </p>
        <div>
          <Link className="btn btn--primary" to="/">
            Back to the storefront
          </Link>
        </div>
      </div>
    </div>
  );
}
