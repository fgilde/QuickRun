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
  // Every agent above also says "Safari", which is why Safari can only be read after all of them
  // have been ruled out. Getting that order wrong reads Chrome as Safari and offers a Chrome user a
  // download that will not load in their browser.
  safari: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 '
    + '(KHTML, like Gecko) Version/17.0 Safari/605.1.15',
};

for (const [expected, agent] of Object.entries(AGENTS)) {
  const seen = currentBrowser(agent);
  if (seen !== expected) problems.push(`browser check: ${expected} was read as ${seen}`);

}

// Safari has a build but no store listing, and cannot have one until the extension ships inside a
// signed app. Its card offers the download instead - and a listing that appeared here by accident
// would turn that card into a link to a page that does not exist.
if (STORES.safari !== null)
  problems.push('Safari has a store listing now - its card needs checking, it offers a download');

// Something this page has never heard of gets no button rather than a wrong one.
const unknown = currentBrowser('Mozilla/5.0 (X11; CrOS x86_64) SomeBrowser/1.0');
if (unknown !== null) problems.push(`browser check: an unknown browser was read as ${unknown}`);

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
// ---- what a page may do with a local QuickRun ---------------------------------------------------
//
// The rule the daemon enforces is that a web page may not drive it. What this side has to get right
// is the other half: where a target is handed over, and that a page never invents one of its own
// shape. Both are pure functions, so they are checked here rather than in a browser.
const { carry, targetFor, DEFAULT_PORT } = await import('./.vitepress/theme/local.js');

if (carry({ repo: 'acme/app' }) !== 'repo=acme%2Fapp')
  problems.push(`a repository is not carried as expected: ${carry({ repo: 'acme/app' })}`);

if (carry({ repo: 'acme/app', ref: 'feature/x' }) !== 'repo=acme%2Fapp&ref=feature%2Fx')
  problems.push('a ref with a slash is not escaped');

const asFile = carry({ file: 'C:\dev\demo.yml' });
if (asFile !== 'file=C%3A%5Cdev%5Cdemo.yml')
  problems.push(`a config file is not carried as expected: ${asFile}`);

// A file and a repository are never carried together: a config file names its own repository, and
// two answers to the same question is how the wrong one gets used.
if (carry({ repo: 'acme/app', file: 'demo.yml' }).includes('repo='))
  problems.push('a file and a repository were carried together');

const localTarget = targetFor(true, { repo: 'acme/app' });
if (!localTarget.startsWith(`http://127.0.0.1:${DEFAULT_PORT}/#run?`))
  problems.push(`a running QuickRun is not handed the local page: ${localTarget}`);

const schemeTarget = targetFor(false, { repo: 'acme/app' });
if (!schemeTarget.startsWith('quickrun://run?'))
  problems.push(`without QuickRun the scheme is not used: ${schemeTarget}`);

// Loopback and nothing else: a target pointed anywhere else would be a page reaching a machine that
// is not the reader's.
for (const target of [localTarget, schemeTarget])
  if (/^https?:\/\//.test(target) && !target.includes('127.0.0.1'))
    problems.push(`a target points somewhere other than loopback: ${target}`);

console.log('a page hands over to loopback only, and carries what it is allowed to');

console.log('every browser is read as itself, and Opera installs from the Chrome listing');
