// Loads QuickRun's own window content in a real browser and drives it, reporting anything it broke.
//
// The page is one big script. A syntax error anywhere in it, or an exception while it is starting,
// leaves a window that draws but does nothing: the tabs stop switching, the address it is listening
// on never appears, and nothing says why - the exception is in a console nobody has open. That is
// exactly what shipped, and running the page is the only thing that catches it.
//
//   node scripts/drive-dashboard.mjs [--exe path/to/quickrun] [--screenshot out.png]
//
// Starts a daemon of its own on a free port, so it never touches a running QuickRun.

import { existsSync, mkdtempSync, writeFileSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { createServer } from 'node:net';

const argument = (name, fallback = null) => {
  const at = process.argv.indexOf(name);
  return at > 0 ? process.argv[at + 1] : fallback;
};

const exe = argument('--exe', process.platform === 'win32'
  ? 'src/QuickRun.App/bin/Debug/net10.0/win-x64/quickrun.exe'
  : 'src/QuickRun.App/bin/Debug/net10.0/quickrun');

const shotPath = argument('--screenshot');

if (!existsSync(exe)) {
  console.error(`no quickrun binary at ${exe} - build it first, or pass --exe`);
  process.exit(2);
}

/** A port nobody is on, so this never collides with a QuickRun somebody is using. */
const freePort = () => new Promise((resolve) => {
  const probe = createServer();
  probe.listen(0, '127.0.0.1', () => {
    const { port } = probe.address();
    probe.close(() => resolve(port));
  });
});

const port = await freePort();
const home = mkdtempSync(join(tmpdir(), 'quickrun-dashboard-'));

const daemon = spawn(exe, ['daemon', '--port', String(port), '--no-update'], {
  env: { ...process.env, QUICKRUN_HOME: home },
  stdio: ['ignore', 'pipe', 'pipe'],
});

/** Waits for the listener rather than guessing how long it takes to start. */
const reachable = async () => {
  for (let attempt = 0; attempt < 100; attempt++) {
    try {
      const response = await fetch(`http://127.0.0.1:${port}/api/ping`);
      if (response.ok) return true;
    } catch { /* not up yet */ }
    await new Promise((wait) => setTimeout(wait, 100));
  }
  return false;
};

if (!await reachable()) {
  console.error('the daemon never answered');
  daemon.kill();
  process.exit(2);
}

const chrome = ['C:/Program Files/Google/Chrome/Application/chrome.exe',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/usr/bin/google-chrome', '/usr/bin/chromium'].find(existsSync);

if (!chrome) {
  console.error('no chrome found');
  daemon.kill();
  process.exit(2);
}

const profile = mkdtempSync(join(tmpdir(), 'quickrun-dashboard-chrome-'));

const browser = spawn(chrome, [
  '--headless=new',
  '--remote-debugging-port=0',
  `--user-data-dir=${profile}`,
  '--no-first-run',
  '--window-size=1100,900',
  `http://127.0.0.1:${port}/`,
], { stdio: ['ignore', 'pipe', 'pipe'] });

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
const page = targets.find((t) => t.type === 'page' && t.url.includes(`:${port}`));

const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((open) => socket.addEventListener('open', open, { once: true }));

let nextId = 1;
const pending = new Map();
const problems = [];
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
  if (result.exceptionDetails) return { thrown: result.exceptionDetails.exception?.description };
  return result.result?.value;
};

// What the page is made of, before anything is clicked.
const tabs = await evaluate(`JSON.stringify([...document.querySelectorAll('nav button[data-tab]')].map((b) => b.dataset.tab))`);
const tabNames = JSON.parse(tabs ?? '[]');

if (tabNames.length === 0) problems.push('the page has no tabs at all');

// Every tab has to open. A dead script leaves them all inert, which is what a frozen window is.
const visited = [];

for (const name of tabNames) {
  await evaluate(`document.querySelector('nav button[data-tab="${name}"]').click()`);
  await new Promise((wait) => setTimeout(wait, 120));

  const shown = await evaluate(
    `[...document.querySelectorAll('main section')].filter((s) => !s.hidden).map((s) => s.id).join(',')`);

  visited.push({ tab: name, shown });
  if (shown !== name) problems.push(`clicking ${name} showed "${shown}"`);
}

// The folder mode: choose it, name a folder, and get a plan for it. Nothing is checked out and
// nothing runs - the plan is what appears, which is the confirmation gate doing its job.
const project = mkdtempSync(join(tmpdir(), 'quickrun-project-'));
writeFileSync(join(project, 'quickrun.yml'), [
  'name: Driven',
  'tasks:',
  '  - name: hello',
  '    run: echo hi',
  '',
].join('\n'));

await evaluate(`document.querySelector('nav button[data-tab="runs"]').click()`);
await evaluate(`document.querySelector('input[name="source"][value="folder"]').click()`);
await new Promise((wait) => setTimeout(wait, 150));

const folderVisible = await evaluate(`!document.getElementById('folderRow').hidden
  && document.getElementById('repoRow').hidden`);
if (folderVisible !== true) problems.push('choosing a folder did not swap the form');

await evaluate(`document.getElementById('folderInput').value = ${JSON.stringify(project)}`);
await evaluate(`document.getElementById('prepareFolderButton').click()`);

// Reading a folder is quick, but it is still a round trip through the daemon.
for (let attempt = 0; attempt < 60; attempt++) {
  const done = await evaluate(`!document.getElementById('planPanel').hidden`);
  if (done === true) break;
  await new Promise((wait) => setTimeout(wait, 200));
}

const planned = await evaluate(`JSON.stringify({
  shown: !document.getElementById('planPanel').hidden,
  commands: [...document.querySelectorAll('#planPanel .plan li')].map((li) => li.textContent.trim()),
  note: document.getElementById('starterState').textContent,
})`);

const plan = JSON.parse(planned ?? '{}');
if (!plan.shown) problems.push(`no plan appeared for a folder: ${plan.note ?? "(nothing said)"}`);
else if (!plan.commands?.some((c) => c.includes('echo hi')))
  problems.push(`the plan did not list the folder's command: ${JSON.stringify(plan.commands)}`);

// The address it listens on: what somebody clicks to open the page in a real browser.
const address = await evaluate(`(document.body.textContent.match(/127\\.0\\.0\\.1:\\d+/) ?? [null])[0]`);
if (!address) problems.push('the page never says where it is listening');

if (consoleErrors.length > 0) problems.push('the console reported an error');

if (shotPath) {
  const { result } = await send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(shotPath, Buffer.from(result.data, 'base64'));
}

socket.close();
browser.kill();
daemon.kill();

console.log(JSON.stringify({ tabs: tabNames, visited, folder: plan, address, consoleErrors, problems }, null, 2));
process.exit(problems.length === 0 ? 0 : 1);
