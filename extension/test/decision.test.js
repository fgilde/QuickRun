import { test } from 'node:test';
import assert from 'node:assert/strict';

/**
 * The confirmation has to survive the worker being shut down.
 *
 * This is the bug two people hit on two machines: the plan was on screen, the warning about running
 * commands with your privileges was read - which takes more than thirty seconds - and by the time
 * Run was pressed the service worker had been shut down for being idle. The answer was being
 * awaited in a promise inside that worker, so it went with it: the click reached a fresh worker
 * that had never heard of the run, the window said "Running", and nothing ran. It worked for
 * whoever clicked within thirty seconds, which is why it looked machine-dependent.
 *
 * So the test starts a run in one worker and answers in another, with only session storage in
 * common - which is exactly what a shutdown leaves behind.
 */

/** How many workers have been started, so each import is a new module instance. */
let started = 0;

/** Session storage as the browser keeps it: outlives the worker, not the browser. */
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
    async remove(key) {
      data.delete(key);
    },
  };
}

/** One worker's lifetime: the listeners it registers, over a session storage that outlives it. */
async function startWorker(session, calls, { windows }) {
  const listeners = { message: [], windowRemoved: [] };

  globalThis.chrome = {
    runtime: {
      onMessage: { addListener: (fn) => listeners.message.push(fn), removeListener: () => {} },
      getURL: (path) => `chrome-extension://test/${path}`,
      sendMessage: async () => {},
    },
    storage: {
      session,
      local: { get: async (defaults) => ({ ...defaults }) },
    },
    windows: {
      create: async ({ url }) => {
        const created = { id: windows.next++, url };
        windows.opened.push(created);
        return created;
      },
      update: async () => ({}),
      onRemoved: { addListener: (fn) => listeners.windowRemoved.push(fn), removeListener: () => {} },
    },
    tabs: { create: async () => ({}), sendMessage: async () => {} },
  };

  // A fresh module instance, because a restarted worker keeps nothing but storage. The query is
  // what makes it fresh, so it counts across the whole file rather than per test.
  await import(`../src/background.js?worker=${started++}`);

  return {
    send(message, sender = {}) {
      return new Promise((resolve) => {
        listeners.message[0](message, sender, resolve);
      });
    },
    closeWindow(id) {
      return Promise.all(listeners.windowRemoved.map((fn) => fn(id)));
    },
  };
}

/** The daemon, as far as the worker can tell. */
function daemon(calls, { runId = 'run-1' } = {}) {
  return async (url, options = {}) => {
    const path = new URL(url).pathname;
    calls.paths.push(`${options.method ?? 'GET'} ${path}`);

    if (path === '/api/run')
      return respond({ id: runId, state: 'awaitingConfirmation', repo: 'a/b', commands: ['x'] });

    if (path === `/api/runs/${runId}/confirm`) return respond({ id: runId, state: 'running' });
    if (path === `/api/runs/${runId}/stop`) return respond({ ok: true });

    // The stream ends immediately and the run reads as finished, so following it stops there.
    if (path.endsWith('/events'))
      return { ok: true, body: { getReader: () => ({ read: async () => ({ done: true }) }) } };

    return respond({ id: runId, state: 'succeeded' });
  };
}

function respond(payload) {
  return { ok: true, status: 200, text: async () => JSON.stringify(payload) };
}

async function withWorkers(body) {
  const calls = { paths: [] };
  const windows = { opened: [], next: 100 };
  const session = sessionStore();

  const previousFetch = globalThis.fetch;
  const previousChrome = globalThis.chrome;
  globalThis.fetch = daemon(calls);

  try {
    await body({ calls, windows, session, worker: () => startWorker(session, calls, { windows }) });
  } finally {
    globalThis.fetch = previousFetch;
    globalThis.chrome = previousChrome;
  }
}

// A deadline, because the failure this guards against is a promise that never settles.
test('a confirmation answered by a later worker still starts the run', { timeout: 10000 }, async () => {
  await withWorkers(async ({ calls, windows, worker }) => {
    const first = await worker();
    const started = await first.send({ type: 'run', target: { repo: 'a/b' } }, { tab: { id: 7 } });

    assert.equal(started.runId, 'run-1');
    assert.equal(started.state, 'awaitingConfirmation');
    assert.equal(windows.opened.length, 1, 'the plan is shown in its own window');
    assert.ok(!calls.paths.includes('POST /api/runs/run-1/confirm'),
      'nothing may start before someone says so');

    // Thirty-one seconds of reading later: a new worker, and everything the old one held is gone.
    const second = await worker();
    const decided = await second.send({ type: 'confirmResult', runId: 'run-1', approved: true });

    assert.equal(decided.error, undefined);
    assert.ok(calls.paths.includes('POST /api/runs/run-1/confirm'),
      `the run must actually be started - calls were ${calls.paths.join(', ')}`);
  });
});

test('closing the window without answering lets the run go', { timeout: 10000 }, async () => {
  await withWorkers(async ({ calls, windows, worker }) => {
    const first = await worker();
    await first.send({ type: 'run', target: { repo: 'a/b' } }, { tab: { id: 7 } });

    const second = await worker();
    await second.closeWindow(windows.opened[0].id);

    assert.ok(calls.paths.includes('POST /api/runs/run-1/stop'),
      'a closed window is a rejection, and the prepared run is released');
    assert.ok(!calls.paths.includes('POST /api/runs/run-1/confirm'),
      'silence must never mean approval');
  });
});

test('a second answer for the same run changes nothing', { timeout: 10000 }, async () => {
  await withWorkers(async ({ calls, worker }) => {
    const first = await worker();
    await first.send({ type: 'run', target: { repo: 'a/b' } }, { tab: { id: 7 } });

    await first.send({ type: 'confirmResult', runId: 'run-1', approved: true });
    await first.send({ type: 'confirmResult', runId: 'run-1', approved: false });

    assert.equal(calls.paths.filter((call) => call.endsWith('/confirm')).length, 1);
    assert.ok(!calls.paths.includes('POST /api/runs/run-1/stop'),
      'a run already going must not be stopped by a stale answer');
  });
});
