import { Injectable, computed, inject, signal } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';

import { hasPermission as localHasPermission } from '../core/permissions';
import { toAuthenticatedUser } from '../core/auth-config';
import type { AuthenticatedUser, Permission } from '../core/permissions';

/**
 * Auth state for the Angular storefront, exposed as signals.
 *
 * Uses the SAME `toAuthenticatedUser` and `hasPermission` from
 * core/auth-config.ts as the React storefront's lib/auth.ts, so both derive permissions
 * from a token identically. That is the point of the shared layer — if each
 * parsed claims itself, one would eventually read the wrong claim and the bug
 * would surface only as "permissions randomly missing in Angular".
 *
 * ---
 * **React/Angular divergence worth noting** (docs/react-vs-angular.md):
 *
 * React exposes this as a hook (`useCurrentUser`) that recomputes on render.
 * Angular exposes it as an injectable service holding signals, which any
 * component can read without prop-drilling and which updates every consumer
 * when it changes. Angular's version needs no provider wrapper in the component
 * tree — DI handles it — but it does need the bridge below from RxJS to
 * signals, because the OIDC library is Observable-based.
 */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly oidc = inject(OidcSecurityService);

  private readonly accessToken = signal<string | null>(null);
  private readonly authenticated = signal(false);
  private readonly loading = signal(true);

  /** The signed-in user, or null. Derived from the token, never stored separately. */
  readonly user = computed<AuthenticatedUser | null>(() => {
    const token = this.accessToken();
    return token ? toAuthenticatedUser(token) : null;
  });

  readonly isAuthenticated = computed(() => this.authenticated() && this.user() !== null);
  readonly isLoading = this.loading.asReadonly();

  readonly displayName = computed(() => {
    const user = this.user();
    return user?.displayName ?? user?.username ?? null;
  });

  /** Realm roles, minus Keycloak's built-in ones which are noise on screen. */
  readonly roles = computed(() =>
    (this.user()?.roles ?? []).filter(
      (role) =>
        !role.startsWith('default-roles') &&
        role !== 'offline_access' &&
        role !== 'uma_authorization',
    ),
  );

  readonly permissions = computed(() => [...(this.user()?.permissions ?? [])].sort());

  constructor() {
    // Bridge the library's Observable into signals. Called once at startup by
    // APP_INITIALIZER equivalent in app.config.ts.
    this.oidc.checkAuth().subscribe((response) => {
      this.authenticated.set(response.isAuthenticated);
      this.accessToken.set(response.accessToken || null);
      this.loading.set(false);
    });

    // Keep the token current after a silent renew, otherwise every request
    // after the first five minutes would carry an expired token.
    this.oidc.userData$.subscribe(() => {
      this.oidc.getAccessToken().subscribe((token) => this.accessToken.set(token || null));
    });
  }

  /**
   * Whether the signed-in user holds a permission.
   *
   * Decides what to *render*. The server enforces the same rule independently —
   * anyone can copy the token from devtools and call the API directly.
   */
  can(permission: Permission): boolean {
    return localHasPermission(this.user(), permission);
  }

  signIn(): void {
    this.oidc.authorize();
  }

  /**
   * Signs out at Keycloak, not just locally.
   *
   * Clearing only local tokens leaves the Keycloak session alive, so the next
   * "Sign in" logs the same user straight back in with no prompt — which looks
   * exactly like a broken logout.
   */
  signOut(): void {
    this.oidc.logoff().subscribe();
  }
}
