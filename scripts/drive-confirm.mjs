// Loads the extension's confirmation window in a real browser, with a real plan, and reports what
// the page did - including anything it threw.
//
// This exists because a page that throws while rendering fails silently: the exception goes to a
// devtools console nobody has open, the half-drawn window looks merely empty, and the buttons do
// nothing because the module never got as far as binding them. That is exactly what shipped, and
// the only way to catch it before a user does is to run the page.
//
//   node scripts/drive-confirm.mjs <plan.json> [--screenshot out.png]
//
// The plan is what POST /api/run returns. Exits non-zero when the page reports an error or when the
// command list is empty for a plan that has commands.

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { extname, join } from 'node:path';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';


const SOURCE = new URL('../extension/src/', import.meta.url);

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
};

const planPath = process.argv[2];
if (!planPath || !existsSync(planPath)) {
  console.error('usage: node scripts/drive-confirm.mjs <plan.json> [--screenshot out.png]');
  process.exit(2);
}

const plan = JSON.parse(readFileSync(planPath, 'utf8'));
const shotAt = process.argv.indexOf('--screenshot');
const shotPath = shotAt > 0 ? process.argv[shotAt + 1] : null;

/**
 * The extension APIs the confirmation window uses, and nothing more.
 *
 * Stubbed rather than mocked away: the point is to run the page's own code against the daemon's own
 * plan, so only the bridge to the browser is replaced.
 */
const stub = `<script>
window.__events = [];
window.addEventListener('error', (e) => window.__events.push('error: ' + (e.error?.stack || e.message)));
window.addEventListener('unhandledrejection', (e) => window.__events.push('rejection: ' + (e.reason?.stack || e.reason)));

const PLAN = ${JSON.stringify(plan)};

window.chrome = {
  storage: {
    session: {
      get: async (key) => (key === 'pendingRun' ? { pendingRun: PLAN } : {}),
      remove: async () => {},
    },
    local: { get: async (defaults) => defaults },
  },
  runtime: {
    sendMessage: async (message) => {
      window.__events.push('sendMessage: ' + message.type);
      return { ok: true };
    },
    onMessage: { addListener: () => {}, removeListener: () => {} },
  },
  windows: { onRemoved: { addListener: () => {}, removeListener: () => {} } },
};
</script>`;

const server = createServer((request, response) => {
  const path = decodeURIComponent(new URL(request.url, 'http://localhost').pathname).slice(1) || 'confirm.html';

  try {
    if (path === 'confirm.html') {
      const html = readFileSync(new URL('confirm.html', SOURCE), 'utf8')
        .replace('</head>', `${stub}\n</head>`);
      response.writeHead(200, { 'content-type': TYPES['.html'] }).end(html);
      return;
    }

    const body = readFileSync(new URL(path, SOURCE));
    response.writeHead(200, { 'content-type': TYPES[extname(path)] ?? 'application/octet-stream' }).end(body);
  } catch {
    response.writeHead(404).end('not here');
  }
});

await new Promise((ready) => server.listen(0, '127.0.0.1', ready));
const port = server.address().port;

const chrome = ['C:/Program Files/Google/Chrome/Application/chrome.exe',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/usr/bin/google-chrome', '/usr/bin/chromium'].find(existsSync);

if (!chrome) {
  console.error('no chrome found');
  process.exit(2);
}

const profile = mkdtempSync(join(tmpdir(), 'quickrun-confirm-'));

const browser = spawn(chrome, [
  '--headless=new',
  '--remote-debugging-port=0',
  `--user-data-dir=${profile}`,
  '--no-first-run',
  '--window-size=820,900',
  `http://127.0.0.1:${port}/confirm.html`,
], { stdio: ['ignore', 'pipe', 'pipe'] });

/** The debugging port Chrome chose, from the line it prints on stderr. */
const endpoint = await new Promise((resolve, reject) => {
  let buffered = '';
  browser.stderr.on('data', (chunk) => {
    buffered += chunk;
    const match = buffered.match(/ws:\/\/127\.0\.0\.1:(\d+)\/devtools\/browser\/\S+/);
    if (match) resolve(match[0]);
  });
  browser.on('exit', (code) => reject(new Error(`chrome exited with ${code}`)));
  setTimeout(() => reject(new Error('chrome never printed a debugging endpoint')), 20000);
});

const targets = await (await fetch(`http://127.0.0.1:${new URL(endpoint).port}/json/list`)).json();
const page = targets.find((t) => t.type === 'page' && t.url.includes('confirm.html'));

// Node's own WebSocket, so this needs nothing installed.
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((open) => socket.addEventListener('open', open, { once: true }));

let nextId = 1;
const pending = new Map();
const consoleErrors = [];

socket.addEventListener('message', (frame) => {
  const message = JSON.parse(frame.data);

  if (message.id && pending.has(message.id)) {
    pending.get(message.id)(message);
    pending.delete(message.id);
    return;
  }

  if (message.method === 'Runtime.exceptionThrown')
    consoleErrors.push(message.params.exceptionDetails.exception?.description
      ?? message.params.exceptionDetails.text);

  if (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error')
    consoleErrors.push(message.params.args.map((a) => a.description ?? a.value).join(' '));
});

const send = (method, params = {}) => new Promise((resolve) => {
  const id = nextId++;
  pending.set(id, resolve);
  socket.send(JSON.stringify({ id, method, params }));
});

await send('Runtime.enable');
await send('Page.enable');
await new Promise((wait) => setTimeout(wait, 1500));

const evaluate = async (expression) => {
  const { result } = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  return result.result?.value;
};

const state = await evaluate(`JSON.stringify({
  events: window.__events,
  subtitle: document.getElementById('subtitle').textContent,
  origin: document.getElementById('origin').textContent,
  description: document.getElementById('description').hidden ? null : document.getElementById('description').textContent,
  commands: [...document.querySelectorAll('#commands li')].map((li) => li.textContent),
  inputs: [...document.querySelectorAll('#inputs input, #inputs select')].map((f) => f.id || f.name),
  approveText: document.getElementById('approve').textContent,
})`);

const seen = JSON.parse(state);

// Clicking Run has to do something. A page whose module threw never bound the listener, and the
// button then sits there looking enabled and doing nothing - which is the failure this catches.
await evaluate(`document.getElementById('approve').click()`);
await new Promise((wait) => setTimeout(wait, 400));
const afterClick = JSON.parse(await evaluate('JSON.stringify(window.__events)'));

if (shotPath) {
  const { result } = await send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(shotPath, Buffer.from(result.data, 'base64'));
}

socket.close();
browser.kill();
server.close();

const problems = [];
if (seen.events.some((e) => e.startsWith('error') || e.startsWith('rejection'))) problems.push('the page threw');
if (consoleErrors.length > 0) problems.push('the console reported an error');
if (plan.commands.length > 0 && seen.commands.length !== plan.commands.length)
  problems.push(`the plan has ${plan.commands.length} commands and the page shows ${seen.commands.length}`);
if ((plan.inputs?.length ?? 0) > 0 && seen.inputs.length === 0) problems.push('the plan has inputs and the page shows none');
if (!afterClick.some((e) => e.startsWith('sendMessage'))) problems.push('clicking Run did nothing');

console.log(JSON.stringify({ seen, consoleErrors, afterClick, problems }, null, 2));
process.exit(problems.length === 0 ? 0 : 1);
