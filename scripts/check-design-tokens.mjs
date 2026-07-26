// =============================================================================
//  Design-token guard
// =============================================================================
//  Each frontend owns its own tokens.css (docs/adr/0018-self-contained-frontends.md),
//  which removes the structural guarantee that they look the same. This script
//  restores it as a *check* rather than as coupling:
//
//    1. every colour pair used as text-on-background meets WCAG 2.2 AA
//    2. every app's token file is IDENTICAL, so the apps cannot drift visually
//
//  Visual drift is the kind least likely to be caught by an end-to-end test and
//  most likely to be noticed by a user, so it is worth a dedicated check.
//
//  Run:  node scripts/check-design-tokens.mjs
// =============================================================================

import { readFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');

const TOKEN_FILES = [
  'web/react-store/src/styles/tokens.css',
  'web/angular-store/src/styles/tokens.css',
];

// Pairs that actually appear as text on a background, with the AA threshold.
// 4.5:1 for body text; 3:1 for large text, UI boundaries and focus indicators.
const CONTRAST_PAIRS = [
  ['--color-text-primary', '--color-surface-base', 4.5, 'body text'],
  ['--color-text-primary', '--color-surface-raised', 4.5, 'text on a card'],
  ['--color-text-secondary', '--color-surface-base', 4.5, 'secondary text'],
  ['--color-text-muted', '--color-surface-base', 4.5, 'muted text'],
  ['--color-text-on-brand', '--color-brand-500', 4.5, 'text on a primary button'],
  ['--color-status-success-fg', '--color-status-success-bg', 4.5, 'success message'],
  ['--color-status-warning-fg', '--color-status-warning-bg', 4.5, 'warning message'],
  ['--color-status-danger-fg', '--color-status-danger-bg', 4.5, 'error message'],
  ['--color-status-info-fg', '--color-status-info-bg', 4.5, 'info message'],
  ['--color-border-strong', '--color-surface-base', 3, 'input border'],
  ['--color-border-focus', '--color-surface-base', 3, 'focus ring'],
];

// --- WCAG relative luminance and contrast ------------------------------------
function luminance(hex) {
  const value = hex.replace('#', '').trim();
  const full = value.length === 3 ? value.split('').map((c) => c + c).join('') : value;

  const channels = [0, 2, 4].map((i) => {
    const srgb = parseInt(full.slice(i, i + 2), 16) / 255;
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

/**
 * Extracts custom properties per theme block.
 *
 * The light palette is in `:root {}` and the dark one in `:root[data-theme='dark'] {}`.
 * Both are checked: a palette that passes in light mode and fails in dark is a
 * very common and completely invisible regression.
 */
function parseThemes(css) {
  const themes = { light: {}, dark: {} };

  const lightBlock = css.match(/:root\s*\{([\s\S]*?)\}/);
  const darkBlock = css.match(/:root\[data-theme='dark'\]\s*\{([\s\S]*?)\}/);

  for (const [theme, block] of [
    ['light', lightBlock],
    ['dark', darkBlock],
  ]) {
    if (!block) continue;
    for (const [, name, value] of block[1].matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) {
      themes[theme][name] = value.trim();
    }
  }

  return themes;
}

// --- Run ----------------------------------------------------------------------
const failures = [];
const contents = new Map();

for (const file of TOKEN_FILES) {
  const path = resolve(root, file);

  if (!existsSync(path)) {
    failures.push(`missing token file: ${file}`);
    continue;
  }

  const css = readFileSync(path, 'utf8');
  contents.set(file, css);

  const themes = parseThemes(css);

  for (const theme of ['light', 'dark']) {
    for (const [fg, bg, required, description] of CONTRAST_PAIRS) {
      const foreground = themes[theme][fg];
      const background = themes[theme][bg];

      if (!foreground || !background) {
        failures.push(`${file} [${theme}]: missing token in pair ${fg} / ${bg}`);
        continue;
      }

      const ratio = contrast(foreground, background);
      if (ratio < required) {
        failures.push(
          `${file} [${theme}]: ${description} — ${fg} (${foreground}) on ${bg} (${background}) ` +
            `is ${ratio.toFixed(2)}:1, needs ${required}:1`,
        );
      }
    }
  }
}

// Drift check: the apps are allowed to differ in every other respect, but their
// visual vocabulary must be identical or they stop being the same product.
const [first, ...rest] = TOKEN_FILES;
for (const other of rest) {
  if (contents.has(first) && contents.has(other) && contents.get(first) !== contents.get(other)) {
    failures.push(
      `token drift: ${other} differs from ${first}. ` +
        `Both frontends must ship the same palette — copy one over the other.`,
    );
  }
}

if (failures.length > 0) {
  console.error('\n✖ Design token check failed:\n');
  for (const failure of failures) console.error(`  • ${failure}`);
  console.error('\nDo not lower the thresholds.\n');
  process.exit(1);
}

console.log(
  `✔ Design tokens OK — ${TOKEN_FILES.length} files, ` +
    `${CONTRAST_PAIRS.length * 2} contrast checks each, palettes identical`,
);
