import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { Auth } from '../auth/auth';

/**
 * Home page.
 *
 * Phase 3 shows the auth state and the token's contents, because that is what
 * this phase built. Real product browsing arrives in Phase 4.
 *
 * Showing the permissions on screen is not filler — it is the fastest way to
 * see that signing in as `customer` versus `administrator` yields genuinely
 * different capabilities, and that they came from composite roles in Keycloak
 * rather than from anything this app decided.
 *
 * Content is word-for-word identical to the React `HomePage`: the shared
 * Playwright specs assert on visible text, so any difference fails the parity
 * run.
 */
@Component({
  selector: 'app-home-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (auth.isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Signing you in…</p>
      </div>
    } @else {
      <div class="stack">
        <h1 class="page-title">Storefront</h1>

        @if (auth.isAuthenticated()) {
          <p class="lede">
            Signed in as <strong>{{ auth.displayName() }}</strong>
          </p>

          <div class="grid grid--2">
            <section class="card stack" aria-labelledby="roles-heading">
              <h2 id="roles-heading" style="margin-top: 0">Roles</h2>
              <p class="muted">
                Coarse job titles from Keycloak. Nothing in this app is gated on these.
              </p>
              <div class="chips">
                @for (role of auth.roles(); track role) {
                  <span class="chip">{{ role }}</span>
                }
              </div>
            </section>

            <section class="card stack" aria-labelledby="permissions-heading">
              <h2 id="permissions-heading" style="margin-top: 0">
                Permissions
                <span class="badge badge--info">{{ auth.permissions().length }}</span>
              </h2>
              <p class="muted">
                Granted by composite roles, not assigned to you directly. These gate what the UI shows
                — the server enforces the same rules independently.
              </p>
              <div class="chips">
                @for (permission of auth.permissions(); track permission) {
                  <span class="chip">{{ permission }}</span>
                }
              </div>
            </section>
          </div>

          <section class="card stack" aria-labelledby="whats-next">
            <h2 id="whats-next" style="margin-top: 0">What arrives next</h2>
            <p class="muted">
              Phase 4 adds the Catalog service and the Storefront BFF, and this page becomes product
              browsing with search and filtering — built in React and Angular simultaneously.
            </p>
          </section>
        } @else {
          <p class="lede">
            A reference .NET microservices platform. Sign in to see how roles and permissions reach
            the browser.
          </p>

          <section class="card stack" aria-labelledby="try-heading">
            <h2 id="try-heading" style="margin-top: 0">Try it</h2>
            <p class="muted">
              Every account uses the password <code>Passw0rd!</code>. Sign in as different users and
              watch the permission list change.
            </p>
            <ul class="muted" style="margin: 0; padding-inline-start: 1.25rem">
              <li><code>customer</code> — 5 permissions</li>
              <li><code>support</code> — 4, all read-only</li>
              <li><code>catalogmgr</code> — 5, catalog and pricing</li>
              <li><code>ordermgr</code> — 7, orders and refunds</li>
              <li><code>administrator</code> — 15</li>
            </ul>
            <div>
              <button type="button" class="btn btn--primary" (click)="auth.signIn()">Sign in</button>
            </div>
          </section>
        }
      </div>
    }
  `,
})
export class HomePage {
  protected readonly auth = inject(Auth);
}
