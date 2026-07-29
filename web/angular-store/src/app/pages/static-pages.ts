import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Auth } from '../auth/auth';

/**
 * OIDC redirect target.
 *
 * Keycloak sends the browser back here with `?code=&state=`. The auth library
 * exchanges the code for tokens using the PKCE verifier it kept. This renders
 * only for the moment that exchange takes.
 */
@Component({
  selector: 'app-auth-callback-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="centred" aria-busy="true" aria-live="polite">
      <p class="lede">Completing sign-in…</p>
    </div>
  `,
})
export class AuthCallbackPage {
  protected readonly auth = inject(Auth);
}

/**
 * Target of the hidden silent-renew iframe.
 *
 * Access tokens last five minutes. Rather than interrupting the user with a
 * redirect, the library loads this route in an invisible iframe, Keycloak
 * recognises the still-valid session cookie, and a fresh token comes back with
 * no interaction. Deliberately renders nothing.
 */
@Component({
  selector: 'app-silent-renew-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
export class SilentRenewPage {}

/** 404. */
@Component({
  selector: 'app-not-found-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="centred">
      <div class="card stack">
        <h1 class="page-title">Page not found</h1>
        <p class="muted">That page does not exist.</p>
        <div><a class="btn btn--primary" routerLink="/">Back to the storefront</a></div>
      </div>
    </div>
  `,
})
export class NotFoundPage {}

/**
 * 403.
 *
 * Unused until the admin panel in Phase 8, but the route exists now so both
 * storefronts share the same shape.
 */
@Component({
  selector: 'app-forbidden-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="centred">
      <div class="card stack" role="alert">
        <h1 class="page-title">Not permitted</h1>
        <p class="muted">
          Your account does not have the permission this page requires. The server enforces this
          independently of what the interface shows.
        </p>
        <div><a class="btn btn--primary" routerLink="/">Back to the storefront</a></div>
      </div>
    </div>
  `,
})
export class ForbiddenPage {}
