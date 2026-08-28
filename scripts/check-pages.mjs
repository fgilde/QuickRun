// Parses the script in every page QuickRun ships, and fails if one of them will not parse.
//
// A syntax error in a page is invisible until somebody opens it: the browser stops at the bad token,
// every listener after it is never attached, and what the user sees is a window that draws and does
// nothing. That shipped twice - a stray newline inside a string literal, both times introduced by a
// tool rather than by hand - and neither the build nor any test noticed.
//
// Takes a second, needs no browser. The browser-driven harnesses catch what this cannot: exceptions
// while the page runs. This catches what they are too slow to run on every save.
//
//   node scripts/check-pages.mjs

import { readFileSync, existsSync } from 'node:fs';

/** The pages, and the placeholders the server fills in before a browser ever sees them. */
const PAGES = [
  ['src/QuickRun.App/Daemon/dashboard.html', { '{{TOKEN}}': 'token', '{{PORT}}': '9876', '{{VERSION}}': '0.0.0' }],
  ['extension/src/confirm.html', {}],
  ['extension/src/popup.html', {}],
  ['extension/src/options.html', {}],
];

let broken = 0;

for (const [path, placeholders] of PAGES) {
  if (!existsSync(path)) {
    console.error(`missing  ${path}`);
    broken++;
    continue;
  }

  let html = readFileSync(path, 'utf8');
  for (const [from, to] of Object.entries(placeholders)) html = html.split(from).join(to);

  const blocks = [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/g)]
    .filter(([, attributes]) => !/\bsrc=/.test(attributes));

  if (blocks.length === 0) {
    console.log(`ok       ${path} (no inline script)`);
    continue;
  }

  let failed = false;

  for (const [, attributes, code] of blocks) {
    try {
      // A module may use import/export, which Function() cannot take; those pages are covered by
      // the harness that loads them for real.
      if (/type\s*=\s*["']module["']/.test(attributes)) continue;
      new Function(code);
    } catch (error) {
      console.error(`BROKEN   ${path}: ${error.message}`);
      failed = true;
    }
  }

  if (failed) broken++;
  else console.log(`ok       ${path}`);
}

process.exit(broken === 0 ? 0 : 1);
