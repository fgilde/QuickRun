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
  console.error('Page-wide styles found in the built CSS:\n');
  for (const problem of problems) console.error(`  ${problem}`);
  console.error('\nA scoped style block must not target html or body. If a rule needs an ancestor,');
  console.error('write "html.dark .thing { … }" rather than ":global(html.dark) .thing { … }".');
  process.exit(1);
}

console.log('built CSS carries no page-wide filter, transform, blend or opacity rule');
