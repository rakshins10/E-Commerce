import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { Auth } from './auth/auth';
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
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ThemeToggle],
  templateUrl: './app.html',
})
export class App {
  protected readonly auth = inject(Auth);
}
