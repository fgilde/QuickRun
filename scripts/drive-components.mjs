// Loads the web components in a real browser and presses the button.
//
// Every branch of a click is a security decision - hand over to the window, ask the browser for the
// scheme, or send somebody to the download page - and none of them can be checked by reading the
// file. So this runs it: a headless Chrome, the real components.js, and a fake daemon on loopback
// that can answer, refuse, or not be there at all.
//
//   node scripts/drive-components.mjs [--screenshot out.png]
//
// Exits non-zero on the first thing that does not hold.

import { readFileSync, existsSync, writeFileSync, mkdtempSync } from 'node:fs';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import assert from 'node:assert/strict';

const COMPONENTS = new URL('../site/public/components.js', import.meta.url);

/**
 * The fake daemon, and the page that embeds the button.
 *
 * Both from one server, so what the page does is the only variable. `answer` is what the next
 * /api/show will say - the trusted-site case and the refused case are the same click with a
 * different answer behind it.
 */
let answer = { shown: true };
const asked = [];

/**
 * The page under test, in two documents.
 *
 * The button lives in an iframe and the recorder lives in the page around it, because a click is
 * allowed to navigate - that is three of the branches - and a navigation takes the document's own
 * state with it. Before this was split, "what did the click record" was read out of a document the
 * click had already replaced, which came back as nothing at all. Same origin, so the page can reach
 * straight into the frame: no messages, no bridge, and the component sees an ordinary document.
 */
const FRAME = `<!doctype html>
<html><head><meta charset="utf-8"></head>
<body><script type="module" src="/components.js"></script></body></html>`;

const PAGE = `<!doctype html>
<html><head><meta charset="utf-8"><title>components</title></head>
<body>
  <div id="stage"></div>
  <script>
    window.__seen = [];
    window.__stop = false;

    const record = (event) => {
      window.__seen.push({ type: event.type, detail: event.detail, cancelable: event.cancelable });
      if (window.__stop && event.type === 'quickrun-run') event.preventDefault();
    };

    window.__put = async (attributes) => {
      const stage = document.getElementById('stage');
      stage.textContent = '';

      const frame = document.createElement('iframe');
      frame.src = '/frame.html';
      frame.width = '820';
      frame.height = '160';
      frame.style.border = '0';

      const loaded = new Promise((done) => frame.addEventListener('load', done, { once: true }));
      stage.append(frame);
      await loaded;

      window.__frame = frame;
      const inside = frame.contentDocument;
      await frame.contentWindow.customElements.whenDefined('quickrun-btn');

      // On the frame's document, which is the point of bubbling: one listener for a page full of
      // buttons.
      for (const type of ['quickrun-status', 'quickrun-run', 'quickrun-handover'])
        inside.addEventListener(type, record);

      const button = inside.createElement('quickrun-btn');
      for (const [key, value] of Object.entries(attributes)) button.setAttribute(key, value);
      button.textContent = 'Run this';
      inside.body.append(button);

      return true;
    };

    window.__press = () => window.__frame.contentDocument
      .querySelector('quickrun-btn').shadowRoot.querySelector('[part="button"]').click();

    window.__look = () => {
      const view = window.__frame.contentWindow;
      const host = window.__frame.contentDocument.querySelector('quickrun-btn');
      const button = host.shadowRoot.querySelector('[part="button"]');
      const icon = host.shadowRoot.querySelector('[part="icon"]');

      return {
        label: host.shadowRoot.querySelector('[part="label"]').textContent,
        parts: [...host.shadowRoot.querySelectorAll('[part]')].map((e) => e.getAttribute('part')),
        side: button.dataset.icon,
        iconShown: icon ? view.getComputedStyle(icon).display !== 'none' : false,
        background: view.getComputedStyle(button).backgroundColor,
        radius: view.getComputedStyle(button).borderTopLeftRadius,
        styled: Boolean(host.shadowRoot.querySelector('style')),
        running: host.dataset.running !== undefined,
        light: host.textContent.trim(),
      };
    };
  </script>
</body></html>`;

const server = createServer((request, response) => {
  const url = new URL(request.url, 'http://127.0.0.1');

  if (url.pathname === '/frame.html') {
    response.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }).end(FRAME);
    return;
  }

  if (url.pathname === '/components.js') {
    response.writeHead(200, { 'content-type': 'text/javascript; charset=utf-8' })
      .end(readFileSync(COMPONENTS));
    return;
  }

  if (url.pathname === '/api/ping') {
    response.writeHead(200, {
      'content-type': 'application/json',
      'access-control-allow-origin': '*',
    }).end(JSON.stringify({ ok: true, version: '9.9.9-test', busy: false }));
    return;
  }

  if (url.pathname === '/api/show') {
    asked.push(url.search);

    if (answer === null) {
      response.writeHead(403).end('{"error":"not a trusted site"}');
      return;
    }

    response.writeHead(200, { 'content-type': 'application/json' }).end(JSON.stringify(answer));
    return;
  }

  response.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }).end(PAGE);
});

await new Promise((ready) => server.listen(0, '127.0.0.1', ready));
const port = server.address().port;

/**
 * A port with nothing on it: the reader who has not installed QuickRun.
 *
 * Taken and released rather than picked - a number out of thin air might be somebody's dev server,
 * and 9 and its neighbours are on Chrome's blocked list, which fails for the wrong reason.
 */
const NOTHING = await new Promise((resolve) => {
  const empty = createServer();
  empty.listen(0, '127.0.0.1', () => {
    const chosen = empty.address().port;
    empty.close(() => resolve(chosen));
  });
});

const chrome = ['C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/usr/bin/google-chrome', '/usr/bin/google-chrome-stable',
  '/usr/bin/chromium', '/usr/bin/chromium-browser',
  '/snap/bin/chromium'].find(existsSync);

if (!chrome) {
  console.error('no chrome found');
  process.exit(2);
}

const profile = mkdtempSync(join(tmpdir(), 'quickrun-components-'));

const browser = spawn(chrome, [
  '--headless=new',
  '--remote-debugging-port=0',
  `--user-data-dir=${profile}`,
  '--no-first-run',
  '--window-size=900,500',
  // quickrun.org goes nowhere: what is being checked is where a click *wants* to go, which
  // Page.frameRequestedNavigation reports before anything loads - and a navigation that actually
  // succeeded would unload the page along with everything the click was supposed to have recorded.
  // Only that host, or the test page itself would be redirected too.
  '--host-resolver-rules=MAP quickrun.org 127.0.0.1:1',
  `http://127.0.0.1:${port}/page.html`,
], { stdio: ['ignore', 'pipe', 'pipe'] });

const endpoint = await new Promise((resolve, reject) => {
  let buffered = '';
  browser.stderr.on('data', (chunk) => {
    buffered += chunk;
    const match = buffered.match(/ws:\/\/127\.0\.0\.1:(\d+)\/devtools\/browser\/\S+/);
    if (match) resolve(match[0]);
  });
  browser.on('exit', (code) => reject(new Error(
    `chrome exited with ${code}${buffered ? `: ${buffered.trim()}` : ' and said nothing'}`)));
  setTimeout(() => reject(new Error('chrome never printed a debugging endpoint within 60s'
    + (buffered ? `, it said: ${buffered.trim()}` : ''))), 60000);
});

const targets = await (await fetch(`http://127.0.0.1:${new URL(endpoint).port}/json/list`)).json();
const page = targets.find((t) => t.type === 'page' && t.url.includes('page.html'));

const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((open) => socket.addEventListener('open', open, { once: true }));

let nextId = 1;
const pending = new Map();
const thrown = [];
let wanted = [];

socket.addEventListener('message', (frame) => {
  const message = JSON.parse(frame.data);

  if (message.id && pending.has(message.id)) {
    pending.get(message.id)(message);
    pending.delete(message.id);
    return;
  }

  if (message.method === 'Runtime.exceptionThrown')
    thrown.push(message.params.exceptionDetails.exception?.description
      ?? message.params.exceptionDetails.text);

  // Where a click asked to go. Fired before anything is loaded, so an address that does not resolve
  // is still recorded - which is the whole point.
  if (message.method === 'Page.frameRequestedNavigation') wanted.push(message.params.url);
});

const send = (method, params = {}) => new Promise((resolve) => {
  const id = nextId++;
  pending.set(id, resolve);
  socket.send(JSON.stringify({ id, method, params }));
});

await send('Runtime.enable');
await send('Page.enable');

const evaluate = async (expression) => {
  const { result } = await send('Runtime.evaluate',
    { expression, returnByValue: true, awaitPromise: true });

  if (result.exceptionDetails) {
    throw new Error(result.exceptionDetails.exception?.description
      ?? result.exceptionDetails.text);
  }

  if (!result.result) {
    throw new Error(`the page was gone when asked "${expression.slice(0, 60)}" - a navigation from`
      + ' the click before landed in the middle of this case');
  }

  return result.result.value;
};

const wait = (ms) => new Promise((done) => setTimeout(done, ms));

// The page itself, once: from here on only frames come and go.
for (let attempt = 0; ; attempt += 1) {
  await wait(200);
  if (await evaluate("typeof window.__put === 'function'")) break;
  if (attempt > 40) throw new Error('the test page never loaded');
}

/** One case: a new frame with a button in it, pressed, and what came of that. */
async function press(attributes,
  { stop = false, shows = { shown: true }, settle = 700, soak = 500 } = {}) {
  answer = shows;

  await evaluate(`window.__stop = ${stop}; window.__seen = []`);
  await evaluate(`window.__put(${JSON.stringify(attributes)})`);

  // The ping has to have answered before the press, or the status event and data-running are
  // simply not there yet - which is a race, not a finding.
  await wait(soak);

  const look = JSON.parse(await evaluate('JSON.stringify(window.__look())'));

  // The frame's own load is a navigation too; only what the click asks for is interesting.
  wanted = [];
  asked.length = 0;

  await evaluate('window.__press()');
  await wait(settle);

  return {
    look,
    seen: JSON.parse(await evaluate('JSON.stringify(window.__seen)')),
    wanted: [...wanted],
    asked: [...asked],
  };
}

const checks = [];
const check = (what, run) => {
  try {
    run();
    checks.push(`  ok   ${what}`);
  } catch (error) {
    checks.push(`  FAIL ${what}\n       ${error.message.split('\n')[0]}`);
    process.exitCode = 1;
  }
};

// 1. QuickRun is there and this site is trusted: the window opens, and nothing navigates anywhere.
const trusted = await press({ repo: 'acme/app', 'run-cfg': 'collection', port: String(port) });

check('the element upgrades and shows its parts', () => {
  assert.deepEqual(trusted.look.parts.sort(), ['button', 'icon', 'label']);
  assert.equal(trusted.look.label, 'Run this');
  assert.equal(trusted.look.side, 'left');
  assert.equal(trusted.look.iconShown, true);
  assert.equal(trusted.look.styled, true);
});

check('the ping is reported and left on the host for styling', () => {
  const status = trusted.seen.find((e) => e.type === 'quickrun-status');
  assert.equal(status?.detail.running, true);
  assert.equal(status?.detail.version, '9.9.9-test');
  assert.equal(trusted.look.running, true);
});

check('a trusted site gets the window, and the config travels with it', () => {
  const over = trusted.seen.find((e) => e.type === 'quickrun-handover');
  assert.equal(over?.detail.how, 'window');
  assert.equal(trusted.asked.length, 1);
  assert.match(trusted.asked[0], /repo=acme%2Fapp/);
  assert.match(trusted.asked[0], /config=collection/);
  assert.deepEqual(trusted.wanted, [], 'a hand-over to the window must not navigate');
});

check('quickrun-run comes first, bubbles, and can be cancelled', () => {
  const run = trusted.seen.find((e) => e.type === 'quickrun-run');
  assert.equal(run?.cancelable, true);
  assert.equal(run.detail.target.repo, 'acme/app');
  assert.equal(run.detail.target.config, 'collection');
  assert.ok(trusted.seen.indexOf(run) < trusted.seen.findIndex((e) => e.type === 'quickrun-handover'));
});

// 2. The same click on a site nobody has trusted: the scheme, which is one browser prompt.
const untrusted = await press({ repo: 'acme/app', port: String(port) }, { shows: null });

check('a site that is not trusted falls back to the scheme', () => {
  assert.equal(untrusted.seen.find((e) => e.type === 'quickrun-handover')?.detail.how, 'scheme');
  assert.ok(untrusted.wanted.some((url) => url.startsWith('quickrun://run?repo=acme%2Fapp')),
    `nothing asked for the scheme, it asked for: ${untrusted.wanted.join(', ') || 'nothing'}`);
});

// 3. Nothing listening: the run page, carrying the repository so the press is not lost.
// Long enough for the ping to give up: with nothing listening the answer is a timeout, and that
// wait is part of the case rather than something to be raced.
// Both waits are the ping giving up: before the press so the button's own state is settled, after
// it because that is when the run page is asked for. Reading either sooner is a race, not a check.
const missing = await press({ repo: 'acme/app', ref: 'preview', port: String(NOTHING) },
  { soak: 2500, settle: 3000 });

check('without QuickRun the press lands on the run page, not nowhere', () => {
  const to = missing.wanted.find((url) => url.startsWith('https://quickrun.org/run'));
  assert.ok(to, `it went to: ${missing.wanted.join(', ') || 'nothing'}, and saw ${JSON.stringify(missing.seen)}`);
  assert.match(to, /repo=acme%2Fapp/);
  assert.match(to, /ref=preview/);
  assert.match(to, /executeQuickRun=true/);
});

check('and the host says QuickRun is not there', () => {
  assert.equal(missing.look.running, false);
});

// 4. A page that wants its own question in front of the hand-over.
const stopped = await press({ repo: 'acme/app', port: String(port) }, { stop: true });

check('preventDefault stops the hand-over completely', () => {
  assert.equal(stopped.seen.find((e) => e.type === 'quickrun-handover'), undefined);
  assert.deepEqual(stopped.asked, [], 'it asked the daemon anyway');
  assert.deepEqual(stopped.wanted, [], 'it navigated anyway');
});

// 5. mode="link": for a page that would rather send people to quickrun.org than hand over.
const linked = await press({ repo: 'acme/app', mode: 'link', port: String(port) });

check('mode="link" goes to the run page and nowhere near the daemon', () => {
  const to = linked.wanted.find((url) => url.startsWith('https://quickrun.org/run'));
  assert.ok(to, `it went to: ${linked.wanted.join(', ') || 'nothing'}`);
  assert.doesNotMatch(to, /executeQuickRun/, 'a link is a link - it should not run on arrival');
  assert.deepEqual(linked.asked, []);
});

// 6. The looks: the icon on either side or gone, and the properties that dress it.
const right = await press({
  repo: 'acme/app',
  icon: 'right',
  port: String(port),
  style: '--quickrun-bg: rgb(31, 136, 61); --quickrun-radius: 999px',
});

check('the icon moves and the custom properties are the styling surface', () => {
  assert.equal(right.look.side, 'right');
  assert.equal(right.look.background, 'rgb(31, 136, 61)');
  assert.equal(right.look.radius, '999px');
});

const bare = await press({ repo: 'acme/app', icon: 'none', unstyled: '', port: String(port) });

check('icon="none" hides the mark and unstyled drops the stylesheet', () => {
  assert.equal(bare.look.iconShown, false);
  assert.equal(bare.look.styled, false, 'unstyled kept the bundled CSS');
});

check('a label attribute wins over the text, and the light DOM is never drawn twice', () => {
  assert.equal(bare.look.light, 'Run this');
});

const named = await press({ repo: 'acme/app', label: 'Try the demo', port: String(port) });

check('the label attribute is what the button says', () => {
  assert.equal(named.look.label, 'Try the demo');
});

const shotAt = process.argv.indexOf('--screenshot');

if (shotAt > 0) {
  await evaluate(`window.__put({ repo: 'acme/app', port: '${port}' })`);
  await wait(300);
  const { result } = await send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(process.argv[shotAt + 1], Buffer.from(result.result.data, 'base64'));
}

check('nothing threw while any of that happened', () => {
  const errors = thrown.filter((line) => !/ERR_|Failed to fetch|net::/.test(line));
  assert.deepEqual(errors, []);
});

console.log(checks.join('\n'));

socket.close();
browser.kill();
server.close();

console.log(process.exitCode ? '\ncomponents: something is wrong' : '\ncomponents: all good');
process.exit(process.exitCode ?? 0);
