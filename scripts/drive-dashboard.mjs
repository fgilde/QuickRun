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

import { existsSync, mkdtempSync, readdirSync, writeFileSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { createServer } from 'node:net';

const argument = (name, fallback = null) => {
  const at = process.argv.indexOf(name);
  return at > 0 ? process.argv[at + 1] : fallback;
};

/**
 * The built binary, wherever this platform's build put it.
 *
 * Searched rather than assumed: the project builds with a runtime identifier, so the executable is
 * under net10.0/<rid>/ and the rid differs per platform - which is what made this fail in CI on the
 * one platform I had not run it on.
 */
function findExe() {
  const named = argument('--exe');
  if (named) return named;

  const root = 'src/QuickRun.App/bin/Debug/net10.0';
  const binary = process.platform === 'win32' ? 'quickrun.exe' : 'quickrun';

  if (!existsSync(root)) return null;

  const here = join(root, binary);
  if (existsSync(here)) return here;

  for (const entry of readdirSync(root, { withFileTypes: true })) {
    if (!entry.isDirectory()) continue;
    const candidate = join(root, entry.name, binary);
    if (existsSync(candidate)) return candidate;
  }

  return null;
}

const exe = findExe();
const shotPath = argument('--screenshot');

if (exe === null || !existsSync(exe)) {
  console.error('no quickrun binary under src/QuickRun.App/bin/Debug/net10.0'
    + ' - build it first (dotnet build src/QuickRun.App), or pass --exe');
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
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/usr/bin/google-chrome', '/usr/bin/google-chrome-stable',
  '/usr/bin/chromium', '/usr/bin/chromium-browser',
  '/snap/bin/chromium'].find(existsSync);

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

// Seen rather than merely marked: `hidden` loses to any rule that sets display, and a .row is a
// flexbox - so a form can be "hidden" and completely on screen. Asking the browser what is actually
// visible is the only question worth asking, and asking the other one is how that shipped.
const visible = (id) => evaluate(`(() => {
  const element = document.getElementById(${JSON.stringify(id)});
  if (!element) return null;
  const box = element.getBoundingClientRect();
  return getComputedStyle(element).display !== 'none' && box.width > 0 && box.height > 0;
})()`);

// One field takes both, so what changes while typing is which extras are on screen.
await evaluate(`document.getElementById('repoInput').value = 'acme/app';
  document.getElementById('repoInput').dispatchEvent(new Event('input'))`);
await new Promise((wait) => setTimeout(wait, 120));

const asRepo = {
  branch: await visible('refSelect'),
  copy: await visible('folderOptions'),
  extras: await visible('repoExtras'),
  browse: await visible('browseButton'),
};

if (asRepo.branch !== true) problems.push('a repository offers no branch picker');
if (asRepo.copy !== false) problems.push('a repository offers the copy switch');
if (asRepo.extras !== true) problems.push('a repository hides the token and pull request');
if (asRepo.browse !== true) problems.push('the browse button is not there for a repository');

await evaluate(`document.getElementById('repoInput').value = ${JSON.stringify(project)};
  document.getElementById('repoInput').dispatchEvent(new Event('input'))`);
await new Promise((wait) => setTimeout(wait, 120));

const asFolder = {
  branch: await visible('refSelect'),
  typedBranch: await visible('refInput'),
  copy: await visible('folderOptions'),
  extras: await visible('repoExtras'),
  browse: await visible('browseButton'),
  kind: await evaluate(`document.getElementById('targetKind').textContent`),
};

if (asFolder.branch !== false || asFolder.typedBranch !== false)
  problems.push('a folder still offers a branch');
if (asFolder.copy !== true) problems.push('a folder does not offer the copy switch');
if (asFolder.extras !== false) problems.push('a folder still offers the token and pull request');
if (asFolder.browse !== true) problems.push('the browse button vanished for a folder');
if (!(asFolder.kind ?? '').includes('folder')) problems.push('the form does not say it read a folder');

await evaluate(`document.getElementById('prepareButton').click()`);

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

// Which run is happening, answerable at a glance.
//
// A prepared run is waiting for a decision, so its card has to be marked as such and the tab has to
// say that something is going on. Before this, a run somebody had just started from a badge sat at
// the bottom of the list looking exactly like the ones that finished last week.
await evaluate(`refresh()`);
await new Promise((done) => setTimeout(done, 600));

const attention = JSON.parse(await evaluate(`JSON.stringify({
  tab: document.querySelector('nav button[data-tab="runs"]')?.textContent?.trim(),
  busy: document.querySelector('nav button[data-tab="runs"]')?.dataset.busy ?? '',
  first: (() => {
    const card = document.querySelector('#runList .card');
    if (!card) return null;
    return {
      active: card.dataset.active ?? '',
      waiting: card.dataset.waiting ?? '',
      state: card.querySelector('[data-state]')?.textContent?.trim() ?? '',
    };
  })(),
})`) ?? '{}');

if (!attention.first) problems.push('the prepared run has no card in the list');
else {
  if (attention.first.active !== '1')
    problems.push(`the waiting run's card is not marked active: ${JSON.stringify(attention.first)}`);
  if (attention.first.waiting !== '1')
    problems.push(`the waiting run's card is not marked as waiting: ${JSON.stringify(attention.first)}`);
}

if (attention.busy !== '1' || !/·\s*\d/.test(attention.tab ?? ''))
  problems.push(`the Runs tab does not say anything is going on: ${JSON.stringify(attention)}`);

// The workspaces tab: the directory, the buttons that act on it, and a list that does not hold
// everything else up.
//
// Listing used to sum the size of every file in every checkout before answering, so a machine with
// a few node_modules in there showed an empty tab for minutes. The names come first now, and the
// sizes are a separate request - which is what the timing below is checking.
await evaluate(`document.querySelector('nav button[data-tab="workspaces"]').click()`);
await new Promise((done) => setTimeout(done, 800));

const workspaces = JSON.parse(await evaluate(`(async () => {
  const started = performance.now();
  const answer = await fetch('/api/dashboard/workspaces', { headers: { 'X-QuickRun-Dashboard': TOKEN } });
  const ms = Math.round(performance.now() - started);
  const body = await answer.json();

  return JSON.stringify({
    ms,
    ok: answer.ok,
    count: body.workspaces?.length ?? 0,
    root: document.getElementById('workspaceRoot')?.textContent ?? '',
    openButton: document.getElementById('openRoot')?.textContent ?? '',
    removeAll: !!document.getElementById('cleanAll'),
    columns: [...document.querySelectorAll('#workspaceList th')].map((th) => th.textContent.trim()),
  });
})()`) ?? '{}');

if (!workspaces.ok) problems.push('the workspaces endpoint did not answer');
if (!workspaces.root) problems.push('the workspace directory is not shown');
if (!workspaces.openButton) problems.push('there is no button to open the workspace directory');
if (!workspaces.removeAll) problems.push('there is no Remove all button');

// Generous, because this is a real disk on a real machine - but a hundredth of what walking every
// file used to cost, so it still fails if the sizes creep back into the listing.
if (workspaces.ms > 3000)
  problems.push(`listing the workspaces took ${workspaces.ms}ms - it is measuring sizes again`);

if (workspaces.columns.includes('Last run'))
  problems.push('the Last run column is back: its answer was wrong more often than right');

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

console.log(JSON.stringify({ tabs: tabNames, visited, asRepo, asFolder, plan, attention, workspaces, address, consoleErrors, problems }, null, 2));
process.exit(problems.length === 0 ? 0 : 1);
