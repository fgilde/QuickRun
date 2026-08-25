// Drives the local window's page in a real browser, the way a person drives it.
//
// This exists because the confirmation gate is a rule about a rendered page: what you approve has
// to be what runs. A changed input value applies itself, the command list is rebuilt, and the run
// still starts on one click - three things that only a real layout engine, a real event loop and a
// real timer can be asked about.
//
//   quickrun --port 9876 --no-update &
//   node scripts/drive-window.mjs http://127.0.0.1:9876 <a repository with inputs>
//
// Needs Chrome and a running QuickRun, so it is a hand-run tool rather than part of CI.

import { execFile } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [base, repo] = process.argv.slice(2);
if (!base || !repo) {
  console.error('usage: node scripts/drive-window.mjs <http://127.0.0.1:9876> <repo>');
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

const profile = mkdtempSync(join(tmpdir(), 'quickrun-drive-'));
const port = 9222 + (process.pid % 300);

const chrome = execFile(CHROME, [
  '--headless=new', '--disable-gpu', '--no-sandbox', '--no-first-run',
  `--user-data-dir=${profile}`, `--remote-debugging-port=${port}`,
  '--window-size=1400,1000', base,
]);

/** The page's debugging endpoint, once Chrome has one. */
async function target() {
  for (let attempt = 0; attempt < 60; attempt += 1) {
    try {
      const pages = await fetch(`http://127.0.0.1:${port}/json/list`).then((r) => r.json());
      const page = pages.find((p) => p.type === 'page' && p.url.startsWith(base));
      if (page?.webSocketDebuggerUrl) return page.webSocketDebuggerUrl;
    } catch { /* not listening yet */ }
    await new Promise((done) => setTimeout(done, 250));
  }
  throw new Error('Chrome never opened the page');
}

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

/** Runs an expression in the page and returns what it evaluated to. */
function evaluate(expression) {
  const id = nextId += 1;
  socket.send(JSON.stringify({
    id,
    method: 'Runtime.evaluate',
    params: { expression, awaitPromise: true, returnByValue: true },
  }));

  return new Promise((done, fail) => waiting.set(id, (reply) => {
    if (reply.error) return fail(new Error(reply.error.message));
    if (reply.result?.exceptionDetails) {
      return fail(new Error(reply.result.exceptionDetails.exception?.description ?? 'page threw'));
    }
    done(reply.result.result.value);
  }));
}


// The page is the one being tested, so everything below goes through its own controls.
const result = await evaluate(`(async () => {
  const $ = (id) => document.getElementById(id);

  $('repoInput').value = ${JSON.stringify(repo)};
  $('prepareButton').click();

  const panel = $('planPanel');
  const until = async (test, ms = 30000) => {
    const end = Date.now() + ms;
    while (Date.now() < end) {
      if (test()) return true;
      await new Promise((done) => setTimeout(done, 100));
    }
    return false;
  };

  if (!await until(() => !panel.hidden && panel.querySelector('[data-confirm]'))) {
    return { error: 'no plan: ' + panel.textContent.slice(0, 200) };
  }

  const button = () => panel.querySelector('[data-confirm]');
  const commands = () => [...panel.querySelectorAll('.plan code')].map((c) => c.textContent);
  const field = panel.querySelector('select, input');

  const out = { label: button().textContent, before: commands(), fields: !!field };
  if (!field) return out;

  // Change a value the way a person does, then leave it alone.
  if (field.tagName === 'SELECT') {
    const other = [...field.options].find((o) => o.value !== field.value);
    if (other) field.value = other.value;
    out.changed = field.value;
  } else {
    field.value = 'driven-by-the-tool';
    out.changed = field.value;
  }
  field.dispatchEvent(new Event('change', { bubbles: true }));
  field.dispatchEvent(new Event('input', { bubbles: true }));

  out.labelWhileTyping = button().textContent;

  // Nobody clicked anything: the value has to apply itself.
  await until(() => panel.querySelector('[data-plan-state]').textContent.length > 0
                    && panel.querySelector('[data-plan-state]').textContent !== 'applying...');

  out.note = panel.querySelector('[data-plan-state]').textContent;
  out.after = commands();
  out.labelAfterApply = button().textContent;
  out.keptFocus = document.activeElement === field || field.isConnected;

  // And now one single click has to start it.
  button().click();
  out.started = await until(() => panel.hidden);

  return out;
})()`);

console.log(JSON.stringify(result, null, 1));

socket.close();
chrome.kill();
try { rmSync(profile, { recursive: true, force: true }); } catch { /* Chrome may still hold it */ }
