import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';

import { Auth } from '../auth/auth';
import type { Permission } from './permissions';

/**
 * Route guard: allows the route only if the user holds the permission.
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
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * React wraps each element in a `<RequirePermission>` component; Angular attaches a `canActivate`
 * function to the route. Angular's runs **before** the component is created, so a forbidden route never
 * mounts and never fires its data request. React's guard renders, redirects, and only then unmounts —
 * which is harmless here but means a guarded component's `useQuery` can briefly fire.
 *
 * A real point for Angular's router, and the reason this file is a factory rather than a component.
 */
export function requirePermission(permission: Permission): CanActivateFn {
  return () => {
    const auth = inject(Auth);
    const router = inject(Router);

    // Not signed in and not permitted are different situations with different remedies: one is "sign
    // in", the other is "ask someone for access". Collapsing them tells a user with the wrong role to
    // try signing in again, which will not help.
    if (!auth.isAuthenticated()) {
      return router.createUrlTree(['/signin']);
    }

    if (!auth.can(permission)) {
      // A distinct URL rather than an inline message: it is linkable and appears in analytics, so
      // "twelve people a day hit /forbidden on /users" is a fact somebody can act on - usually by
      // fixing the role assignment rather than the code.
      return router.createUrlTree(['/forbidden']);
    }

    return true;
  };
}
