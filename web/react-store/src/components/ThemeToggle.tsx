import { useEffect, useState } from 'react';

type Theme = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'ecommerce.theme';

/**
 * Light / dark / follow-the-system theme switch.
 *
 * Three states rather than two, deliberately. A two-state toggle cannot express
 * "follow my operating system", so a user who switches their OS to dark in the
 * evening keeps getting a light site. `system` is the default.
 *
 * The mechanism is one attribute on `<html>`. The design tokens define both
 * palettes and the CSS picks by `prefers-color-scheme` *and* by
 * `[data-theme]`, so removing the attribute falls back to the OS setting.
 * No JavaScript reads a colour — see web/design-tokens/build.mjs.
 *
 * The Angular storefront implements this with a signal and an effect. Same
 * behaviour, same attribute, same tokens; different idiom.
 */
export function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>(
    () => (localStorage.getItem(STORAGE_KEY) as Theme) ?? 'system',
  );

  useEffect(() => {
    const root = document.documentElement;

    if (theme === 'system') {
      root.removeAttribute('data-theme');
      localStorage.removeItem(STORAGE_KEY);
    } else {
      root.setAttribute('data-theme', theme);
      localStorage.setItem(STORAGE_KEY, theme);
    }
  }, [theme]);

  const next: Record<Theme, Theme> = { system: 'light', light: 'dark', dark: 'system' };
  const label: Record<Theme, string> = {
    system: 'Theme: follow system',
    light: 'Theme: light',
    dark: 'Theme: dark',
  };
  const icon: Record<Theme, string> = { system: '◐', light: '☀', dark: '☾' };

  return (
    <button
      type="button"
      className="btn btn--ghost"
      onClick={() => setTheme(next[theme])}
      // The icon alone conveys nothing to a screen reader, so the accessible
      // name carries the current state. The shared e2e specs query this button
      // by name, which is why it must match the Angular implementation exactly.
      aria-label={label[theme]}
      title={label[theme]}
    >
      <span aria-hidden="true">{icon[theme]}</span>
    </button>
  );
}
