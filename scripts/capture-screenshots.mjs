// Takes the screenshots the website shows, from the real thing.
//
// The pictures on the landing page have to be the window people get, not a mockup of it - so they
// are captured from a running QuickRun with a real repository going, in a real browser engine, and
// committed under site/public/screenshots.
//
//   quickrun --port 9960 --no-update --no-webview --no-tray &
//   node scripts/capture-screenshots.mjs http://127.0.0.1:9960 owner/repo
//
// Needs Chrome and a running QuickRun, so it is a hand-run tool rather than part of CI.

import { execFile } from 'node:child_process';
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [base, repo = 'fgilde/MudBlazor.Extensions', outDir = 'site/public/screenshots'] =
  process.argv.slice(2);

if (!base) {
  console.error('usage: node scripts/capture-screenshots.mjs <http://127.0.0.1:9960> [repo] [outDir]');
  process.exit(2);
}

const CHROME = process.env.CHROME ?? [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  '/usr/bin/google-chrome',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
].find((path) => {
  try { readFileSync(path); return true; } catch { return false; }
});

if (!CHROME) {
  console.error('Chrome not found. Set CHROME to its path.');
  process.exit(2);
}

// The window's own content column is 1000px wide, so a viewport just wider than that keeps the
// pictures free of empty space.
const WIDTH = 1080;
const HEIGHT = 880;

mkdirSync(outDir, { recursive: true });

const profile = join(tmpdir(), `quickrun-shots-${process.pid}`);
const port = 9500 + (process.pid % 300);

const chrome = execFile(CHROME, [
  '--headless=new', '--disable-gpu', '--no-sandbox', '--no-first-run', '--hide-scrollbars',
  `--user-data-dir=${profile}`, `--remote-debugging-port=${port}`,
  `--window-size=${WIDTH},${HEIGHT}`, '--force-device-scale-factor=1', base,
]);

async function target() {
  for (let attempt = 0; attempt < 60; attempt += 1) {
    try {
      const pages = await fetch(`http://127.0.0.1:${port}/json/list`).then((r) => r.json());
      const page = pages.find((p) => p.type === 'page' && p.url.startsWith(base));
      if (page?.webSocketDebuggerUrl) return page.webSocketDebuggerUrl;
    } catch { /* not listening yet */ }
    await wait(250);
  }
  throw new Error('Chrome never opened the page');
}

const wait = (ms) => new Promise((done) => setTimeout(done, ms));

const socket = new WebSocket(await target());
await new Promise((open, fail) => { socket.onopen = open; socket.onerror = fail; });

let nextId = 1;
const waiting = new Map();
socket.onmessage = (message) => {
  const reply = JSON.parse(message.data);
  const settle = waiting.get(reply.id);
  if (!settle) return;
  waiting.delete(reply.id);
  settle(reply);
};

function send(method, params = {}) {
  const id = nextId += 1;
  socket.send(JSON.stringify({ id, method, params }));

  return new Promise((done, fail) => waiting.set(id, (reply) => {
    if (reply.error) return fail(new Error(`${method}: ${reply.error.message}`));
    done(reply.result);
  }));
}

async function evaluate(expression) {
  const result = await send('Runtime.evaluate', {
    expression: `(async () => { ${PRELUDE}\n${expression} })()`,
    awaitPromise: true,
    returnByValue: true,
  });

  if (result.exceptionDetails) {
    throw new Error(result.exceptionDetails.exception?.description ?? 'the page threw');
  }

  return result.result.value;
}

/** Helpers every step gets. */
const PRELUDE = `
  const $ = (id) => document.getElementById(id);
  const until = async (test, ms = 60000) => {
    const end = Date.now() + ms;
    while (Date.now() < end) {
      const value = test();
      if (value) return value;
      await new Promise((done) => setTimeout(done, 120));
    }
    return null;
  };
  const tab = (name) => [...document.querySelectorAll('nav button')]
    .find((button) => button.dataset.tab === name)?.click();
  const show = (element) => element?.scrollIntoView({ block: 'start' });
`;

async function shoot(name) {
  // A frame to settle after scrolling, so nothing is captured mid-animation.
  await wait(500);

  const { data } = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  const file = join(outDir, `${name}.png`);
  writeFileSync(file, Buffer.from(data, 'base64'));
  console.log(`wrote ${file}`);
}

await send('Page.enable');
await send('Runtime.enable');
await send('Emulation.setDeviceMetricsOverride', {
  width: WIDTH, height: HEIGHT, deviceScaleFactor: 1, mobile: false,
});
// Light, because the site around the pictures is light.
await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'light' }] });

await evaluate(`await until(() => $('prepareButton'));`);

// --- what is running -------------------------------------------------------------------------
console.log('runs...');
await evaluate(`
  tab('runs');
  // A plan left over from an earlier capture is noise in this picture, not news. The page's own
  // token is used, because cancelling through the page is the same path a click takes.
  const live = document.querySelector('#runList .card [data-state] .pill')?.textContent ?? '';
  const stale = [...document.querySelectorAll('#runList .card')]
    .filter((card) => !card.textContent.includes('running'))
    .map((card) => card.dataset.run);

  const post = (id, what) => fetch('/api/dashboard/runs/' + id + '/' + what, {
    method: 'POST',
    headers: { 'X-QuickRun-Dashboard': TOKEN },
  }).catch(() => null);

  // Cancelled or finished attempts from an earlier capture: off the list entirely, so the picture
  // shows the run that is actually going.
  for (const id of stale) {
    await post(id, 'cancel');
    await post(id, 'forget');
  }

  if (stale.length > 0) {
    await refresh();
    await until(() => document.querySelectorAll('#runList .card').length <= 1, 8000);
  }
  const card = await until(() => document.querySelector('#runList .card'));
  // The log is the point of this shot, so it gets room: the starter card above it is collapsed.
  const details = document.querySelector('#runs details');
  if (details) details.open = false;
  show(document.querySelector('#runList'));
  window.scrollBy(0, -70);
  return !!card;
`);
await shoot('runs');

// --- the plan, with the values a config declares ---------------------------------------------
console.log('plan...');
const planned = await evaluate(`
  tab('runs');
  window.scrollTo(0, 0);
  $('repoInput').value = ${JSON.stringify(repo)};
  $('prepareButton').click();

  const panel = $('planPanel');
  const ready = await until(() => !panel.hidden && panel.querySelector('[data-confirm]'), 180000);
  if (!ready) return { ok: false, state: panel.textContent.slice(0, 160) };

  show(panel);
  window.scrollBy(0, -90);
  return { ok: true };
`);

if (planned.ok) await shoot('plan');
else console.error(`no plan: ${planned.state}`);

// --- the config builder ----------------------------------------------------------------------
console.log('builder...');
const built = await evaluate(`
  tab('builder');
  $('bRepo').value = ${JSON.stringify(repo)};
  $('bLoad').click();

  const loaded = await until(() => $('bSource').textContent.length > 0, 180000);
  if (!loaded) return { ok: false, state: $('bState').textContent };

  // The editor renders asynchronously; without this the shot can catch an empty gutter.
  await until(() => document.querySelector('.monaco-editor .view-lines')?.textContent.length > 40);
  window.scrollTo(0, 0);
  return { ok: true };
`);

if (built.ok) await shoot('builder');
else console.error(`no config: ${built.state}`);

// --- settings ---------------------------------------------------------------------------------
console.log('settings...');
await evaluate(`
  tab('settings');
  await until(() => $('autostartState').textContent !== 'checking\\u2026');
  window.scrollTo(0, 0);
  return true;
`);
await shoot('settings');

// --- workspaces -------------------------------------------------------------------------------
console.log('workspaces...');
await evaluate(`
  tab('workspaces');
  await until(() => document.querySelectorAll('#workspaceList .card').length > 0);
  window.scrollTo(0, 0);
  return true;
`);
await shoot('workspaces');

socket.close();
chrome.kill();
try { rmSync(profile, { recursive: true, force: true }); } catch { /* Chrome may still hold it */ }
