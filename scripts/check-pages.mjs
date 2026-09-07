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

import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs';

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

/*
 * And the addresses the app hands out.
 *
 * A run with no config ends in one line and one link, and that link was
 * https://quickrun.org/docs/config - which has never been a page. Nobody notices, because the one
 * person who follows it is the one person already stuck. Checked against the site in this
 * repository rather than over the network: it is the same commit that publishes both.
 */
const SEARCH = ['src', 'extension/src', 'README.md'];

/** Every file worth reading for links: source, pages, docs. */
function files(where) {
  if (statSync(where).isFile()) return [where];

  const found = [];

  for (const entry of readdirSync(where, { withFileTypes: true })) {
    const path = `${where}/${entry.name}`;

    if (entry.isDirectory()) {
      if (['bin', 'obj', 'node_modules', 'dist', 'monaco'].includes(entry.name)) continue;
      found.push(...files(path));
      continue;
    }

    if (/\.(cs|js|mjs|html|css|md)$/.test(entry.name)) found.push(path);
  }

  return found;
}

/**
 * Addresses the pages workflow puts on the site rather than files committed here.
 *
 * Listed rather than looked for: they are not in site/public until that workflow copies them in, so
 * looking would fail on a clean checkout - which is exactly what CI is. The value says where each
 * one comes from, so a rename breaks the note instead of quietly leaving a dead address.
 */
const BUILT = {
  'quickrun.schema.json': 'schema/quickrun.schema.json',
  configs: 'configs',
};

/** Whether the site in this repository serves that path. */
function served(path) {
  const clean = path.replace(/^\/+|\/+$/g, '');

  if (clean === '') return true;

  // A page, in either language, or a file under public/ - which is how badge.svg and the logos
  // are served.
  if (existsSync(`site/${clean}.md`) || existsSync(`site/${clean}/index.md`)
      || existsSync(`site/public/${clean}`)) return true;

  // Something the pages workflow copies in, checked at its source in this repository.
  const [first, ...rest] = clean.split('/');
  if (BUILT[first]) return existsSync([BUILT[first], ...rest].join('/'));

  return false;
}

const linked = new Map();

for (const where of SEARCH) {
  if (!existsSync(where)) continue;

  for (const file of files(where)) {
    const text = readFileSync(file, 'utf8');

    for (const [, path] of text.matchAll(/https:\/\/quickrun\.org\/([A-Za-z0-9./_-]*)/g)) {
      // A trailing dot or slash is punctuation in a sentence, not part of the address.
      const clean = path.replace(/[.]$/, '');
      if (!linked.has(clean)) linked.set(clean, file);
    }
  }
}

let dead = 0;

for (const [path, file] of [...linked].sort()) {
  if (served(path)) continue;

  console.error(`DEAD     https://quickrun.org/${path} (in ${file})`);
  dead++;
}

console.log(dead === 0
  ? `ok       ${linked.size} addresses on quickrun.org, all of them pages this site has`
  : `${dead} of ${linked.size} addresses do not exist`);

process.exit(broken === 0 && dead === 0 ? 0 : 1);
