import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

/**
 * The wiring, not the parsing.
 *
 * parseAutorun being right proves nothing about whether anything acts on it - and this session has
 * twice shipped a page whose logic was correct and whose bootstrap never ran. So content.js is
 * actually executed here, with a DOM small enough to write by hand, and the test watches the one
 * thing that matters: which message reaches the background worker.
 *
 * That message is where a click ends too, so "the link does what the button does" is exactly the
 * assertion below. What happens after it - the confirmation window drawing the plan - is what
 * scripts/drive-confirm.mjs covers in a real browser.
 */

const source = readFileSync(fileURLToPath(new URL('../src/content.js', import.meta.url)), 'utf8');
const targetsSource = readFileSync(fileURLToPath(new URL('../src/targets.js', import.meta.url)), 'utf8');

/** A DOM element with the handful of things content.js touches, and nothing else. */
function element(tag) {
  const node = {
    tagName: tag,
    children: [],
    dataset: {},
    style: { setProperty() {} },
    classList: { add() {}, remove() {} },
    append(...kids) { node.children.push(...kids); },
    appendChild(kid) { node.children.push(kid); return kid; },
    addEventListener() {},
    remove() {},
    // A fresh anchor never already holds a button; that is what inject() checks for.
    querySelector: () => null,
  };
  return node;
}

/** Runs content.js against one address and reports every message it sent. */
function inject(search, { times = 1, path = '/acme/app' } = {}) {
  const sent = [];
  const anchor = element('div');

  const sandbox = {
    console,
    URLSearchParams,              // a browser global; a fresh vm context has none
    setTimeout: () => 0,          // schedule() must not run inject() behind the test's back
    clearTimeout: () => {},
    location: {
      pathname: path,
      search,
      href: `https://github.com/acme/app${search}`,
    },
    MutationObserver: class { observe() {} },
    document: {
      body: element('body'),
      createElement: element,
      querySelector: () => null,
      addEventListener() {},
    },
    chrome: {
      runtime: {
        getManifest: () => ({ version: '0.0.0-test' }),
        getURL: (path) => `chrome-extension://test/${path}`,
        onMessage: { addListener() {} },
        sendMessage(message) {
          sent.push(message);

          switch (message.type) {
            case 'status': return Promise.resolve({ state: 'ready' });
            case 'shouldShow': return Promise.resolve({ show: true });
            case 'activeRun': return Promise.resolve({ run: null });
            case 'run': return Promise.resolve({ runId: 'r1', state: 'awaitingConfirmation' });
            default: return Promise.resolve({});
          }
        },
      },
    },
  };

  sandbox.globalThis = sandbox;
  vm.createContext(sandbox);

  // targets.js first, the way the manifest loads it; placement is stubbed so no page markup is
  // needed to find out where a button would go.
  vm.runInContext(targetsSource, sandbox);
  sandbox.QuickRunPlacement = {
    repoToolbar: () => anchor,
    pullRequestActions: () => anchor,
    branchRows: () => [
      { ref: 'first', anchor: element('div') },
      { ref: 'second', anchor: element('div') },
    ],
  };
  vm.runInContext(source, sandbox);

  return (async () => {
    for (let i = 0; i < times; i += 1) await sandbox.inject();

    // inject() does not await the autorun - it must not hold up the other buttons - so the run it
    // asks for arrives a few microtasks later. Settle until nothing new turns up.
    for (let i = 0; i < 20; i += 1) {
      const before = sent.length;
      await new Promise((done) => setImmediate(done));
      if (sent.length === before) break;
    }

    return sent;
  })();
}

const runsOf = (sent) => sent.filter((m) => m.type === 'run');

test('an ordinary page prepares nothing', async () => {
  const sent = await inject('');
  assert.equal(runsOf(sent).length, 0);

  // It did inject a button, though - this is not a test that silently did nothing.
  assert.ok(sent.some((m) => m.type === 'status'));
});

test('?executeQuickRun asks for the same run a click asks for', async () => {
  const sent = await inject('?executeQuickRun');
  const runs = runsOf(sent);

  assert.equal(runs.length, 1);

  // Field by field, not deepEqual: this object was made in the vm context, so its prototype is a
  // different Object and a strict deep comparison refuses it however equal the contents are.
  assert.equal(runs[0].target.repo, 'acme/app');
  assert.equal(runs[0].target.ref, null);
  assert.equal(runs[0].target.pr, null);
  assert.equal(runs[0].config, null);
});

test('a named config travels with it', async () => {
  const runs = runsOf(await inject('?executeQuickRun=ci/demo.yml'));

  assert.equal(runs.length, 1);
  assert.equal(runs[0].config, 'ci/demo.yml');
});

test('a refused config prepares nothing at all', async () => {
  for (const value of ['../secrets.yml', '/etc/x.yml', 'notes.txt']) {
    const runs = runsOf(await inject(`?executeQuickRun=${encodeURIComponent(value)}`));
    assert.equal(runs.length, 0, `${value} should not have started a run`);
  }
});

test('GitHub rendering the page again does not prepare a second run', async () => {
  // inject() runs on every mutation and on every Turbo navigation, which on a repository page is
  // constantly. Without the guard this is where a link would prepare runs in a loop.
  const runs = runsOf(await inject('?executeQuickRun', { times: 3 }));
  assert.equal(runs.length, 1);
});

test('switched off is the same as absent', async () => {
  assert.equal(runsOf(await inject('?executeQuickRun=false')).length, 0);
});

test('a link to a branch prepares that branch', async () => {
  const runs = runsOf(await inject('?executeQuickRun', { path: '/acme/app/tree/preview' }));

  assert.equal(runs.length, 1);
  assert.equal(runs[0].target.repo, 'acme/app');
  assert.equal(runs[0].target.ref, 'preview');
  assert.equal(runs[0].target.pr, null);
});

test('a branch with slashes in its name survives', async () => {
  const runs = runsOf(await inject('?executeQuickRun', { path: '/acme/app/tree/feature/deep/name' }));

  assert.equal(runs.length, 1);
  assert.equal(runs[0].target.ref, 'feature/deep/name');
});

test('a link to a pull request prepares that pull request', async () => {
  const runs = runsOf(await inject('?executeQuickRun', { path: '/acme/app/pull/42' }));

  assert.equal(runs.length, 1);
  assert.equal(runs[0].target.repo, 'acme/app');
  assert.equal(runs[0].target.pr, 42);
  assert.equal(runs[0].target.ref, null);
});

test('a branch or a pull request can name its own config', async () => {
  const branch = runsOf(await inject('?executeQuickRun=ci/demo.yml', { path: '/acme/app/tree/preview' }));
  assert.equal(branch.length, 1);
  assert.equal(branch[0].target.ref, 'preview');
  assert.equal(branch[0].config, 'ci/demo.yml');

  const pull = runsOf(await inject('?executeQuickRun=ci/demo.yml', { path: '/acme/app/pull/42' }));
  assert.equal(pull.length, 1);
  assert.equal(pull[0].target.pr, 42);
  assert.equal(pull[0].config, 'ci/demo.yml');
});

test('a branch list does not run whichever branch happens to be first', async () => {
  // Every row on /branches gets a button. An address can mean "run this repository"; it cannot
  // mean "run whichever branch this list put at the top".
  const runs = runsOf(await inject('?executeQuickRun', { path: '/acme/app/branches' }));
  assert.equal(runs.length, 0);
});
