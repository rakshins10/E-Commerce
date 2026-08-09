#!/usr/bin/env node
/**
 * Fails if typographic punctuation has crept back into the repository.
 *
 * ---
 * **Why this exists.** Em dashes, en dashes and curly quotes were scattered through 253 files - source,
 * comments, documentation and user-visible strings alike. They render inconsistently across terminals,
 * editors and diff tools, they are awkward to type, and a codebase where they appear uniformly in prose
 * reads as machine-written rather than maintained.
 *
 * A one-off cleanup only holds until the next edit. This is the part that makes it stick.
 *
 * ---
 * **What is deliberately allowed.**
 *
 * This is not a ban on non-ASCII - that would be wrong. The rule targets punctuation that has a plain
 * ASCII equivalent doing the same job:
 *
 * - `£`, `€`, `©` and the rest carry meaning ASCII cannot express. Allowed.
 * - `…` is the conventional UI ellipsis ("Loading…"), specified by every major platform's interface
 *   guidelines and read correctly by screen readers, where "..." is announced as three full stops.
 *   Allowed.
 * - `·` separates related facts on one line ("Northwind · SKU NW-TS-001"). A hyphen there would read as
 *   part of the SKU. Allowed.
 * - `×` is the multiplication sign in "Ceramic Mug × 2", not the letter x. Allowed.
 *
 * The line is drawn at *substitutes*: a dash that should be a hyphen, a quote that should be a straight
 * quote. Those are the ones with no reason to differ.
 *
 * Run: `node scripts/check-ascii-punctuation.mjs`
 */
import { execFileSync } from 'node:child_process';
import { readFileSync, statSync } from 'node:fs';

/**
 * Code point -> what to write instead.
 *
 * Written as code points rather than as the characters themselves, so this file passes its own check.
 * The alternative - exempting the checker from the rule - is a hole in the rule, and would make the one
 * file guaranteed to accumulate examples of exactly what it forbids.
 */
const BANNED = new Map([
  [0x2014, 'a hyphen: -'], // em dash
  [0x2013, 'a hyphen: -'], // en dash
  [0x2018, "a straight quote: '"], // left single quotation mark
  [0x2019, "a straight quote: '"], // right single quotation mark
  [0x201c, 'a straight quote: "'], // left double quotation mark
  [0x201d, 'a straight quote: "'], // right double quotation mark
  [0x00a0, 'a normal space'], // non-breaking space
]);

/** Binary and vendored files, which are not ours to police. */
const SKIP = /\.(png|jpe?g|gif|ico|svg|woff2?|ttf|eot|pdf|zip|webm)$|(^|\/)package-lock\.json$/i;

const files = execFileSync('git', ['ls-files', '-z'], {
  encoding: 'utf8',
  maxBuffer: 64 * 1024 * 1024,
})
  .split('\0')
  .filter(Boolean);

const problems = [];

for (const file of files) {
  if (SKIP.test(file)) continue;

  try {
    if (!statSync(file).isFile()) continue;
  } catch {
    continue; // indexed but deleted
  }

  const lines = readFileSync(file, 'utf8').split('\n');

  lines.forEach((line, index) => {
    for (const [codePoint, advice] of BANNED) {
      const column = line.indexOf(String.fromCodePoint(codePoint));

      if (column !== -1) {
        problems.push({ file, line: index + 1, column: column + 1, codePoint, advice });
        break; // one report per line is enough to find it
      }
    }
  });
}

if (problems.length > 0) {
  console.error(`✗ ${problems.length} line(s) use typographic punctuation where ASCII belongs:\n`);

  for (const problem of problems.slice(0, 40)) {
    const code = problem.codePoint.toString(16).toUpperCase().padStart(4, '0');
    console.error(
      `  ${problem.file}:${problem.line}:${problem.column}  U+${code} -> use ${problem.advice}`,
    );
  }

  if (problems.length > 40) {
    console.error(`  ... and ${problems.length - 40} more`);
  }

  console.error('\nSee the header of scripts/check-ascii-punctuation.mjs for what IS allowed.');
  process.exit(1);
}

console.log(`✔ ASCII punctuation OK - ${files.length} tracked files checked`);
