// Measures where the button actually lands on a live GitHub page, and what it costs the layout.
//
// This exists because two placement bugs shipped that no unit test could have caught: the button
// was injected into a container GitHub hides at desktop widths, and into a 70px grid column that
// it overflowed. Both are questions about rendered layout, so both need a real layout engine.
//
//   node tools/measure-placement.mjs https://github.com/microsoft/vscode/branches
//   node tools/measure-placement.mjs https://github.com/microsoft/vscode/pull/1
//
// Needs Chrome and network access, so it is a hand-run tool rather than part of CI.

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const url = process.argv[2];
if (!url) {
  console.error('usage: node tools/measure-placement.mjs <github url>');
  process.exit(2);
}

const CHROME = process.env.CHROME ?? [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  '/usr/bin/google-chrome',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
].find((path) => {
  try {
    readFileSync(path);
    return true;
  } catch {
    return false;
  }
});

if (!CHROME) {
  console.error('Chrome not found. Set CHROME to its path.');
  process.exit(2);
}

const work = mkdtempSync(join(tmpdir(), 'quickrun-placement-'));
const src = (name) => readFileSync(fileURLToPath(new URL(`../src/${name}`, import.meta.url)), 'utf8');

function chrome(target, profile) {
  return execFileSync(CHROME, [
    '--headless=new', '--disable-gpu', '--no-sandbox',
    `--user-data-dir=${join(work, profile)}`,
    '--virtual-time-budget=25000', '--window-size=1600,1000',
    '--dump-dom', target,
  ], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
}

// Render the page with its own scripts, so React has produced the real DOM.
const rendered = chrome(url, 'render');

// Then replay it with those scripts stripped: GitHub's bundle would re-render and replace
// everything the probe is about to measure. The stylesheets stay - "hidden at this width" is a
// CSS rule, and hiding is precisely what is being measured.
const fixture = rendered
  .replace(/<script\b[^>]*\bsrc=[^>]*>\s*<\/script>/gi, '')
  .replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '')
  .replace('</body>', `
<style>${src('quickrun.css')}</style>
<pre id="qr-result"></pre>
<script>${src('targets.js')}</script>
<script>${src('placement.js')}</script>
<script>
window.addEventListener('load', () => setTimeout(() => {
  const P = globalThis.QuickRunPlacement;
  const parsed = QuickRunTargets.parseLocation(${JSON.stringify(new URL(url).pathname)});

  const describe = (el) => el ? {
    tag: el.tagName,
    cls: String(el.className).slice(0, 60),
    visible: el.getClientRects().length > 0,
    width: Math.round(el.getBoundingClientRect().width),
  } : null;

  const wrappers = [...document.querySelectorAll('[class*="TableOverflowWrapper"]')];
  const overflow = () => wrappers.map((w) => w.scrollWidth - w.clientWidth);

  const result = { page: parsed?.kind ?? 'unknown', anchors: [] };

  if (parsed?.kind === 'branches') {
    const before = overflow();
    const rows = P.branchRows(parsed.repo);

    for (const row of rows) {
      const button = document.createElement('button');
      button.className = 'quickrun-button quickrun-button--compact';
      const icon = document.createElement('img');
      icon.className = 'quickrun-icon';
      button.append(icon);
      row.anchor.appendChild(button);
    }

    result.rows = rows.length;
    result.anchors = rows.slice(0, 1).map((r) => describe(r.anchor));
    result.overflowBefore = before;
    result.overflowAfter = overflow();
    result.widensTheTable = result.overflowAfter.some((a, i) => a > before[i] + 1);
  } else if (parsed?.kind === 'pull') {
    result.anchors = [describe(P.pullRequestActions())];
  } else {
    result.anchors = [describe(P.repoToolbar())];
  }

  document.getElementById('qr-result').textContent = 'QRPROBE' + JSON.stringify(result, null, 1) + 'ENDQR';
}, 1200));
</script>
</body>`);

const page = join(work, 'probe.html');
writeFileSync(page, fixture);

const output = chrome(`file:///${page.replaceAll('\\', '/')}`, 'probe');
const probe = /QRPROBE([\s\S]*?)ENDQR/.exec(output);

if (!probe) {
  console.error('the probe did not run - the page may not have rendered');
  process.exit(1);
}

const result = JSON.parse(probe[1]);
console.log(JSON.stringify(result, null, 2));

// A container nobody can see and a table that grew are both silent failures in a browser.
const invisible = result.anchors.filter(Boolean).some((a) => !a.visible);
if (invisible) console.error('\nFAIL: the chosen container is not visible');
if (result.widensTheTable) console.error('\nFAIL: the button widens the table');
process.exit(invisible || result.widensTheTable ? 1 : 0);
