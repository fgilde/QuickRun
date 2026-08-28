// Guards against a class of bug that is invisible in review and obvious to every visitor.
//
// ":global(html.dark) .x" inside a scoped <style> block compiles to "html.dark { … }" - the
// descendant part is dropped and the declaration lands on the whole document. That shipped once,
// as a page-wide filter: invert(1) that turned dark mode inside out.

import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const assets = join(import.meta.dirname, '.vitepress', 'dist', 'assets');

/** A rule whose selector is only html/body, with no descendant to narrow it. */
const PAGE_WIDE = /(?:^|[};])\s*(html|body)((?:\.[\w-]+)|(?:\[[^\]]*\])|:[\w-]+)*\s*\{([^}]*)\}/g;

/** Declarations that repaint or move everything when applied to the document itself. */
const DANGEROUS = ['filter:', 'transform:', 'mix-blend-mode:', 'opacity:'];

const problems = [];

// ---- the browser check that decides which install button is offered --------------------------
//
// Edge and Opera both say "Chrome" and add a token of their own, so the order these are ruled out in
// is the whole of it - and offering an Edge user the Chrome listing is a dead end they cannot use.
const { currentBrowser, listingFor, STORES } = await import('./.vitepress/theme/stores.js');

const AGENTS = {
  edge: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) '
    + 'Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0',
  chrome: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) '
    + 'Chrome/128.0.0.0 Safari/537.36',
  opera: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) '
    + 'Chrome/128.0.0.0 Safari/537.36 OPR/114.0.0.0',
  firefox: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:129.0) Gecko/20100101 Firefox/129.0',
};

for (const [expected, agent] of Object.entries(AGENTS)) {
  const seen = currentBrowser(agent);
  if (seen !== expected) problems.push(`browser check: ${expected} was read as ${seen}`);
}

// Safari takes no extension of this kind; saying nothing beats guessing.
const safari = currentBrowser('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 '
  + '(KHTML, like Gecko) Version/17.0 Safari/605.1.15');
if (safari !== null) problems.push(`browser check: Safari was read as ${safari}`);

// Opera installs from the Chrome listing, so it has one exactly when Chrome does.
if (listingFor('opera') !== STORES.chrome)
  problems.push('Opera is not pointed at the Chrome listing');

for (const file of readdirSync(assets).filter((f) => f.endsWith('.css'))) {
  const css = readFileSync(join(assets, file), 'utf8');

  for (const match of css.matchAll(PAGE_WIDE)) {
    const [, element, , body] = match;
    const found = DANGEROUS.filter((property) => body.includes(property));
    if (found.length === 0) continue;

    problems.push(`${file}: ${element} { ${found.join(' ')} } — applies to the whole document`);
  }
}

if (problems.length > 0) {
  console.error('The built site has problems:\n');
  for (const problem of problems) console.error(`  ${problem}`);

  // The advice belongs to the CSS findings and nowhere else - printed under a browser check it
  // sends whoever reads it looking in the wrong file.
  if (problems.some((problem) => problem.includes('applies to the whole document'))) {
    console.error('\nA scoped style block must not target html or body. If a rule needs an ancestor,');
    console.error('write "html.dark .thing { … }" rather than ":global(html.dark) .thing { … }".');
  }

  process.exit(1);
}

console.log('built CSS carries no page-wide filter, transform, blend or opacity rule');
console.log('every browser is read as itself, and Opera installs from the Chrome listing');
