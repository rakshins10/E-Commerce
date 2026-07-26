/**
 * OIDC wiring for the React storefront.
 *
 * The configuration lives in src/lib/auth.ts. The Angular
 * storefront also uses — so both apps request identical scopes and redirect
 * URIs. Only the plumbing below is React-specific.
 *
 * @see docs/authorization-model.md
 * @see docs/concepts-explained.md#17-oauth2-oidc-jwt-and-pkce
 */

import { WebStorageStateStore } from 'oidc-client-ts';
import type { AuthProviderProps } from 'react-oidc-context';
import { createOidcConfig } from '../lib/auth';

const authority =
  import.meta.env.VITE_KEYCLOAK_AUTHORITY ?? 'http://localhost:8080/realms/ecommerce';

const shared = createOidcConfig({
  authority,
  clientId: 'storefront-react',
  origin: window.location.origin,
});

export const oidcConfig: AuthProviderProps = {
  authority: shared.authority,
  client_id: shared.clientId,
  redirect_uri: shared.redirectUri,
  post_logout_redirect_uri: shared.postLogoutRedirectUri,
  scope: shared.scope,
  silent_redirect_uri: shared.silentRedirectUri,

  // Authorization Code + PKCE. There is deliberately NO client_secret: this
  // bundle is downloaded to the user's browser, so anything in it is readable.
  // PKCE proves possession of a verifier the app generated, which an attacker
  // who intercepts the authorization code does not have.
  response_type: 'code',

  // Renew the access token in the background before it expires (ours last 5
  // minutes). Without this the user is bounced to the login page mid-session.
  automaticSilentRenew: true,

  // Tokens live in sessionStorage rather than localStorage: they are cleared
  // when the tab closes and are not shared across tabs, which limits the blast
  // radius of an XSS payload. In-memory would be safer still, but costs a full
  // redirect on every page refresh. This is the usual pragmatic middle ground
  // for a SPA - and the real mitigation for XSS is not storing tokens
  // differently, it is not having XSS.
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),

  // Strip ?code=&state= from the URL after the redirect, so a refresh does not
  // attempt to redeem an already-used authorization code.
  onSigninCallback: () => {
    window.history.replaceState({}, document.title, window.location.pathname);
  },
};

/** Where the storefront BFF lives. */
export const bffBaseUrl = import.meta.env.VITE_BFF_URL ?? 'http://localhost:6001';
