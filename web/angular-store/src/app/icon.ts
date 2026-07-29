import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * The icon set.
 *
 * ---
 * **Inline SVG rather than an icon font or a library.** No extra network request, no flash of missing
 * glyph before a font loads, and `currentColor` means an icon inherits the colour of whatever it sits
 * inside — so a button that changes colour on hover does not need a second rule to keep its icon in
 * step.
 *
 * ---
 * **Icons here are always decorative.** Every one sits beside real text, or inside a control whose
 * accessible name is set explicitly. `aria-hidden` is therefore unconditional: an icon announced as
 * "shopping cart" next to the word "Basket" is repetition, and the shared Playwright specs select by
 * accessible name, so a stray label would change what they match.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * React's version is a function returning JSX and needs no registration. Angular's is a component that
 * every consumer must add to its `imports` array — more ceremony, and the compiler tells you when you
 * forget, which React's version cannot.
 */
const PATHS: Record<string, string> = {
  cart: 'M3 4h2l2.4 11.2a2 2 0 0 0 2 1.6h7.7a2 2 0 0 0 2-1.6L21 8H6M9 21a1 1 0 1 0 0-2 1 1 0 0 0 0 2Zm9 0a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z',
  user: 'M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z',
  package:
    'M21 8v8a2 2 0 0 1-1 1.7l-7 4a2 2 0 0 1-2 0l-7-4A2 2 0 0 1 3 16V8a2 2 0 0 1 1-1.7l7-4a2 2 0 0 1 2 0l7 4A2 2 0 0 1 21 8ZM3.3 7 12 12l8.7-5M12 22V12',
  search: 'M21 21l-4.3-4.3M19 11a8 8 0 1 1-16 0 8 8 0 0 1 16 0Z',
  trash: 'M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2m3 0v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6',
  check: 'M20 6 9 17l-5-5',
  chevronRight: 'm9 18 6-6-6-6',
  store: 'M3 9 5 3h14l2 6M3 9v11a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V9M3 9h18M9 21v-6h6v6',
  tag: 'M20.6 13.4 12 22l-9-9V3h10l7.6 7.6a2 2 0 0 1 0 2.8ZM7.5 7.5h.01',
  truck: 'M10 17h4V5H2v12h3m5 0a2 2 0 1 0 4 0m-4 0a2 2 0 1 1 4 0m5 0h2v-5l-3-4h-4v9h1m4 0a2 2 0 1 1-4 0m4 0a2 2 0 1 0-4 0',
  shield: 'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z',
  boxOpen: 'M3 7h18v13a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V7ZM3 7l2-4h14l2 4M12 3v4M8 12h8',
};

export type IconName = keyof typeof PATHS;

@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg [class]="cssClass()" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path [attr.d]="path()" />
    </svg>
  `,
})
export class Icon {
  readonly name = input.required<IconName>();

  readonly variant = input<string>('');

  protected readonly path = computed(() => PATHS[this.name()] ?? '');

  protected readonly cssClass = computed(() =>
    this.variant() ? `icon ${this.variant()}` : 'icon',
  );
}
