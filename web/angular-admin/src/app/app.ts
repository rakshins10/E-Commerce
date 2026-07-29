import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { Auth } from './auth/auth';
import { Permissions } from './core/permissions';
import { ThemeToggle } from './theme-toggle';
import type { Permission } from './core/permissions';

/**
 * The navigation, and the permission each item needs.
 *
 * ---
 * **Declared as data, not as a wall of `@if (auth.can(x))`.** With six items the conditional version is
 * already hard to scan, and it is the shape where somebody eventually adds an item and forgets the
 * guard — producing a link that leads straight to a 403.
 *
 * Keeping the permission next to the label means the two cannot drift apart, and the nav's entire
 * authorization surface is readable in one glance.
 */
const NAVIGATION: readonly { to: string; label: string; permission: Permission }[] = [
  { to: '/', label: 'Dashboard', permission: Permissions.Admin.DashboardRead },
  { to: '/orders', label: 'Orders', permission: Permissions.Order.Read },
  { to: '/inventory', label: 'Inventory', permission: Permissions.Inventory.Read },
  { to: '/users', label: 'Users', permission: Permissions.Users.Read },
  { to: '/audit', label: 'Audit log', permission: Permissions.Admin.AuditRead },
];

/**
 * The back-office shell.
 *
 * ---
 * **Different roles see genuinely different navigation.** A support agent signing in sees Orders,
 * Inventory and Users; an order manager sees Orders and Inventory but no Users; an administrator sees
 * everything. Same application, same build — the token decides.
 *
 * That is the practical payoff of guarding on permissions rather than roles: adding a permission to a
 * composite in Keycloak changes what people see with **no deployment**.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ThemeToggle],
  template: `
    <div class="app">
      <!-- Must be the first focusable element to be useful. WCAG 2.4.1. -->
      <a class="skip-link" href="#main">Skip to content</a>

      <header class="app-header">
        <div class="container app-header__inner">
          <a routerLink="/" class="app-header__brand">Back office</a>

          <nav class="app-header__nav" aria-label="Main">
            @for (item of visible(); track item.to) {
              <a
                [routerLink]="item.to"
                routerLinkActive="is-active"
                ariaCurrentWhenActive="page"
                [routerLinkActiveOptions]="{ exact: item.to === '/' }"
                class="nav-link"
                >{{ item.label }}</a
              >
            }
          </nav>

          <div class="app-header__spacer"></div>

          <div class="app-header__actions">
            <app-theme-toggle />

            @if (auth.isAuthenticated()) {
              <span class="muted">{{ auth.user()?.username }}</span>
              <button type="button" class="btn btn--secondary" (click)="auth.signOut()">Sign out</button>
            } @else {
              <button type="button" class="btn btn--primary" (click)="auth.signIn()">Sign in</button>
            }
          </div>
        </div>
      </header>

      <main id="main" class="app-main" tabindex="-1">
        <div class="container">
          <router-outlet />
        </div>
      </main>

      <footer class="app-footer">
        <div class="container">
          <p style="margin: 0">
            Back office — Angular. The React back office at <code>:3001</code> is functionally
            identical.
          </p>
        </div>
      </footer>
    </div>
  `,
})
export class App {
  protected readonly auth = inject(Auth);

  /**
   * The navigation items this user may see.
   *
   * `computed`, so it recalculates the moment the token changes — after a silent renew that picked up
   * a newly granted role, the nav updates without a refresh.
   */
  protected readonly visible = computed(() =>
    NAVIGATION.filter((item) => this.auth.can(item.permission)),
  );
}
