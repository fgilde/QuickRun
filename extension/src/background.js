// Owns every conversation with the daemon.
//

import * as api from './api.js';
import { matchesTarget, stillWorthActing } from './match.js';

const DOWNLOAD_PAGE = 'https://fgilde.github.io/QuickRun/download';

/** Live runs, keyed by run id, so the content script can be told where a run has got to. */
const active = new Map();

/** The log window per run, so it can be raised when the run has something to show. */
const logWindows = new Map();

chrome.runtime.onMessage.addListener((message, sender, respond) => {
  handle(message, sender)
    .then(respond)
    .catch((error) => respond({ error: String(error) }));
  return true; // keep the channel open for the async reply
});

async function handle(message, sender) {
  switch (message?.type) {
    case 'status':
      return status();
    case 'run':
      return startRun(message.target, sender?.tab?.id);
    case 'inputs':
      return supplyInputs(message.runId, message.values);
    case 'stop':
      return stopRun(message.runId);
    case 'runState':
      return runState(message.runId);
    case 'activeRun':
      return activeRun(message.target);
    case 'showLog':
      return showLog(message.runId, sender?.tab?.id);
    case 'reveal':
      return revealRun(message.runId);
    case 'openDownloads':
      await chrome.tabs.create({ url: DOWNLOAD_PAGE });
      return { ok: true };
    case 'bootstrapDaemon':
      return bootstrapDaemon();
    default:
      return { error: `unknown message ${message?.type}` };
  }
}

/** What the button and the popup should show. */
async function status() {
  const { port } = await api.settings();
  const ping = await api.ping(port);

  if (!ping.running) return { state: 'not-installed', port };

  return { state: 'ready', version: ping.version, busy: ping.busy, port };
}


/**
 * Tries to start an installed-but-stopped daemon. This is the single remaining job of the
 * quickrun:// scheme: the browser will not tell us whether a handler exists, so we attempt it and
 * find out from the next ping.
 */
async function bootstrapDaemon() {
  const { port, useProtocolFallback } = await api.settings();
  if (!useProtocolFallback) return { started: false, reason: 'protocol fallback disabled' };

  try {
    await chrome.tabs.create({ url: 'quickrun://open', active: false });
  } catch {
    return { started: false, reason: 'no handler' };
  }

  // Give the daemon a few seconds to come up, then check.
  for (let attempt = 0; attempt < 6; attempt += 1) {
    await sleep(700);
    const ping = await api.ping(port);
    if (ping.running) return { started: true };
  }

  return { started: false, reason: 'no answer after starting' };
}

async function startRun(target, tabId) {
  const { port } = await api.settings();

  const prepared = await api.prepare(target, { port });

  // A config whose inputs have no values is not a failure: it is a form to fill in, and the window
  // is where that happens - so the window opens with it instead of the click ending in an error.
  const needsInput = prepared.run?.state === 'awaitingInput';
  if (prepared.error && !needsInput) return { error: prepared.error };

  const run = prepared.run;

  // The command list is confirmed in an extension window, not in the page: a page can overlay a
  // convincing fake panel, and the user must never approve one set of commands while another runs.
  const approved = await confirmInWindow(run);
  if (!approved) return { cancelled: true };

  const started = await api.confirm(run.id, { port });
  if (started.error) return { error: started.error };

  follow(run.id, tabId, { port });
  return { runId: run.id, state: 'running' };
}

/** The values for a config's inputs, and the plan they produce. */
async function supplyInputs(runId, values) {
  const { port } = await api.settings();
  return api.supplyInputs(runId, values ?? {}, { port });
}

/**
 * The run as the daemon sees it, for a window that is waiting for something to finish.
 *
 * Also where a lost stream is picked up again: this worker keeps who-is-watching-what in memory, so
 * a restart forgets every run it was following. Anyone asking about a run that is still going and
 * has nobody on it gets a watcher attached again, which is enough to heal the common case - the log
 * window asks, and its own log starts moving again.
 */
async function runState(runId) {
  const { port } = await api.settings();
  const run = await api.state(runId, { port });

  if (run && !TERMINAL.includes(run.state) && !active.has(runId)) follow(runId, undefined, { port });

  return { run };
}

async function stopRun(runId) {
  const { port } = await api.settings();
  const stopped = await api.stop(runId, { port });
  return { ok: stopped };
}

/**
 * The run of this repository and ref that is still going, if there is one.
 *
 * This is what lets a button offer Stop after the page was reloaded: the tab has forgotten the run,
 * the daemon has not. A run that has finished but still owns processes counts as going - that is
 * exactly the case where stopping is still worth offering.
 */
async function activeRun(target) {
  if (!target?.repo) return { run: null };

  const { port } = await api.settings();
  const all = await api.runs({ port });

  const match = all
    .filter((run) => matchesTarget(run, target))
    .filter(stillWorthActing)
    .at(-1);

  if (!match) return { run: null };

  // Follow it, so the button gets progress even though this tab never started it.
  if (!active.has(match.id)) follow(match.id, undefined, { port });

  return {
    run: {
      id: match.id,
      state: match.state,
      url: match.url ?? match.tasks?.find((task) => task.url)?.url ?? null,
      leftovers: match.leftovers ?? 0,
      progress: match.progress ?? null,
    },
  };
}

/**
 * Brings the run's log window back. Closing that window does not stop the run, so getting it back
 * has to be possible - otherwise a run keeps going with nowhere to watch it.
 */
async function showLog(runId, tabId) {
  const existing = logWindows.get(runId);

  if (existing !== undefined) {
    const raised = await chrome.windows.update(existing, { focused: true, drawAttention: true })
      .then(() => true)
      .catch(() => false);
    if (raised) return { ok: true, reopened: false };
    logWindows.delete(runId);
  }

  const { port } = await api.settings();
  const run = await api.state(runId, { port });
  if (!run) return { error: 'that run is gone' };

  // The window opens attached to a run that is already going: no plan to approve, straight to the
  // log and a Stop.
  await chrome.storage.session.set({ attachedRun: run });

  const created = await chrome.windows.create({
    url: chrome.runtime.getURL('confirm.html?attach=1'),
    type: 'popup',
    width: 760,
    height: 720,
  });

  logWindows.set(runId, created.id);
  if (!active.has(runId)) follow(runId, tabId, { port });

  return { ok: true, reopened: true };
}

/** The browser cannot open a local folder itself, so the daemon does it. */
async function revealRun(runId) {
  const { port } = await api.settings();
  return { ok: await api.reveal(runId, { port }) };
}

/**
 * Opens confirm.html and resolves with the user's decision. The window is left open afterwards:
 * once approved it becomes the run's log view, which is where a hundred lines of build output
 * belong - not in a toolbar button.
 */
async function confirmInWindow(run) {
  await chrome.storage.session.set({ pendingRun: run });

  const created = await chrome.windows.create({
    url: chrome.runtime.getURL('confirm.html'),
    type: 'popup',
    width: 760,
    height: 720,
  });

  logWindows.set(run.id, created.id);

  return new Promise((resolve) => {
    const onMessage = (message, sender, respond) => {
      if (message?.type !== 'confirmResult' || message.runId !== run.id) return false;
      cleanup();
      respond({ ok: true });
      resolve(Boolean(message.approved));
      return true;
    };

    // A closed window is a rejection: silence must never mean approval.
    const onRemoved = (windowId) => {
      if (windowId !== created.id) return;
      cleanup();
      if (logWindows.get(run.id) === windowId) logWindows.delete(run.id);
      resolve(false);
    };

    function cleanup() {
      chrome.runtime.onMessage.removeListener(onMessage);
      chrome.windows.onRemoved.removeListener(onRemoved);
    }

    chrome.runtime.onMessage.addListener(onMessage);
    chrome.windows.onRemoved.addListener(onRemoved);
  });
}

const TERMINAL = ['succeeded', 'failed', 'cancelled'];

/**
 * Relays the run's events to the tab that started it, so the button can show progress.
 *
 * Reconnecting, because a stream that ended is not the same thing as a run that ended. This worker
 * is shut down after thirty seconds without traffic and everything reading the stream dies with it,
 * which is how a log window came to sit frozen at 85% while the build underneath went on for ten
 * more minutes. The daemon replays a run's history to a new subscriber, so nothing is lost.
 */
function follow(runId, tabId, connection) {
  const controller = new AbortController();
  active.set(runId, controller);

  (async () => {
    while (!controller.signal.aborted) {
      await api
        .streamEvents(runId, connection, (event) => notify(tabId, runId, event), controller.signal)
        .catch(() => {});

      if (controller.signal.aborted) break;

      const run = await api.state(runId, connection).catch(() => null);
      if (!run || TERMINAL.includes(run.state)) break;

      await sleep(1000);
    }

    active.delete(runId);
  })();
}

function notify(tabId, runId, event) {
  const message = { type: 'runEvent', runId, event };

  // The tab drives the button's progress; the confirmation window shows the full log. Either may
  // be gone, and a run must not care.
  if (tabId !== undefined) chrome.tabs.sendMessage(tabId, message).catch(() => {});
  chrome.runtime.sendMessage(message).catch(() => {});

  // A run the user has stopped watching should say so itself. The moment worth interrupting for is
  // the outcome, and the moment something became reachable - not every line of build output.
  if (event.kind === 'taskReady' || event.kind === 'finished' || event.kind === 'failed'
      || event.kind === 'cancelled')
    raiseLogWindow(runId);
}

function raiseLogWindow(runId) {
  const windowId = logWindows.get(runId);
  if (windowId === undefined) return;

  chrome.windows
    .update(windowId, { focused: true, drawAttention: true })
    .catch(() => logWindows.delete(runId));
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
