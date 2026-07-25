/**
 * OIDC configuration and token parsing, shared by every client.
 *
 * Written once here so the React storefront, the Angular storefront and the
 * mobile app cannot disagree about what a token means. If each parsed claims
 * itself, one of them would eventually read the wrong claim and the bug would
 * only show up as "permissions randomly missing in Angular".
 *
 * @see docs/authorization-model.md
 * @see docs/concepts-explained.md#17-oauth2-oidc-jwt-and-pkce
 */

import type { AuthenticatedUser } from '../permissions/index.js';

/** Which Keycloak client an application authenticates as. */
export type ClientId =
  | 'storefront-react'
  | 'storefront-angular'
  | 'admin-react'
  | 'admin-angular'
  | 'mobile';

export interface OidcConfig {
  /** Realm URL, e.g. `http://localhost:8080/realms/ecommerce`. */
  readonly authority: string;
  readonly clientId: ClientId;
  readonly redirectUri: string;
  readonly postLogoutRedirectUri: string;
  readonly scope: string;
  readonly silentRedirectUri?: string;
}

/**
 * Builds an OIDC configuration.
 *
 * **Note what is absent: there is no client secret.** These are *public*
 * clients. A secret shipped inside a JavaScript bundle is readable by anyone
 * who opens devtools, so it is not a secret. PKCE replaces it — the library
 * generates a random verifier, sends only its hash to start the flow, and must
 * present the original to redeem the authorization code. An intercepted code is
 * useless without it.
 */
export function createOidcConfig(options: {
  authority: string;
  clientId: ClientId;
  origin: string;
}): OidcConfig {
  const { authority, clientId, origin } = options;

  return {
    authority,
    clientId,
    redirectUri: `${origin}/auth/callback`,
    postLogoutRedirectUri: `${origin}/`,
    // `openid` identifies the user; `profile` and `email` add name and address.
    // `offline_access` is what yields a refresh token, without which the session
    // ends when the short-lived access token expires (5 minutes in our realm).
    scope: 'openid profile email offline_access',
    silentRedirectUri: `${origin}/auth/silent-renew`,
  };
}

/** The claims our realm puts in an access token. */
interface AccessTokenClaims {
  sub: string;
  preferred_username?: string;
  email?: string;
  name?: string;
  given_name?: string;
  family_name?: string;
  exp: number;
  iat: number;
  iss: string;
  aud: string | string[];
  /** Flattened by a protocol mapper in the realm. */
  permissions?: string[];
  realm_access?: { roles?: string[] };
}

/**
 * Decodes a JWT payload **without verifying it**.
 *
 * ---
 * **This does not and cannot validate anything.** It reads the middle segment
 * of the token, which is plain base64url — not encrypted, merely signed.
 * Anyone can read a JWT; they simply cannot forge one.
 *
 * Verification requires the issuer's public key and happens **on the server**,
 * on every request. The client decodes only to decide what to render. A client
 * that "validated" a token would be asking the token whether to trust itself.
 *
 * Corollary: never put anything secret in a JWT.
 */
export function decodeToken(accessToken: string): AccessTokenClaims | null {
  try {
    const payload = accessToken.split('.')[1];
    if (!payload) return null;

    // base64url -> base64, then pad to a multiple of 4.
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');

    // `atob` only, deliberately: it is global in browsers, in React Native and
    // in Node 18+. Reaching for Node's `Buffer` as a fallback would drag
    // @types/node into a package that must stay platform-neutral.
    //
    // The percent-encoding dance is not decoration - atob yields Latin-1, so a
    // name containing any non-ASCII character (Zoë, 日本語) comes back mangled
    // without it.
    const json = decodeURIComponent(
      atob(padded)
        .split('')
        .map((c) => '%' + c.charCodeAt(0).toString(16).padStart(2, '0'))
        .join(''),
    );

    return JSON.parse(json) as AccessTokenClaims;
  } catch {
    // A malformed token is not an exceptional condition - it is an expired or
    // corrupted one. Returning null lets the caller treat it as "signed out".
    return null;
  }
}

/**
 * Maps a raw access token to the user shape the UI works with.
 *
 * One implementation, so React and Angular cannot disagree about where
 * permissions live.
 */
export function toAuthenticatedUser(accessToken: string): AuthenticatedUser | null {
  const claims = decodeToken(accessToken);
  if (!claims?.sub) return null;

  const displayName =
    claims.name ??
    [claims.given_name, claims.family_name].filter(Boolean).join(' ') ??
    undefined;

  return {
    id: claims.sub,
    username: claims.preferred_username ?? claims.sub,
    email: claims.email,
    displayName: displayName || undefined,
    roles: claims.realm_access?.roles ?? [],
    permissions: claims.permissions ?? [],
  };
}

/**
 * Whether the token has expired, with a safety margin.
 *
 * The margin matters: a token valid for two more seconds will expire in flight,
 * and the request fails with a 401 that looks like a bug. Refreshing slightly
 * early avoids the whole class of problem.
 */
export function isTokenExpired(accessToken: string, marginSeconds = 30): boolean {
  const claims = decodeToken(accessToken);
  if (!claims?.exp) return true;

  return claims.exp * 1000 <= Date.now() + marginSeconds * 1000;
}

/**
 * Where tokens may be kept.
 *
 * - **`memory`** — safest in a browser. A token in a JavaScript variable cannot
 *   be read by an XSS payload that only reaches storage, and it disappears when
 *   the tab closes. The cost is a silent re-authentication on page refresh,
 *   which is what the silent-renew iframe is for.
 * - **`session`** — survives a refresh, readable by any script on the origin.
 * - **`secure`** — the OS keychain. React Native only, and the only acceptable
 *   choice there: `AsyncStorage` is unencrypted plain text on the device, so a
 *   rooted phone or a filesystem backup exposes every stored token.
 */
export type TokenStorageStrategy = 'memory' | 'session' | 'secure';
