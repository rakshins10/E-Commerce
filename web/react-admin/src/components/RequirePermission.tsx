import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';

import { useCurrentUser } from '../auth/useCurrentUser';
import type { Permission } from '../lib/permissions';

/**
 * Route guard: renders its children only if the user holds the permission.
 *
 * ---
 * **This is user experience, not security.** It stops an order manager clicking into a users page that
 * would only 403 them. It stops nothing at all from someone who opens devtools, copies the token and
 * calls the API directly — which is why the Admin BFF checks the permission at the edge *and* every
 * service checks it again.
 *
 * Worth being blunt about, because "the button is hidden" is the most common wrong answer to "how is
 * this endpoint protected?" in an interview. A guard that runs in the browser is a guard the user
 * controls.
 *
 * ---
 * **Why redirect to /forbidden rather than render an inline message.** A distinct URL is linkable and
 * appears in analytics, so "twelve people a day hit /forbidden on /admin/users" is a fact somebody can
 * act on — usually by fixing the role assignment rather than the code.
 */
export function RequirePermission({
  permission,
  children,
}: {
  permission: Permission;
  children: ReactNode;
}) {
  const { can, isLoading, isAuthenticated } = useCurrentUser();

  if (isLoading) {
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Loading…</p>
      </div>
    );
  }

  // Not signed in and not permitted are different situations with different remedies: one is "sign in",
  // the other is "ask someone for access". Collapsing them into one screen tells a user with the wrong
  // role to try signing in again, which will not help.
  if (!isAuthenticated) {
    return <Navigate to="/signin" replace />;
  }

  if (!can(permission)) {
    return <Navigate to="/forbidden" replace />;
  }

  return <>{children}</>;
}
