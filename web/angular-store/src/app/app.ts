import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { Auth } from './auth/auth';
import { BasketService } from './core/basket';
import { Icon } from './icon';
import { ThemeToggle } from './theme-toggle';

/**
 * The application shell: header, navigation, sign-in state, footer.
 *
 * Visually identical to the React `AppLayout`. Structurally the same too —
 * a persistent shell around a routed outlet.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md):
 *
 * React wraps routes in a layout element and renders `<Outlet />`; Angular puts
 * `<router-outlet>` in the root component. React's `NavLink` sets an active
 * class via a render-prop; Angular's `routerLinkActive` is a directive, and
 * `ariaCurrentWhenActive` sets `aria-current="page"` declaratively — which is
 * genuinely tidier, because the accessible state and the visual state come from
 * the same directive and cannot disagree.
 *
 * Angular also gets `Auth` by injection with no provider wrapper in the tree,
 * where React needs `<AuthProvider>` at the root and a hook to reach it.
 *
 * ---
 * **The cart badge is where the two frameworks diverge most visibly.**
 *
 * Both apps show a count in the header that must never disagree with the basket page. React reads the
 * basket through TanStack Query under the *same query key* the basket page uses, so both consumers get
 * the same cache entry and one mutation updates both. Angular reads a `computed` off the singleton
 * `BasketService` signal, so both consumers read the same signal and one `set` updates both.
 *
 * Different mechanisms, same rule: **one source of truth, derived in two places** — never a count kept
 * in its own piece of state, which is how a header ends up showing 2 while the basket shows 3.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ThemeToggle, Icon],
  templateUrl: './app.html',
})
export class App {
  protected readonly auth = inject(Auth);
  private readonly basketService = inject(BasketService);

  protected readonly itemCount = this.basketService.itemCount;

  /** "Basket, 1 item" rather than "Basket, 1 items" — the count is read aloud, so it has to read. */
  protected readonly basketLabel = () =>
    this.itemCount() === 1 ? 'Basket, 1 item' : `Basket, ${this.itemCount()} items`;

  constructor() {
    // Sign-in resolves after the shell has rendered, so the badge needs a load once it does — the
    // equivalent of React's `enabled: isAuthenticated`. The effect re-runs on sign-out too, where the
    // guard leaves the previous basket in place; navigating anywhere would 401 first anyway.
    effect(() => {
      if (this.auth.isAuthenticated()) {
        void this.basketService.load().catch(() => {
          // A header badge is not worth failing the shell over. The basket page reports its own errors.
        });
      }
    });
  }
}
