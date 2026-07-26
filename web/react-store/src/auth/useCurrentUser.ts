/**
 * Turns the OIDC library's user object into the app's `AuthenticatedUser`,
 * using the shared parser so React and Angular derive permissions identically.
 */

import { useMemo } from 'react';
import { useAuth } from 'react-oidc-context';
import {
  hasPermission as sharedHasPermission,
  toAuthenticatedUser,
  type AuthenticatedUser,
  type Permission,
} from '@ecommerce/shared';

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
    can: (permission) => sharedHasPermission(user, permission),
  };
}
