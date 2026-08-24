// Owns every conversation with the daemon.
//

import * as api from './api.js';

const DOWNLOAD_PAGE = 'https://fgilde.github.io/QuickRun/download';

/** Live runs, keyed by run id, so the content script can be told where a run has got to. */
const active = new Map();

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
    case 'stop':
      return stopRun(message.runId);
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
  if (prepared.error) return { error: prepared.error };

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

async function stopRun(runId) {
  const { port } = await api.settings();
  const stopped = await api.stop(runId, { port });
  return { ok: stopped };
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

/** Relays the run's events to the tab that started it, so the button can show progress. */
function follow(runId, tabId, connection) {
  const controller = new AbortController();
  active.set(runId, controller);

  api
    .streamEvents(runId, connection, (event) => notify(tabId, runId, event), controller.signal)
    .catch(() => {})
    .finally(() => active.delete(runId));
}

function notify(tabId, runId, event) {
  const message = { type: 'runEvent', runId, event };

  // The tab drives the button's progress; the confirmation window shows the full log. Either may
  // be gone, and a run must not care.
  if (tabId !== undefined) chrome.tabs.sendMessage(tabId, message).catch(() => {});
  chrome.runtime.sendMessage(message).catch(() => {});
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
