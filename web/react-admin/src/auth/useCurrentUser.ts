/**
 * Turns the OIDC library's user object into this app's `AuthenticatedUser`.
 *
 * The parsing itself lives in `src/lib/auth.ts`, owned by this application.
 * The Angular storefront has its own equivalent in `core/auth-config.ts`; the
 * two are kept in step by the shared end-to-end suite rather than by sharing
 * code. See docs/adr/0018-self-contained-frontends.md.
 */

import { useMemo } from 'react';
import { useAuth } from 'react-oidc-context';

import { hasPermission as localHasPermission } from '../lib/permissions';
import { toAuthenticatedUser } from '../lib/auth';
import type { AuthenticatedUser, Permission } from '../lib/permissions';

export interface CurrentUserState {
  readonly user: AuthenticatedUser | null;
  readonly isAuthenticated: boolean;
  readonly isLoading: boolean;
  /**
   * Whether the signed-in user holds a permission.
   *
   * UI convenience only — it decides what to *render*. The server enforces the
   * same rule independently on every request, because anyone can copy the token
   * out of devtools and call the API directly.
   */
  readonly can: (permission: Permission) => boolean;
}

export function useCurrentUser(): CurrentUserState {
  const auth = useAuth();

  const user = useMemo(
    () => (auth.user?.access_token ? toAuthenticatedUser(auth.user.access_token) : null),
    [auth.user?.access_token],
  );

  return {
    user,
    isAuthenticated: auth.isAuthenticated && user !== null,
    isLoading: auth.isLoading,
    can: (permission) => localHasPermission(user, permission),
  };
}
