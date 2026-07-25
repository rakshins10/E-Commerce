// =============================================================================
//  Generates platform-specific token files from tokens.json
// =============================================================================
//  tokens.json -> dist/tokens.css        CSS custom properties, light + dark
//              -> dist/tokens.ts         typed constants for TS consumers
//              -> dist/tokens.native.ts  React Native flat values
//
//  It also VALIDATES colour contrast and fails the build on anything below
//  WCAG 2.2 AA. That check is the reason this is a build step rather than a
//  hand-written CSS file: contrast is easy to get wrong by eye, easy to miss in
//  review, and impossible to notice later without an audit. Making it a build
//  failure means an inaccessible colour pair can never reach main.
//
//  See web/design-tokens/README.md.
// =============================================================================

import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const tokens = JSON.parse(readFileSync(resolve(here, 'tokens.json'), 'utf8'));

const isMeta = (key) => key.startsWith('$');
const isThemed = (value) =>
  value && typeof value === 'object' && 'light' in value && 'dark' in value;

// -----------------------------------------------------------------------------
//  Flatten the nested token tree into --kebab-case-names
// -----------------------------------------------------------------------------
function flatten(node, path = [], out = { light: {}, dark: {} }) {
  for (const [key, value] of Object.entries(node)) {
    if (isMeta(key)) continue;

    const next = [...path, key];

    if (isThemed(value)) {
      const name = cssName(next);
      out.light[name] = value.light;
      out.dark[name] = value.dark;
    } else if (value && typeof value === 'object') {
      flatten(value, next, out);
    } else {
      // Not theme-dependent (spacing, radii, font sizes): the same in both.
      const name = cssName(next);
      out.light[name] = value;
      out.dark[name] = value;
    }
  }
  return out;
}

const cssName = (path) =>
  path
    .map((segment) => segment.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase())
    .join('-');

const flat = flatten(tokens);

// -----------------------------------------------------------------------------
//  Contrast validation (WCAG 2.2)
// -----------------------------------------------------------------------------
function luminance(hex) {
  const value = hex.replace('#', '');
  const full =
    value.length === 3 ? value.split('').map((c) => c + c).join('') : value;

  const channels = [0, 2, 4].map((i) => {
    const srgb = parseInt(full.slice(i, i + 2), 16) / 255;
    // The sRGB -> linear transfer function from the WCAG definition.
    return srgb <= 0.04045 ? srgb / 12.92 : ((srgb + 0.055) / 1.055) ** 2.4;
  });

  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
}

function contrast(foreground, background) {
  const a = luminance(foreground);
  const b = luminance(background);
  const [lighter, darker] = a > b ? [a, b] : [b, a];
  return (lighter + 0.05) / (darker + 0.05);
}

// Pairs that actually appear as text on a background in the UI. Adding a new
// text/background combination to a component means adding it here too -
// otherwise it is unverified.
const CONTRAST_PAIRS = [
  ['color-text-primary', 'color-surface-base', 4.5, 'body text'],
  ['color-text-primary', 'color-surface-raised', 4.5, 'body text on a card'],
  ['color-text-secondary', 'color-surface-base', 4.5, 'secondary text'],
  ['color-text-muted', 'color-surface-base', 4.5, 'muted text'],
  ['color-text-on-brand', 'color-brand-500', 4.5, 'text on a primary button'],
  ['color-status-success-fg', 'color-status-success-bg', 4.5, 'success message'],
  ['color-status-warning-fg', 'color-status-warning-bg', 4.5, 'warning message'],
  ['color-status-danger-fg', 'color-status-danger-bg', 4.5, 'error message'],
  ['color-status-info-fg', 'color-status-info-bg', 4.5, 'info message'],
  // 3:1 is the AA threshold for non-text UI boundaries and focus indicators.
  ['color-border-strong', 'color-surface-base', 3, 'input border'],
  ['color-border-focus', 'color-surface-base', 3, 'focus ring'],
];

const failures = [];

for (const theme of ['light', 'dark']) {
  for (const [fg, bg, required, description] of CONTRAST_PAIRS) {
    const foreground = flat[theme][fg];
    const background = flat[theme][bg];

    if (!foreground || !background) {
      failures.push(`${theme}: missing token in pair ${fg} / ${bg}`);
      continue;
    }

    const ratio = contrast(foreground, background);
    if (ratio < required) {
      failures.push(
        `${theme}: ${description} — ${fg} (${foreground}) on ${bg} (${background}) ` +
          `is ${ratio.toFixed(2)}:1, needs ${required}:1`
      );
    }
  }
}

if (failures.length > 0) {
  console.error('\n✖ Design tokens fail WCAG 2.2 AA contrast:\n');
  for (const failure of failures) console.error(`  • ${failure}`);
  console.error('\nFix the values in tokens.json. Do not lower the threshold.\n');
  process.exit(1);
}

// -----------------------------------------------------------------------------
//  Emit
// -----------------------------------------------------------------------------
const dist = resolve(here, 'dist');
mkdirSync(dist, { recursive: true });

const banner = `/* GENERATED by web/design-tokens/build.mjs — do not edit.
   Source of truth: web/design-tokens/tokens.json */\n\n`;

// --- CSS -------------------------------------------------------------------
// Dark mode is applied two ways on purpose: prefers-color-scheme honours the
// operating system by default, and [data-theme] lets an explicit user choice
// override it. Supporting only the media query means the in-app theme switch
// cannot work; supporting only the attribute ignores the OS setting.
const cssVars = (map, indent = '  ') =>
  Object.entries(map)
    .map(([name, value]) => `${indent}--${name}: ${value};`)
    .join('\n');

const css = `${banner}:root {
${cssVars(flat.light)}
  color-scheme: light;
}

@media (prefers-color-scheme: dark) {
  :root:not([data-theme='light']) {
${cssVars(flat.dark, '    ')}
    color-scheme: dark;
  }
}

:root[data-theme='dark'] {
${cssVars(flat.dark)}
  color-scheme: dark;
}

:root[data-theme='light'] {
${cssVars(flat.light)}
  color-scheme: light;
}
`;

writeFileSync(resolve(dist, 'tokens.css'), css);

// --- TypeScript ------------------------------------------------------------
const tsName = (name) => name.replace(/-([a-z0-9])/g, (_, c) => c.toUpperCase());

const tsEntries = (map) =>
  Object.entries(map)
    .map(([name, value]) => `  ${tsName(name)}: '${value}',`)
    .join('\n');

const ts = `${banner}export const lightTokens = {
${tsEntries(flat.light)}
} as const;

export const darkTokens = {
${tsEntries(flat.dark)}
} as const;

export type TokenName = keyof typeof lightTokens;

/** Reference a token as a CSS variable, so the live theme applies. */
export const cssVar = (name: TokenName): string =>
  \`var(--\${String(name).replace(/[A-Z0-9]/g, (c) => '-' + c.toLowerCase())})\`;
`;

writeFileSync(resolve(dist, 'tokens.ts'), ts);

// --- React Native ----------------------------------------------------------
// React Native has no CSS variables and no cascade, so the theme cannot be
// swapped by changing a root attribute. Both palettes are exported as plain
// objects and the app picks one at runtime from useColorScheme().
const native = `${banner}export const lightTheme = {
${tsEntries(flat.light)}
} as const;

export const darkTheme = {
${tsEntries(flat.dark)}
} as const;

export type Theme = typeof lightTheme;
`;

writeFileSync(resolve(dist, 'tokens.native.ts'), native);

const count = Object.keys(flat.light).length;
console.log(
  `✔ ${count} tokens → tokens.css, tokens.ts, tokens.native.ts ` +
    `(${CONTRAST_PAIRS.length * 2} contrast checks passed)`
);
