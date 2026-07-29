#!/usr/bin/env node
/**
 * Copies `web/shared-assets/img` into each application's static directory.
 *
 * ---
 * **Why a copy rather than a shared import.** [ADR-0018](../../docs/adr/0018-self-contained-frontends.md)
 * says each app owns its own code, and that rule has earned its keep — but it is about *logic*, not
 * about content. Four checked-in copies of the same thirteen SVGs would drift the first time somebody
 * corrected one, and nothing would catch it.
 *
 * So the images live in **one** place in git and are copied at build time. The copies are gitignored,
 * which is the point: there is exactly one version of an image to change.
 *
 * **Why a script rather than each framework's own mechanism.** Angular can point `assets` at a path
 * outside the project; Vite's `publicDir` is a single directory already in use for favicons. Using each
 * framework's native approach would mean two different answers to one question, and a reader would have
 * to learn both to know where an image comes from. One script, one answer.
 *
 * Runs automatically before `dev` and `build` in every app.
 */

import { cpSync, existsSync, mkdirSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const web = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = join(web, 'shared-assets', 'img');

/**
 * Where each app serves static files from.
 *
 * React (Vite) uses `public/`; Angular uses `public/` too since v18, so the shape happens to match.
 */
const TARGETS = [
  join(web, 'react-store', 'public', 'img'),
  join(web, 'angular-store', 'public', 'img'),
  join(web, 'react-admin', 'public', 'img'),
  join(web, 'angular-admin', 'public', 'img'),
];

if (!existsSync(source)) {
  console.error(
    `Missing ${source}. Run \`node scripts/generate-product-images.mjs\` from the repository root.`,
  );
  process.exit(1);
}

for (const target of TARGETS) {
  // Removed first, so an image deleted from the source does not linger in a copy. A stale asset that
  // only exists in one app is exactly the drift this script is meant to prevent.
  rmSync(target, { recursive: true, force: true });
  mkdirSync(dirname(target), { recursive: true });
  cpSync(source, target, { recursive: true });
}

console.log(`Synced product images into ${TARGETS.length} applications.`);
