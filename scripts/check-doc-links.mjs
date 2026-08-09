#!/usr/bin/env node
/**
 * Fails if a relative link in any Markdown file points at something that does not exist.
 *
 * ---
 * **Why this exists.** Documentation ships in the same PR as the code it describes, which means links get
 * written against a tree that is still moving. Five were already broken when this was first run: two ADRs
 * referenced by a shortened filename, one pointing at a page that was never written, one at
 * `web/shared/` - a directory ADR-0018 deliberately deleted - and one at the wrong ADR number entirely.
 *
 * None of them would fail a build. All of them make a reader distrust the rest of the page.
 *
 * ---
 * **Scope: relative links only.** External URLs are not checked - that needs the network, makes the build
 * flaky, and fails for reasons nobody in this repository can fix. Anchors are not checked either: the file
 * is verified to exist, not the heading within it.
 *
 * Run: `node scripts/check-doc-links.mjs`
 */
import { execFileSync } from 'node:child_process';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { dirname, join, normalize } from 'node:path';

const files = execFileSync('git', ['ls-files', '-z', '*.md'], { encoding: 'utf8' })
  .split('\0')
  .filter(Boolean);

const broken = [];

for (const file of files) {
  const text = readFileSync(file, 'utf8');
  const lines = text.split('\n');

  lines.forEach((line, index) => {
    // [label](target) - relative targets only.
    for (const match of line.matchAll(/\]\(([^)\s]+?)(?:\s+"[^"]*")?\)/g)) {
      let target = match[1];

      if (/^(https?:|mailto:|#)/.test(target)) continue;

      // Strip an anchor; we check the file exists, not the heading.
      const anchorAt = target.indexOf('#');
      if (anchorAt === 0) continue;
      if (anchorAt > 0) target = target.slice(0, anchorAt);
      if (!target) continue;

      const resolved = normalize(join(dirname(file), decodeURIComponent(target)));

      if (!existsSync(resolved)) {
        broken.push({ file, line: index + 1, target: match[1] });
        continue;
      }

      // A link to a directory is fine only if it really is one.
      if (target.endsWith('/') && !statSync(resolved).isDirectory()) {
        broken.push({ file, line: index + 1, target: match[1] });
      }
    }
  });
}

if (broken.length > 0) {
  console.error(`${broken.length} broken relative link(s):\n`);
  for (const b of broken) console.error(`  ${b.file}:${b.line}  ->  ${b.target}`);
  process.exit(1);
}

console.log(`OK - every relative link in ${files.length} markdown files resolves`);
