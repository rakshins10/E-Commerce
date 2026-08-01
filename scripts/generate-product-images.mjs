#!/usr/bin/env node
/**
 * Generates the product artwork in `web/shared-assets/img`.
 *
 * ---
 * **Why generated rather than drawn.** Twelve images that must share a frame, a palette and a light
 * source are twelve chances to drift. A generator makes the shared parts literally shared, so adding a
 * thirteenth product is one entry in a table rather than an exercise in matching what came before.
 *
 * **Why SVG.** No binary blobs in git — these diff, review and scale like the Mermaid diagrams do. They
 * are also a few hundred bytes each, so the storefront ships its whole catalogue of imagery for less
 * than one photograph.
 *
 * **Why the images are deliberately illustrative.** Stock photography would look more "real" and would
 * be a licensing problem, a download, and a thing that cannot be regenerated. Flat illustration reads as
 * intentional rather than as a missing asset, and it works in both light and dark themes because the
 * frame carries its own background.
 *
 * Run: `node scripts/generate-product-images.mjs`
 */

import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const outDir = join(root, 'web', 'shared-assets', 'img');

/**
 * The frame every product shares.
 *
 * A soft vertical wash rather than a flat fill, so a grid of cards has some depth without any of them
 * competing with the product name beneath. The palette is per-category, which gives the grid a rhythm:
 * clothing reads warm, drinkware cool, stationery neutral.
 */
const PALETTES = {
  clothing: { bg1: '#eef2ff', bg2: '#e0e7ff', ink: '#4338ca', accent: '#818cf8', light: '#c7d2fe' },
  drinkware: { bg1: '#ecfeff', bg2: '#cffafe', ink: '#0e7490', accent: '#22d3ee', light: '#a5f3fc' },
  stationery: { bg1: '#fef3c7', bg2: '#fde68a', ink: '#92400e', accent: '#f59e0b', light: '#fcd34d' },
};

/** Wraps a product's shapes in the shared frame. */
function frame(id, palette, body) {
  const p = PALETTES[palette];

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 400" role="img" aria-label="Product illustration">
  <defs>
    <linearGradient id="bg-${id}" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="${p.bg1}"/>
      <stop offset="100%" stop-color="${p.bg2}"/>
    </linearGradient>
  </defs>
  <rect width="400" height="400" fill="url(#bg-${id})"/>
${body(p)}
</svg>
`;
}

/** A t-shirt body. `sleeves` is how far the sleeves extend. */
const tshirt = (sleeveLength, extra = () => '') => (p) => `  <path d="M140 110 L110 130 L${110 - sleeveLength} ${175 + sleeveLength * 0.4} L128 ${196 + sleeveLength * 0.4} L140 178 L140 300 L260 300 L260 178 L272 ${196 + sleeveLength * 0.4} L${290 + sleeveLength} ${175 + sleeveLength * 0.4} L290 130 L260 110 L230 110 Q200 132 170 110 Z" fill="${p.ink}"/>
  <path d="M170 110 Q200 132 230 110 L222 104 Q200 122 178 104 Z" fill="${p.light}"/>
${extra(p)}`;

/** A hoodie: t-shirt plus a hood, a pocket and optionally a zip. */
const hoodie = (withZip) => (p) => `  <path d="M136 122 L104 144 L84 196 L106 214 L136 190 L136 306 L264 306 L264 190 L294 214 L316 196 L296 144 L264 122 L236 116 Q200 140 164 116 Z" fill="${p.ink}"/>
  <path d="M164 116 Q200 148 236 116 Q228 96 200 96 Q172 96 164 116 Z" fill="${p.accent}"/>
  <path d="M160 240 L240 240 L232 282 L168 282 Z" fill="${p.accent}" opacity="0.55"/>
  <circle cx="182" cy="128" r="4" fill="${p.light}"/>
  <circle cx="218" cy="128" r="4" fill="${p.light}"/>
  <path d="M182 128 L186 168" stroke="${p.light}" stroke-width="4" stroke-linecap="round"/>
  <path d="M218 128 L214 168" stroke="${p.light}" stroke-width="4" stroke-linecap="round"/>
${withZip ? `  <rect x="196" y="132" width="8" height="174" fill="${p.light}"/>` : ''}`;

const PRODUCTS = [
  // --- Clothing -------------------------------------------------------------------------------
  { file: 'tshirt-classic.svg', palette: 'clothing', body: tshirt(26) },
  {
    file: 'tshirt-long.svg',
    palette: 'clothing',
    body: tshirt(64),
  },
  {
    file: 'tshirt-graphic.svg',
    palette: 'clothing',
    // The "print" is a simple mark, so the graphic tee is distinguishable at thumbnail size.
    body: tshirt(26, (p) => `  <circle cx="200" cy="222" r="34" fill="none" stroke="${p.light}" stroke-width="7"/>
  <path d="M182 222 L196 236 L220 208" fill="none" stroke="${p.light}" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/>`),
  },
  { file: 'hoodie-pullover.svg', palette: 'clothing', body: hoodie(false) },
  { file: 'hoodie-zip.svg', palette: 'clothing', body: hoodie(true) },
  {
    file: 'hoodie-heavy.svg',
    palette: 'clothing',
    // Heavier: a thicker cuff and hem, so "450gsm" reads visually.
    body: (p) => `${hoodie(false)(p)}
  <rect x="136" y="288" width="128" height="18" fill="${p.accent}" opacity="0.8"/>`,
  },

  // --- Drinkware ------------------------------------------------------------------------------
  {
    file: 'mug-enamel.svg',
    palette: 'drinkware',
    body: (p) => `  <path d="M132 150 L268 150 L256 300 Q254 316 238 316 L162 316 Q146 316 144 300 Z" fill="${p.ink}"/>
  <rect x="126" y="140" width="148" height="18" rx="9" fill="${p.light}"/>
  <path d="M268 186 Q318 186 318 226 Q318 266 268 266" fill="none" stroke="${p.ink}" stroke-width="18" stroke-linecap="round"/>
  <ellipse cx="200" cy="176" rx="52" ry="12" fill="${p.accent}" opacity="0.45"/>`,
  },
  {
    file: 'bottle-insulated.svg',
    palette: 'drinkware',
    body: (p) => `  <rect x="172" y="92" width="56" height="34" rx="8" fill="${p.accent}"/>
  <path d="M164 126 Q160 150 160 176 L160 300 Q160 320 180 320 L220 320 Q240 320 240 300 L240 176 Q240 150 236 126 Z" fill="${p.ink}"/>
  <rect x="176" y="196" width="14" height="86" rx="7" fill="${p.light}" opacity="0.6"/>
  <rect x="160" y="232" width="80" height="26" fill="${p.accent}" opacity="0.5"/>`,
  },
  {
    file: 'mug-ceramic.svg',
    palette: 'drinkware',
    body: (p) => `  <path d="M138 156 L262 156 L262 296 Q262 314 244 314 L156 314 Q138 314 138 296 Z" fill="${p.ink}"/>
  <path d="M262 194 Q312 194 312 232 Q312 270 262 270" fill="none" stroke="${p.ink}" stroke-width="20" stroke-linecap="round"/>
  <ellipse cx="200" cy="156" rx="62" ry="16" fill="${p.light}"/>
  <ellipse cx="200" cy="158" rx="46" ry="10" fill="${p.accent}" opacity="0.5"/>`,
  },

  // --- Stationery -----------------------------------------------------------------------------
  {
    file: 'notebook-dot.svg',
    palette: 'stationery',
    body: (p) => {
      const dots = [];
      for (let x = 168; x <= 256; x += 22) {
        for (let y = 148; y <= 280; y += 22) {
          dots.push(`  <circle cx="${x}" cy="${y}" r="3" fill="${p.accent}" opacity="0.7"/>`);
        }
      }
      return `  <rect x="128" y="112" width="150" height="196" rx="8" fill="${p.ink}"/>
  <rect x="146" y="120" width="126" height="180" rx="4" fill="#fffdf7"/>
${dots.join('\n')}
  <rect x="128" y="112" width="18" height="196" rx="8" fill="${p.accent}"/>`;
    },
  },
  {
    file: 'pens-fineliner.svg',
    palette: 'stationery',
    body: (p) => {
      const colours = [p.ink, p.accent, '#0e7490', '#4338ca', '#be123c', '#15803d'];
      return colours
        .map((colour, index) => {
          const x = 118 + index * 32;
          return `  <rect x="${x}" y="140" width="18" height="150" rx="9" fill="${colour}"/>
  <path d="M${x} 140 L${x + 9} 112 L${x + 18} 140 Z" fill="${colour}"/>
  <rect x="${x}" y="250" width="18" height="14" fill="#ffffff" opacity="0.6"/>`;
        })
        .join('\n');
    },
  },
  {
    file: 'portfolio-leather.svg',
    palette: 'stationery',
    body: (p) => `  <rect x="112" y="118" width="176" height="184" rx="10" fill="${p.ink}"/>
  <rect x="124" y="130" width="152" height="160" rx="6" fill="none" stroke="${p.light}" stroke-width="3" opacity="0.8"/>
  <rect x="180" y="102" width="40" height="30" rx="6" fill="${p.accent}"/>
  <circle cx="200" cy="210" r="20" fill="none" stroke="${p.accent}" stroke-width="5"/>
  <path d="M112 208 L288 208" stroke="${p.light}" stroke-width="3" opacity="0.5"/>`,
  },
];

mkdirSync(outDir, { recursive: true });

for (const [index, product] of PRODUCTS.entries()) {
  writeFileSync(join(outDir, product.file), frame(index, product.palette, product.body), 'utf8');
}

/**
 * A fallback for anything without artwork — a product created through the admin panel, for instance.
 *
 * Every image element needs one: a broken image icon in a shop reads as a broken shop.
 */
writeFileSync(
  join(outDir, 'placeholder.svg'),
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 400" role="img" aria-label="No image available">
  <rect width="400" height="400" fill="#f1f5f9"/>
  <rect x="120" y="140" width="160" height="120" rx="10" fill="none" stroke="#94a3b8" stroke-width="6"/>
  <circle cx="164" cy="180" r="14" fill="#94a3b8"/>
  <path d="M132 250 L186 198 L226 238 L252 216 L268 250 Z" fill="#94a3b8"/>
</svg>
`,
  'utf8',
);

console.log(`Wrote ${PRODUCTS.length + 1} images to web/shared-assets/img`);
