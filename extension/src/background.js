// Owns every conversation with the daemon.
//

import * as api from './api.js';
import { matchesTarget, stillWorthActing } from './match.js';

const DOWNLOAD_PAGE = 'https://quickrun.org/download';

/** Live runs, keyed by run id, so the content script can be told where a run has got to. */
const active = new Map();

/**
 * What has to outlive this worker.
 *
 * A service worker is shut down after thirty seconds without an event, and reading a command list
 * takes longer than that. So nothing that a decision depends on may live in a variable here: the
 * window that comes back with "yes" thirty-one seconds later must find a worker that still knows
 * what to do with it. Session storage is cleared when the browser closes, which is exactly the
 * lifetime these two want.
 *
 * `pendingRuns`: run id to { tabId, windowId } while its window is asking.
 * `runWindows`:  run id to the window showing it, so the window can be raised rather than duplicated.
 */
const PENDING = 'pendingRuns';
const WINDOWS = 'runWindows';

async function remembered(key) {
  const stored = await chrome.storage.session.get({ [key]: {} });
  return stored[key] ?? {};
}

async function remember(key, change) {
  const all = await remembered(key);
  change(all);
  await chrome.storage.session.set({ [key]: all });
}

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
      return startRun(message.target, sender?.tab?.id, message.config ?? null);
    case 'confirmResult':
      return decideRun(message.runId, Boolean(message.approved));
    case 'inputs':
      return supplyInputs(message.runId, message.values);
    case 'stop':
      return stopRun(message.runId);
    case 'runState':
      return runState(message.runId);
    case 'activeRun':
      return activeRun(message.target);
    case 'shouldShow':
      return shouldShow(message.target);
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

async function startRun(target, tabId, config = null) {
  const { port } = await api.settings();

  // A named config is added only when there is one, so a run without one sends the same request it
  // always sent. The daemon checks the name again - it arrives from a web address.
  const prepared = await api.prepare(config ? { ...target, config } : target, { port });

  // A config whose inputs have no values is not a failure: it is a form to fill in, and the window
  // is where that happens - so the window opens with it instead of the click ending in an error.
  const needsInput = prepared.run?.state === 'awaitingInput';
  if (prepared.error && !needsInput) return { error: prepared.error };

  const run = prepared.run;

  // The command list is confirmed in an extension window, not in the page: a page can overlay a
  // convincing fake panel, and the user must never approve one set of commands while another runs.
  //
  // Nothing is awaited here. Waiting for the answer was the bug: this worker is shut down thirty
  // seconds after the last event, and a plan people actually read takes longer than that - so the
  // promise holding the answer died with the worker, the window's Run button pressed into nothing,
  // and the run sat waiting for a confirmation that could no longer arrive. The answer comes back
  // as its own message instead, to whichever worker is alive by then.
  const windowId = await openWindow(run, 'confirm.html');
  await remember(PENDING, (all) => { all[run.id] = { tabId: tabId ?? null, windowId }; });

  return { runId: run.id, state: 'awaitingConfirmation' };
}

/**
 * The answer from the confirmation window: start the run, or let it go.
 *
 * Everything it needs is either in the message or in session storage, so it works just as well in a
 * worker that was started by this very message and knows nothing else.
 */
async function decideRun(runId, approved) {
  if (!runId) return { error: 'no run to decide about' };

  const { port } = await api.settings();

  const pending = (await remembered(PENDING))[runId] ?? null;
  await remember(PENDING, (all) => { delete all[runId]; });

  // Already decided - a second answer for the same run changes nothing.
  if (!pending) return { ok: true, ignored: true };

  const tabId = pending.tabId ?? undefined;

  if (!approved) {
    // The daemon is holding a prepared run that nobody wants. Stopping it releases it.
    await api.stop(runId, { port });
    notify(tabId, runId, { kind: 'cancelled', text: 'not confirmed' });
    return { ok: true, approved: false };
  }

  const started = await api.confirm(runId, { port });

  if (started.error) {
    notify(tabId, runId, { kind: 'failed', text: started.error });
    return { error: started.error };
  }

  follow(runId, tabId, { port });
  return { ok: true, approved: true };
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
  const existing = (await remembered(WINDOWS))[runId];

  if (existing !== undefined) {
    const raised = await chrome.windows.update(existing, { focused: true, drawAttention: true })
      .then(() => true)
      .catch(() => false);
    if (raised) return { ok: true, reopened: false };
    await remember(WINDOWS, (all) => { delete all[runId]; });
  }

  const { port } = await api.settings();
  const run = await api.state(runId, { port });
  if (!run) return { error: 'that run is gone' };

  // The window opens attached to a run that is already going: no plan to approve, straight to the
  // log and a Stop.
  await openWindow(run, 'confirm.html?attach=1');
  if (!active.has(runId)) follow(runId, tabId, { port });

  return { ok: true, reopened: true };
}

/** The browser cannot open a local folder itself, so the daemon does it. */
async function revealRun(runId) {
  const { port } = await api.settings();
  return { ok: await api.reveal(runId, { port }) };
}

/**
 * Opens the window for a run: the plan to approve, or - with `?attach=1` - the log of one that is
 * already going. It stays open after approval and becomes the run's log view, which is where a
 * hundred lines of build output belong, not in a toolbar button.
 */
async function openWindow(run, page) {
  await chrome.storage.session.set(page.includes('attach') ? { attachedRun: run } : { pendingRun: run });

  const created = await chrome.windows.create({
    url: chrome.runtime.getURL(page),
    type: 'popup',
    width: 760,
    height: 720,
  });

  await remember(WINDOWS, (all) => { all[run.id] = created.id; });

  return created.id;
}

/**
 * A closed window is a rejection: silence must never mean approval.
 *
 * Closing a window wakes this worker whether or not it was running, which is the point - the
 * decision is looked up rather than remembered.
 */
// The promise is returned rather than dropped: the browser ignores it, a test can wait for it.
chrome.windows.onRemoved.addListener((windowId) => windowClosed(windowId));

async function windowClosed(windowId) {
  const windows = await remembered(WINDOWS);
  const runId = Object.keys(windows).find((id) => windows[id] === windowId);
  if (!runId) return;

  await remember(WINDOWS, (all) => { delete all[runId]; });

  // Only a run still waiting for an answer is affected; one that was approved keeps going, which is
  // what closing its log window has always meant.
  if ((await remembered(PENDING))[runId]) await decideRun(runId, false);
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
    void raiseLogWindow(runId);
}

async function raiseLogWindow(runId) {
  const windowId = (await remembered(WINDOWS))[runId];
  if (windowId === undefined) return;

  await chrome.windows
    .update(windowId, { focused: true, drawAttention: true })
    .catch(() => remember(WINDOWS, (all) => { delete all[runId]; }));
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
