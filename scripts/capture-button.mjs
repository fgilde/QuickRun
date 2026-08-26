// Takes the screenshot of the button where it actually lives: a GitHub repository page.
//
// The extension is loaded unpacked into a real Chrome, so what the picture shows is the real
// injection into the real page - the one thing a mockup of this feature could never prove.
//
//   quickrun --port 9876 --no-update --no-tray &
//   sh extension/build.sh && node scripts/capture-button.mjs https://github.com/fgilde/QuickRun
//
// Needs Chrome, a running QuickRun on the port the extension expects, and network access.

import { execFile } from 'node:child_process';
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const [url = 'https://github.com/fgilde/MudBlazor.Extensions', outDir = 'site/public/screenshots'] =
  process.argv.slice(2);

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

const extension = resolve('extension/dist/chromium');
readFileSync(join(extension, 'manifest.json'));

mkdirSync(outDir, { recursive: true });

const profile = join(tmpdir(), `quickrun-button-${process.pid}`);
const port = 9700 + (process.pid % 200);
const wait = (ms) => new Promise((done) => setTimeout(done, ms));

// Recent Chrome refuses --load-extension outright, so there are two ways in: let this script start
// Chrome with the unpacked build (older Chrome, and anything with the flag still enabled), or load
// the extension by hand once and point this at that browser:
//
//   chrome --remote-debugging-port=9222 --user-data-dir=%TEMP%/qr-chrome
//   (load extension/dist/chromium via chrome://extensions, open the repository page)
//   node scripts/capture-button.mjs <url> site/public/screenshots 9222
const attachTo = process.argv[4];

const chrome = attachTo ? null : execFile(CHROME, [
  `--user-data-dir=${profile}`, `--remote-debugging-port=${port}`,
  `--disable-extensions-except=${extension}`, `--load-extension=${extension}`,
  '--no-first-run', '--no-default-browser-check', '--hide-scrollbars',
  '--window-size=1500,1000', '--window-position=40,40', '--force-device-scale-factor=1',
  url,
]);

const debugPort = attachTo ? Number(attachTo) : port;

async function endpoint() {
  for (let attempt = 0; attempt < 80; attempt += 1) {
    try {
      const list = await fetch(`http://127.0.0.1:${debugPort}/json/list`).then((r) => r.json());
      const page = list.find((p) => p.type === 'page' && p.url.startsWith('https://github.com'));
      if (page?.webSocketDebuggerUrl) return page.webSocketDebuggerUrl;
    } catch { /* not listening yet */ }
    await wait(300);
  }
  throw new Error('Chrome never opened the GitHub page');
}

const socket = new WebSocket(await endpoint());
await new Promise((open, fail) => { socket.onopen = open; socket.onerror = fail; });

let id = 1;
const pending = new Map();
socket.onmessage = (message) => {
  const reply = JSON.parse(message.data);
  const settle = pending.get(reply.id);
  if (settle) { pending.delete(reply.id); settle(reply); }
};

function send(method, params = {}) {
  const mine = id += 1;
  socket.send(JSON.stringify({ id: mine, method, params }));
  return new Promise((done, fail) => pending.set(mine, (reply) =>
    reply.error ? fail(new Error(`${method}: ${reply.error.message}`)) : done(reply.result)));
}

/**
 * Runs an expression in the page, retrying when GitHub navigates underneath it: Turbo replaces the
 * execution context mid-call, which is not a failure of the thing being measured.
 */
async function evaluate(expression) {
  for (let attempt = 0; attempt < 4; attempt += 1) {
    try {
      const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? 'threw');
      return result.result.value;
    } catch (failure) {
      if (!String(failure.message).includes('context was destroyed')) throw failure;
      await wait(1500);
    }
  }

  throw new Error('the page kept navigating');
}

await send('Page.enable');
await send('Runtime.enable');

// GitHub renders, then Turbo settles. Measuring before that is measuring the wrong page.
await wait(5000);

// The button is injected after GitHub's own rendering settles.
const found = await evaluate(`(async () => {
  const until = async (test, ms) => {
    const end = Date.now() + ms;
    while (Date.now() < end) {
      const value = test();
      if (value) return value;
      await new Promise((done) => setTimeout(done, 200));
    }
    return null;
  };

  const button = await until(() => document.querySelector('.quickrun-button'), 30000);
  if (!button) return null;

  button.scrollIntoView({ block: 'center' });
  await new Promise((done) => setTimeout(done, 400));

  const box = button.getBoundingClientRect();
  return { state: button.dataset.state, title: button.title, top: box.top, left: box.left };
})()`);

if (!found) {
  // Enough to tell "the extension never ran" from "the anchor moved", which are different problems.
  const diagnosis = await evaluate(`JSON.stringify({
    placement: typeof globalThis.QuickRunPlacement,
    targets: typeof globalThis.QuickRunTargets,
    toolbar: !!document.querySelector('#repository-details-container, [data-testid="latest-commit"]'),
    path: location.pathname,
  })`).catch(() => 'no answer');

  console.error(`the button never appeared: ${diagnosis}`);
} else {
  console.log(`button: ${found.state} (${found.title})`);

  const { data } = await send('Page.captureScreenshot', { format: 'png' });
  const file = join(outDir, 'github.png');
  writeFileSync(file, Buffer.from(data, 'base64'));
  console.log(`wrote ${file}`);
}

socket.close();

if (chrome) {
  chrome.kill();
  try { rmSync(profile, { recursive: true, force: true }); } catch { /* Chrome may still hold it */ }
}
