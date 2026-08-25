// Drives the local window's page in a real browser, the way a person drives it.
//
// This exists because the things it checks are claims about a rendered page: that approving a plan
// runs exactly what was on screen, that a test run in the config builder can be watched and
// stopped, that the settings tab reads the machine. A real layout engine, a real event loop and
// real timers are the only way to ask.
//
//   quickrun --port 9876 --no-update &
//   node scripts/drive-window.mjs http://127.0.0.1:9876 owner/repo [inputs|builder|settings]
//
// Needs Chrome and a running QuickRun, so it is a hand-run tool rather than part of CI.

import { execFile } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [base, repo, mode = 'inputs'] = process.argv.slice(2);
if (!base || !repo) {
  console.error('usage: node scripts/drive-window.mjs <http://127.0.0.1:9876> <repo> [mode]');
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

/** What every probe below gets: the page's own elements, and a way to wait for them. */
const PRELUDE = `
  const $ = (id) => document.getElementById(id);
  const until = async (test, ms = 90000) => {
    const end = Date.now() + ms;
    while (Date.now() < end) {
      const value = test();
      if (value) return value;
      await new Promise((done) => setTimeout(done, 100));
    }
    return null;
  };
  const tab = (name) => [...document.querySelectorAll('nav button')]
    .find((button) => button.dataset.tab === name).click();

  // A fresh browser may still be parsing when the probe starts.
  await until(() => document.getElementById('prepareButton'));
`;

const PROBES = {
  // A changed input value applies itself, and one click still starts the run.
  inputs: `
    $('repoInput').value = REPO;
    $('prepareButton').click();

    const panel = $('planPanel');
    if (!await until(() => !panel.hidden && panel.querySelector('[data-confirm]'))) {
      return { error: 'no plan: ' + panel.textContent.slice(0, 200) };
    }

    const button = () => panel.querySelector('[data-confirm]');
    const commands = () => [...panel.querySelectorAll('.plan code')].map((c) => c.textContent);
    const field = panel.querySelector('select, input');

    const out = { label: button().textContent, before: commands(), fields: !!field };
    if (!field) return out;

    if (field.tagName === 'SELECT') {
      const other = [...field.options].find((o) => o.value !== field.value);
      if (other) field.value = other.value;
    } else {
      field.value = 'driven-by-the-tool';
    }
    out.changed = field.value;

    field.dispatchEvent(new Event('change', { bubbles: true }));
    field.dispatchEvent(new Event('input', { bubbles: true }));
    out.labelWhileTyping = button().textContent;

    await until(() => {
      const note = panel.querySelector('[data-plan-state]').textContent;
      return note.length > 0 && note !== 'applying...';
    });

    out.note = panel.querySelector('[data-plan-state]').textContent;
    out.after = commands();
    out.labelAfterApply = button().textContent;
    out.keptTheForm = field.isConnected;

    button().click();
    out.started = !!await until(() => panel.hidden);
    return out;
  `,

  // A test run in the config builder can be watched and stopped without leaving the tab.
  builder: `
    tab('builder');
    $('bRepo').value = REPO;
    $('bLoad').click();

    // Loading ends with the config checked, so the state line has moved on by then: the source
    // pill is what says a config arrived.
    if (!await until(() => $('bSource').textContent.length > 0)) {
      return { error: 'never loaded: ' + $('bState').textContent };
    }

    $('bTest').click();
    const panel = $('bPlan');
    if (!await until(() => panel.querySelector('[data-confirm]'))) {
      return { error: 'no plan: ' + $('bState').textContent };
    }

    const out = { note: $('bState').textContent };
    panel.querySelector('[data-confirm]').click();

    // The run has to appear here, in the builder, with its log and a Stop.
    const card = await until(() => panel.querySelector('.card'));
    if (!card) return { ...out, error: 'no card in the builder' };

    out.cardAppeared = true;
    out.logArrived = !!await until(() => card.querySelector('[data-log]')?.textContent.length > 0);

    out.pageErrors = [];
    window.addEventListener('error', (event) => out.pageErrors.push(String(event.message)));
    window.addEventListener('unhandledrejection', (event) => out.pageErrors.push(String(event.reason)));

    const stop = await until(() => {
      const button = [...card.querySelectorAll('button')].find((b) => b.textContent === 'Stop');
      return button && !button.disabled ? button : null;
    }, 60000);

    out.tasks = [...card.querySelectorAll('.row.task')]
      .map((row) => [...row.children].map((part) => part.textContent.trim()).filter(Boolean).join(' '));
    out.stopEnabled = !!stop;
    out.alsoInTheRunList = document.querySelectorAll('#runList .card').length;

    if (stop) {
      stop.click();
      out.stopped = !!await until(() => {
        const state = card.querySelector('[data-state]').textContent;
        return state.includes('stopped') || state.includes('succeeded') || state.includes('failed');
      });
    }

    out.finalState = card.querySelector('[data-state]').textContent;
    return out;
  `,

  // A running card keeps up: task rows change, and Stop becomes usable once something is running.
  runs: `
    $('repoInput').value = REPO;
    $('prepareButton').click();

    const panel = $('planPanel');
    if (!await until(() => !panel.hidden && panel.querySelector('[data-confirm]'))) {
      return { error: 'no plan: ' + panel.textContent.slice(0, 200) };
    }

    panel.querySelector('[data-confirm]').click();

    const card = await until(() => document.querySelector('#runList .card'));
    if (!card) return { error: 'no card in the run list' };

    const rows = () => [...card.querySelectorAll('.row.task')]
      .map((row) => [...row.children].map((part) => part.textContent.trim()).filter(Boolean).join(' '));
    const stop = () => [...card.querySelectorAll('button')].find((button) => button.textContent === 'Stop');

    const out = { first: rows(), errors: [] };
    window.addEventListener('error', (event) => out.errors.push(String(event.message)));
    window.addEventListener('unhandledrejection', (event) => out.errors.push(String(event.reason)));

    out.becameLive = !!await until(() => stop() && !stop().disabled, 40000);
    out.rowsChanged = !!await until(() => rows().join('|') !== out.first.join('|'), 40000);
    out.rows = rows();
    out.log = card.querySelector('[data-log]')?.textContent.length ?? 0;

    if (stop() && !stop().disabled) {
      stop().click();
      out.stopped = !!await until(() => card.querySelector('[data-state]').textContent.includes('stopped'));
    }

    out.finalState = card.querySelector('[data-state]').textContent;
    return out;
  `,

  // The settings tab reads the machine rather than showing placeholders.
  settings: `
    tab('settings');
    await until(() => $('autostartState').textContent !== 'checking\\u2026');

    return {
      autostart: $('autostartState').textContent,
      autostartDetail: $('autostartDetail').textContent,
      path: $('pathState').textContent,
      pathDetail: $('pathDetail').textContent,
      pathHow: $('pathHow').textContent.slice(0, 90),
      cli: $('cliHelp').textContent.split('\\n').length + ' lines',
      exe: $('exePath').textContent,
    };
  `,
};

if (!PROBES[mode]) {
  console.error(`unknown mode ${mode} - one of ${Object.keys(PROBES).join(', ')}`);
  process.exit(2);
}

const result = await evaluate(`(async () => {
  const REPO = ${JSON.stringify(repo)};
  ${PRELUDE}
  ${PROBES[mode]}
})()`);

console.log(JSON.stringify(result, null, 1));

socket.close();
chrome.kill();
try { rmSync(profile, { recursive: true, force: true }); } catch { /* Chrome may still hold it */ }
