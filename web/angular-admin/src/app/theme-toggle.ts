import { Component, ChangeDetectionStrategy, effect, signal } from '@angular/core';

type Theme = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'ecommerce.theme';

/**
 * Light / dark / follow-the-system theme switch.
 *
 * Behaviourally identical to the React `ThemeToggle` — same three states, same
 * `data-theme` attribute, same accessible names, so the shared Playwright specs
 * pass against both.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md):
 *
 * React uses `useState` + `useEffect` with a dependency array. Angular uses a
 * `signal` + `effect`, where the effect tracks its dependencies automatically —
 * there is no array to get wrong, which removes an entire class of stale-closure
 * bug. The trade is that what the effect depends on is implicit rather than
 * written down.
 */
@Component({
  selector: 'app-theme-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="btn btn--ghost"
      (click)="cycle()"
      [attr.aria-label]="label()"
      [title]="label()"
    >
      <span aria-hidden="true">{{ icon() }}</span>
    </button>
  `,
})
export class ThemeToggle {
  protected readonly theme = signal<Theme>(
    (localStorage.getItem(STORAGE_KEY) as Theme | null) ?? 'system',
  );

  protected readonly label = () =>
    ({
      system: 'Theme: follow system',
      light: 'Theme: light',
      dark: 'Theme: dark',
    })[this.theme()];

  protected readonly icon = () => ({ system: '◐', light: '☀', dark: '☾' })[this.theme()];

  constructor() {
    effect(() => {
      const theme = this.theme();
      const root = document.documentElement;

      if (theme === 'system') {
        // Removing the attribute lets the prefers-color-scheme media query in
        // the design tokens take over again.
        root.removeAttribute('data-theme');
        localStorage.removeItem(STORAGE_KEY);
      } else {
        root.setAttribute('data-theme', theme);
        localStorage.setItem(STORAGE_KEY, theme);
      }
    });
  }

  protected cycle(): void {
    const next: Record<Theme, Theme> = { system: 'light', light: 'dark', dark: 'system' };
    this.theme.update((current) => next[current]);
  }
}
