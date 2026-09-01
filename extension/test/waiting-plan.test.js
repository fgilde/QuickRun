import { test } from 'node:test';
import assert from 'node:assert/strict';

/**
 * A plan that is waiting for a person, and what pressing the button again has to do about it.
 *
 * Reported as "no dialog opens any more, and the button says Running for ever". Three things had to
 * line up for it: the daemon kept a declined plan on its list as "awaiting confirmation", the
 * extension treats such a plan as a run in progress - correctly, it is one the user has to answer -
 * and reopening a run always opened the log view, which has no commands and no Run button. So every
 * press after the first landed on a window with nothing to press, and the plan could never be
 * answered or got rid of.
 *
 * The daemon side is tested in RunRegistryTests. This is the extension side: reopening a plan opens
 * the plan, and the answer from that window still starts the run.
 */

let instance = 0;

function sessionStore() {
  const data = new Map();

  return {
    async get(defaults) {
      const keys = typeof defaults === 'string' ? { [defaults]: undefined } : defaults;
      const out = {};
      for (const [key, fallback] of Object.entries(keys))
        out[key] = data.has(key) ? data.get(key) : fallback;
      return out;
    },
    async set(values) {
      for (const [key, value] of Object.entries(values)) data.set(key, value);
    },
    async remove(key) { data.delete(key); },
  };
}

/** One worker, over a session storage and a set of local settings. */
async function startWorker({ session, windows, local = {} }) {
  const listeners = [];

  globalThis.chrome = {
    runtime: {
      onMessage: { addListener: (fn) => listeners.push(fn), removeListener: () => {} },
      getURL: (path) => `chrome-extension://test/${path}`,
      sendMessage: async () => {},
    },
    storage: {
      session,
      local: { get: async (defaults) => ({ ...defaults, ...local }) },
    },
    windows: {
      create: async ({ url }) => {
        const created = { id: windows.next++, url };
        windows.opened.push(created);
        return created;
      },
      update: async () => ({}),
      onRemoved: { addListener: () => {}, removeListener: () => {} },
    },
    tabs: { create: async () => ({}), sendMessage: async () => {} },
  };

  await import(`../src/background.js?waiting=${instance++}`);

  return (message, sender = {}) =>
    new Promise((resolve) => { listeners[0](message, sender, resolve); });
}

/** The daemon, with one run in whatever state the test needs. */
function daemon(calls, { runId, state, probe }) {
  return async (url, options = {}) => {
    const { pathname, searchParams } = new URL(url);
    calls.push(`${options.method ?? 'GET'} ${pathname}`);

    if (pathname === '/api/ping') return respond({ product: 'QuickRun', version: '1.0.0' });
    if (pathname === '/api/probe') {
      calls.push(`probe ${searchParams.get('repo')}`);
      return respond(probe ?? { quickrun: false, pinokio: false, known: false });
    }
    if (pathname === '/api/runs') return respond([]);
    if (pathname === `/api/runs/${runId}/confirm`) return respond({ id: runId, state: 'running' });
    if (pathname.endsWith('/events'))
      return { ok: true, body: { getReader: () => ({ read: async () => ({ done: true }) }) } };

    // Every other lookup is "what is this run doing", which is the state under test.
    return respond({ id: runId, state, repo: 'a/b', commands: [{ command: 'echo hi' }] });
  };
}

const respond = (payload) => ({ ok: true, status: 200, text: async () => JSON.stringify(payload) });

async function withWorker(options, body) {
  const calls = [];
  const windows = { opened: [], next: 100 };
  const session = sessionStore();

  const previousFetch = globalThis.fetch;
  const previousChrome = globalThis.chrome;
  globalThis.fetch = daemon(calls, options);

  try {
    const send = await startWorker({ session, windows, local: options.local });
    await body({ send, calls, windows, session });
  } finally {
    globalThis.fetch = previousFetch;
    globalThis.chrome = previousChrome;
  }
}

test('reopening a plan opens the plan, not the log view', { timeout: 10000 }, async () => {
  await withWorker({ runId: 'plan-1', state: 'awaitingConfirmation' }, async ({ send, windows }) => {
    const answer = await send({ type: 'showLog', runId: 'plan-1' }, { tab: { id: 7 } });

    assert.equal(answer.ok, true);
    assert.equal(answer.waiting, true);

    const opened = windows.opened.at(-1).url;

    // The plan page, which has the commands and the Run button. ?attach=1 has neither, and that is
    // what every press used to land on.
    assert.match(opened, /confirm\.html$/);
    assert.doesNotMatch(opened, /attach/);
  });
});

test('a plan reopened that way can still be answered', { timeout: 10000 }, async () => {
  await withWorker({ runId: 'plan-2', state: 'awaitingConfirmation' }, async ({ send, calls }) => {
    await send({ type: 'showLog', runId: 'plan-2' }, { tab: { id: 7 } });

    // The window that just opened says yes. Without the reopened plan being recorded as pending,
    // this answer is discarded as "already decided" and nothing ever starts.
    const decided = await send({ type: 'confirmResult', runId: 'plan-2', approved: true });

    assert.deepEqual(decided, { ok: true, approved: true });
    assert.ok(calls.includes('POST /api/runs/plan-2/confirm'), calls.join(', '));
  });
});

test('a run that is going reopens as its log', { timeout: 10000 }, async () => {
  await withWorker({ runId: 'live-1', state: 'running' }, async ({ send, windows }) => {
    const answer = await send({ type: 'showLog', runId: 'live-1' }, { tab: { id: 7 } });

    assert.equal(answer.waiting, false);
    assert.match(windows.opened.at(-1).url, /attach=1/);
  });
});

/**
 * The setting that decides where the button appears.
 *
 * The message for it has been handled since the setting was added, and the function behind it was
 * never written - so every answer was a ReferenceError. The content script reads an unusable answer
 * as "show it", which is why nobody noticed: the button appeared everywhere, and 'known' and
 * 'quickrun' did nothing at all.
 */
test('the show-on setting is answered rather than throwing', { timeout: 10000 }, async () => {
  await withWorker({ runId: 'x', state: 'running', local: { showOn: 'always' } },
    async ({ send, calls }) => {
      assert.deepEqual(await send({ type: 'shouldShow', target: { repo: 'a/b' } }), { show: true });

      // Nothing was asked of the daemon: every repository shows a button, so there is nothing to ask.
      assert.ok(!calls.some((c) => c.startsWith('probe ')), calls.join(', '));
    });
});

test('only repositories with a config, when that is what was asked for', { timeout: 10000 }, async () => {
  await withWorker({ runId: 'x', state: 'running', local: { showOn: 'quickrun' },
                     probe: { quickrun: false, pinokio: true, known: true } },
    async ({ send, calls }) => {
      assert.deepEqual(await send({ type: 'shouldShow', target: { repo: 'a/b' } }), { show: false });
      assert.ok(calls.includes('probe a/b'), calls.join(', '));
    });

  await withWorker({ runId: 'x', state: 'running', local: { showOn: 'quickrun' },
                     probe: { quickrun: true, pinokio: false, known: true } },
    async ({ send }) => {
      assert.deepEqual(await send({ type: 'shouldShow', target: { repo: 'a/b' } }), { show: true });
    });
});

test('known means either kind of instructions', { timeout: 10000 }, async () => {
  await withWorker({ runId: 'x', state: 'running', local: { showOn: 'known' },
                     probe: { quickrun: false, pinokio: true, known: true } },
    async ({ send }) => {
      assert.deepEqual(await send({ type: 'shouldShow', target: { repo: 'a/b' } }), { show: true });
    });

  await withWorker({ runId: 'x', state: 'running', local: { showOn: 'known' },
                     probe: { quickrun: false, pinokio: false, known: false } },
    async ({ send }) => {
      assert.deepEqual(await send({ type: 'shouldShow', target: { repo: 'a/b' } }), { show: false });
    });
});

/**
 * Every message the worker handles has something to handle it.
 *
 * This is the check that would have caught the missing function on the day it went in: a case in the
 * switch whose function does not exist answers every caller with a ReferenceError, and the callers
 * here are written to carry on regardless - so it stays silent for months.
 */
test('no message answers with a ReferenceError', { timeout: 10000 }, async () => {
  const source = await import('node:fs').then((fs) =>
    fs.promises.readFile(new URL('../src/background.js', import.meta.url), 'utf8'));

  const handled = [...source.matchAll(/case '([a-zA-Z]+)':/g)].map((m) => m[1]);
  assert.ok(handled.length > 8, `only found ${handled.length} message types`);

  await withWorker({ runId: 'x', state: 'running' }, async ({ send }) => {
    for (const type of handled) {
      const answer = await send({ type, runId: 'x', target: { repo: 'a/b' }, values: {} },
        { tab: { id: 7 } });

      assert.ok(!/ReferenceError|is not a function/.test(JSON.stringify(answer ?? {})),
        `message '${type}' answered ${JSON.stringify(answer)}`);
    }
  });
});
