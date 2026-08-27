// The confirmation gate, and afterwards the run's log.
//
// It runs in an extension window, which a web page cannot overlay: the user must see the real
// command list, not one a page drew on top of it. After approval the window stays open and streams
// everything QuickRun does, so the log has somewhere to live that is not a 200px toolbar button.

import { httpUrl } from './safeurl.js';
import { inputForm } from './inputs.js';
import { baseUrl, settings } from './api.js';

// Two ways in: a plan waiting to be approved, or a run that is already going and wants watching
// again. Closing this window never stopped the run, so getting back to it has to be possible.
const attaching = new URLSearchParams(location.search).get('attach') === '1';
const stored = await chrome.storage.session.get(attaching ? 'attachedRun' : 'pendingRun');
const pendingRun = attaching ? stored.attachedRun : stored.pendingRun;

// The run is replaced when values are supplied: the plan is rebuilt from them, and what gets
// approved has to be the rebuilt one.
let run = pendingRun;
let form = null;

const review = document.getElementById('review');
const progress = document.getElementById('progress');
const log = document.getElementById('log');
const banner = document.getElementById('banner');
const approve = document.getElementById('approve');
const cancel = document.getElementById('cancel');
const stop = document.getElementById('stop');
const close = document.getElementById('close');

let decided = false;
let errorLines = 0;

/** What each task is doing, by name, in the order they first appeared. */
const tasks = new Map();

// What "Stop" would actually stop. Tasks report when they start and when they exit, so a run whose
// processes have all gone gets a disabled button instead of one that does nothing.
let liveTasks = 0;
let sawTask = false;

if (!run) {
  document.getElementById('name').textContent = 'Nothing to confirm';
  approve.hidden = true;
} else if (attaching) {
  render(run);
  attach(run);
  replay(run.id);
} else {
  render(run);
}

/**
 * Reads the run's event stream directly, from the beginning.
 *
 * A window opened on a run that is already going used to start with an empty log: the extension's
 * worker had been streaming since the run started and only passes on what arrives next, so
 * everything before this window existed was simply missing. The daemon replays a run's history to
 * every new subscriber - so this window subscribes for itself and gets the log it came for.
 *
 * While this is on, the messages relayed by the worker are ignored, or every line would arrive
 * twice.
 */
async function replay(runId) {
  const { port } = await settings();
  const source = new EventSource(`${baseUrl(port)}/api/runs/${runId}/events`);

  ownStream = true;

  source.onmessage = (message) => {
    try {
      consume(JSON.parse(message.data));
    } catch {
      // A frame that is not an event is not worth taking the window down for.
    }
  };

  source.onerror = () => {
    // The run is over, or the daemon went away. Either way the poll in keepWatching() is what
    // decides how this window ends, and the relayed messages are welcome again.
    source.close();
    ownStream = false;
  };
}

/**
 * Takes over a run that is already going: nothing to approve, so the window goes straight to the
 * log, with a Stop that means it. Everything since the run started is replayed by the daemon, so
 * the log is not empty just because this window is new.
 */
function attach(current) {
  decided = true;
  document.getElementById('name').textContent = current.displayName || current.repo;
  review.hidden = true;
  progress.hidden = false;
  approve.hidden = true;
  cancel.hidden = true;
  close.hidden = false;

  const alive = ['running', 'stopping'].includes(current.state) || (current.leftovers ?? 0) > 0;
  stop.hidden = !alive;
  liveTasks = current.liveTasks ?? 0;
  sawTask = true;

  for (const task of current.tasks ?? []) {
    tasks.set(task.name, { state: task.state, url: task.url ?? null, pid: task.pid ?? null });
  }
  renderTasks();

  if (current.progress) {
    document.getElementById('fill').style.width = `${current.progress.percent}%`;
    document.getElementById('percent').textContent = `${current.progress.percent}%`;
    document.getElementById('phase').textContent = current.progress.detail || current.progress.phase;
  }

  if (current.url) showAddress(current.url);

  if (alive) {
    setState(current.state === 'stopping' ? 'Stopping' : 'Running', 'running',
      { busy: current.state === 'stopping' });
    keepWatching();
  } else {
    conclude(current.state === 'succeeded' ? 'finished'
      : current.state === 'failed' ? 'failed' : 'cancelled');
  }
}

/**
 * What is missing on this machine and would be installed before anything runs.
 *
 * Installing a runtime changes the machine, so it belongs next to the command list rather than in
 * the log where it would only be read afterwards.
 */
function showProvisions(run) {
  const list = document.getElementById('provisions');
  const lines = run.provisions ?? [];

  list.textContent = '';
  for (const line of lines) {
    const item = document.createElement('li');
    // textContent: the tool name comes from the repository's own config.
    item.textContent = line;
    list.append(item);
  }

  document.getElementById('provisionSection').hidden = lines.length === 0;
}

function render(run) {
  text('name', run.displayName || run.repo);
  text('subtitle', run.commands.length === 0
    ? 'fill in the values below to see what would run'
    : `${run.commands.length} command(s)`);
  text('repo', run.repo);
  text('ref', run.ref);
  text('commit', run.commit ? run.commit.slice(0, 10) : 'unknown');
  showOrigin(run);
  showProvisions(run);
  text('dir', run.workspace ?? '');

  // What the repository says it is, when its config says anything. textContent: this is the
  // repository's own text.
  if (run.description) {
    const description = document.getElementById('description');
    description.textContent = run.description;
    description.hidden = false;
  }

  if (run.url) showAddress(run.url);

  renderInputs(run);
  renderCommands(run);
}

/**
 * Who wrote the commands about to be approved.
 *
 * A quickrun.yml the repository committed, a config saved on this machine, scripts written for
 * another launcher, or QuickRun's own reading of the files - these are four different promises, and
 * the one that involves guessing is the one worth knowing about before clicking Run.
 */
const ORIGINS = {
  repository: ["the repository's quickrun.yml", ''],
  local: ['your own config for this repository', 'saved on this machine, and it wins over the one the repository ships'],
  supplied: ['the config you supplied', 'not the one in the repository'],
  explicit: ['the config file you named', ''],
  foreign: ["this repository's Pinokio scripts", 'written for another launcher, read by QuickRun'],
  detected: ['QuickRun, from reading the repository', 'there is no config here, so these commands are a considered guess'],
};

function showOrigin(run) {
  const cell = document.getElementById('origin');
  const [what, detail] = ORIGINS[run.origin] ?? ORIGINS.repository;

  cell.textContent = '';
  cell.append(document.createTextNode(what));

  if (detail) {
    const note = document.createElement('span');
    note.className = 'muted';
    note.textContent = ` - ${detail}`;
    cell.append(note);
  }

  // Guessed commands are the case where reading the list below actually matters.
  cell.classList.toggle('origin--guessed', run.origin === 'detected');
}

/** The form the config asks for, and the button that applies it. */
function renderInputs(run) {
  const section = document.getElementById('inputSection');
  form = inputForm(run);

  if (!form) {
    section.hidden = true;
    return;
  }

  const host = document.getElementById('inputs');
  host.textContent = '';
  host.append(form.element);
  section.hidden = false;

  // The error from the daemon names the fields that are missing, which is exactly what to show.
  text('inputError', run.state === 'awaitingInput' ? run.error ?? '' : '');
  updateApprove();
}

function renderCommands(run) {
  const list = document.getElementById('commands');
  list.textContent = '';

  document.getElementById('commandsHeading').hidden = run.commands.length === 0;

  for (const command of run.commands) {
    const item = document.createElement('li');

    const phase = document.createElement('span');
    phase.className = 'phase';
    phase.textContent = command.phase;

    // textContent, never innerHTML: a command string is untrusted repository content.
    const code = document.createElement('code');
    code.textContent = command.command + (command.cwd ? `   (in ${command.cwd})` : '');

    item.append(phase, code);
    list.append(item);
  }
}

/**
 * What the button does next.
 *
 * Values are applied on their own a moment after they change, so the plan on screen is the plan
 * those values produce and the button stays "Run". It only says "Continue" while there is no plan
 * at all - required values still missing.
 */
function updateApprove() {
  approve.textContent = run.commands.length === 0 ? 'Continue' : 'Run';
}

let applyTimer = null;

/** Applies a changed value by itself, so approving stays one click. */
function scheduleApply() {
  updateApprove();

  if (applyTimer) clearTimeout(applyTimer);
  applyTimer = setTimeout(() => {
    applyTimer = null;
    if (form?.dirty()) applyValues();
  }, 600);
}

function text(id, value) {
  document.getElementById(id).textContent = value ?? '';
}

approve.addEventListener('click', async () => {
  // A value changed within the last moment has not been applied yet, so it is applied now - and if
  // it turns out to change nothing about the commands on screen, this click still starts the run.
  if (run.state === 'awaitingInput' || (form?.dirty() ?? false)) {
    const before = run.fingerprint;
    const commands = run.commands.length;

    await applyValues();

    const same = run.fingerprint === before && run.commands.length === commands;
    if (!same || run.commands.length === 0) return;
  }

  decide(true);
});

/** Sends the values, then shows the plan they produced. */
async function applyValues() {
  approve.disabled = true;
  text('inputError', 'applying…');

  const result = await chrome.runtime.sendMessage({
    type: 'inputs',
    runId: run.id,
    values: form?.values() ?? {},
  });

  approve.disabled = false;

  // Only the plan is redrawn, never the form: the field being typed in has to keep the cursor.
  if (result?.run) {
    run = result.run;
    form?.settle();
    text('subtitle', run.commands.length === 0
      ? 'fill in the values below to see what would run'
      : `${run.commands.length} command(s)`);
    renderCommands(run);
  }

  if (result?.error) {
    text('inputError', result.error);
    updateApprove();
    return;
  }

  text('inputError', run.commands.length > 0
    ? 'this is what those values would run'
    : '');
  updateApprove();
}

document.getElementById('inputs').addEventListener('input', scheduleApply);
document.getElementById('inputs').addEventListener('change', scheduleApply);
cancel.addEventListener('click', () => decide(false));
close.addEventListener('click', () => window.close());

// A page cannot open a local folder, and neither can an extension window: the daemon does it,
// and only for a path it already holds for this run.
document.getElementById('openDir').addEventListener('click', async () => {
  await chrome.runtime.sendMessage({ type: 'reveal', runId: run?.id });
});

let stopping = false;
let finished = false;

stop.addEventListener('click', async () => {
  stopping = true;
  stop.disabled = true;
  stop.textContent = 'Stopping…';

  // Stopping takes as long as the processes take to go, so the banner has to show that something
  // is happening rather than sit on "Running" until it is over.
  setState('Stopping', 'running', { busy: true });
  await chrome.runtime.sendMessage({ type: 'stop', runId: run.id });

  // And then it has to end. An event can be missed - a dropped stream, a window opened late - so
  // the run is asked directly rather than waited on.
  watchUntilDone();
});

const TERMINAL = ['succeeded', 'failed', 'cancelled'];

/**
 * Asks the daemon how the run is doing, for as long as it is going.
 *
 * The event stream is the fast path, never the source of truth: it can be cut - the extension's
 * service worker is shut down when nothing is flowing through it, which a quiet ten-minute build
 * does reliably - and a window that only listened would then sit on the last percentage it heard
 * for ever. Asking is also what wakes the worker, which reconnects the stream.
 */
let watching = null;

function keepWatching() {
  if (watching !== null) return;

  watching = setInterval(async () => {
    if (finished) { clearInterval(watching); watching = null; return; }

    const answer = await chrome.runtime.sendMessage({ type: 'runState', runId: run.id })
      .catch(() => null);
    const current = answer?.run;
    if (!current) return;

    // The bar comes from the daemon's own idea of the run, so a window that missed events while it
    // was not being told anything catches up rather than staying behind.
    if (current.progress) {
      document.getElementById('fill').style.width = `${current.progress.percent}%`;
      document.getElementById('percent').textContent = `${current.progress.percent}%`;
      document.getElementById('phase').textContent = current.progress.detail || current.progress.phase;
    }

    for (const task of current.tasks ?? []) {
      tasks.set(task.name, { state: task.state, url: task.url ?? null, pid: task.pid ?? null });
    }
    renderTasks();

    if (current.url) showAddress(current.url);

    if (TERMINAL.includes(current.state) && (current.leftovers ?? 0) === 0) {
      clearInterval(watching);
      watching = null;
      conclude(current.state === 'succeeded' ? 'finished'
        : current.state === 'failed' ? 'failed' : 'cancelled');
    }
  }, 5000);
}

async function watchUntilDone() {
  const giveUpAt = Date.now() + 60_000;

  while (!finished && Date.now() < giveUpAt) {
    await new Promise((resume) => setTimeout(resume, 1000));
    if (finished) return;

    const answer = await chrome.runtime.sendMessage({ type: 'runState', runId: run.id })
      .catch(() => null);
    const state = answer?.run?.state;

    if (state && TERMINAL.includes(state)) {
      // A task that launches something in the background and exits leaves it running, so a run can
      // read as finished with its processes still there. Stop again, and only then call it stopped.
      if ((answer?.run?.leftovers ?? 0) > 0) {
        append(`${answer.run.leftovers} process(es) are still running - ending them`, 'err');
        await chrome.runtime.sendMessage({ type: 'stop', runId: run.id }).catch(() => null);
        continue;
      }

      conclude(state === 'succeeded' ? 'finished' : state === 'failed' ? 'failed' : 'cancelled');
      return;
    }
  }

  if (finished) return;

  // Something is refusing to die. Saying so beats a spinner that never stops.
  setState('Still stopping', 'warn');
  append('the run has not finished stopping - closing this window leaves it running', 'err');
  close.hidden = false;
}

async function decide(approved) {
  if (decided) return;
  decided = true;
  approve.disabled = true;

  const answer = await chrome.runtime
    .sendMessage({ type: 'confirmResult', runId: run?.id, approved })
    .catch((error) => ({ error: String(error) }));

  if (!approved) {
    await chrome.storage.session.remove('pendingRun');
    window.close();
    return;
  }

  // A window that says "Running" over a run that never started is how this hid for two releases:
  // it looked busy while nothing at all was happening. If the start did not happen, the plan stays
  // on screen with the reason, and the button can be pressed again.
  if (answer?.error) {
    decided = false;
    approve.disabled = false;
    const failure = document.getElementById('startError');
    failure.textContent = answer.error;
    failure.hidden = false;
    return;
  }

  await chrome.storage.session.remove('pendingRun');

  // Hand the window over to the run.
  document.getElementById('name').textContent = run.displayName || run.repo;
  review.hidden = true;
  progress.hidden = false;
  approve.hidden = true;
  cancel.hidden = true;
  stop.hidden = false;
  setState('Running', 'running');
  keepWatching();

  // Its own stream, rather than lines relayed by the worker: the worker is shut down whenever it
  // goes quiet, and this window should not go quiet with it.
  replay(run.id);
}

/**
 * The outcome, said plainly. "finished" with twenty error lines behind it is not the same thing as
 * "finished", so the count travels with it.
 */
function setState(label, kind, { busy = false } = {}) {
  const state = document.getElementById('state');
  state.textContent = label;
  banner.className = `banner banner--${kind}`;

  const spinner = document.getElementById('spinner');
  if (busy && !spinner) {
    const mark = document.createElement('span');
    mark.id = 'spinner';
    mark.className = 'spinner';
    banner.prepend(mark);
  } else if (!busy) {
    spinner?.remove();
  }

  const errors = document.getElementById('errors');
  errors.hidden = errorLines === 0;
  errors.textContent = errorLines === 1 ? '1 error line' : `${errorLines} error lines`;
}

/**
 * Shows where the app ended up listening.
 *
 * The URL comes from the repository's own config, so the scheme is checked before it becomes a
 * link: anything but http or https is shown as text and never made clickable.
 */
function showAddress(url) {
  const target = document.getElementById('address');
  const safe = httpUrl(url);

  target.textContent = '';

  if (!safe) {
    target.textContent = url;
    return;
  }

  const link = document.createElement('a');
  link.href = safe;
  link.target = '_blank';
  link.rel = 'noreferrer';
  link.textContent = safe;
  target.append(link);
}

/**
 * One line per task: what it is doing and where it is listening.
 *
 * Built with DOM calls because a task name comes out of someone's config, and the address is only a
 * link when it is http or https - the same rule the summary line follows.
 */
function renderTasks() {
  const host = document.getElementById('tasks');
  host.textContent = '';

  for (const [name, task] of tasks) {
    const row = document.createElement('div');
    row.className = 'task';

    const label = document.createElement('span');
    label.className = 'name';
    label.textContent = name;

    const state = document.createElement('span');
    state.className = `state ${{ ready: 'ok', starting: 'warn' }[task.state] ?? ''}`;
    state.textContent = task.state;

    row.append(label, state);

    if (task.pid) {
      const pid = document.createElement('span');
      pid.className = 'pid';
      pid.textContent = `pid ${task.pid}`;
      row.append(pid);
    }

    if (task.url) {
      const safe = httpUrl(task.url);
      const address = document.createElement(safe ? 'a' : 'span');
      address.textContent = safe ?? task.url;
      if (safe) {
        address.href = safe;
        address.target = '_blank';
        address.rel = 'noreferrer';
      }
      row.append(address);
    }

    host.append(row);
  }
}

/** Stoppable only while something is actually running. */
function updateStop() {
  stop.disabled = sawTask && liveTasks === 0;
  stop.title = stop.disabled ? 'nothing is running any more' : '';
}

/**
 * True while this window reads the daemon's stream itself, in which case the same events relayed by
 * the extension's worker are duplicates.
 */
let ownStream = false;

chrome.runtime.onMessage.addListener((message) => {
  if (message?.type !== 'runEvent' || message.runId !== run?.id || ownStream) return;
  consume(message.event);
});

/** One event, from wherever it came. */
function consume(event) {
  if (event.progress) {
    document.getElementById('fill').style.width = `${event.progress.percent}%`;
    document.getElementById('percent').textContent = `${event.progress.percent}%`;
    document.getElementById('phase').textContent = event.progress.detail || event.progress.phase;
    append(event.progress.detail, 'meta');
    return;
  }

  // The runner announces the address it would have opened as an ordinary log line.
  if (event.kind === 'info' && event.text.startsWith('open ')) showAddress(event.text.slice(5).trim());

  // Per task, so a run with five services says which of them is up.
  if (event.task) {
    const state = { taskStarted: 'starting', taskReady: 'ready', taskExited: 'exited' }[event.kind];
    const url = event.kind === 'info' && event.text.startsWith('open ')
      ? event.text.slice(5).trim()
      : null;

    const pid = event.kind === 'info' && event.text.startsWith('pid ')
      ? Number.parseInt(event.text.slice(4).trim(), 10)
      : null;

    if (state || url || pid) {
      const current = tasks.get(event.task) ?? { state: 'starting', url: null, pid: null };
      tasks.set(event.task, {
        state: state ?? current.state,
        url: url ?? current.url,
        pid: pid ?? current.pid,
      });
      renderTasks();
    }
  }

  if (event.kind === 'taskStarted') {
    sawTask = true;
    liveTasks += 1;
    updateStop();
  }

  if (event.kind === 'taskExited') {
    liveTasks = Math.max(0, liveTasks - 1);
    updateStop();
  }

  if (event.kind === 'error') {
    errorLines += 1;
    setState(document.getElementById('state').textContent, banner.classList.contains('banner--bad') ? 'bad' : 'running');
  }

  append(`${event.task ? `[${event.task}] ` : ''}${event.text}`, event.kind === 'error' ? 'err' : '');

  // Terminal, one way or another: what is left to do is read the log and close the window.
  if (event.kind === 'finished' || event.kind === 'failed' || event.kind === 'cancelled') conclude(event.kind);
}

/** The run is over, however it got there. */
function conclude(kind) {
  if (finished) return;
  finished = true;

  const outcome = {
    finished: ['Finished', 'ok', 'finished'],
    failed: ['Failed', 'bad', 'failed'],
    cancelled: ['Stopped', 'warn', 'stopped'],
  }[kind] ?? ['Finished', 'ok', 'finished'];

  document.getElementById('phase').textContent = outcome[2];
  setState(outcome[0], outcome[1]);
  stop.hidden = true;
  close.hidden = false;

  // Asked to stop, and it stopped: the window has done its job. Long enough to read the banner,
  // short enough to be out of the way - and only when the user asked for it.
  if (stopping) setTimeout(() => window.close(), 1500);
}

/**
 * The log, written in batches and kept to a few hundred lines.
 *
 * A restore prints thousands of lines, and touching the DOM once per line is how a window stops
 * answering and then dies.
 */
const LOG_LINES = 800;
const pendingLines = [];
let flushingLog = false;
let shownLines = 0;

function append(line, kind) {
  if (!line) return;

  pendingLines.push([line, kind]);
  if (flushingLog) return;

  flushingLog = true;
  requestAnimationFrame(flushLog);
}

/**
 * Every line, kept as text even after it has scrolled out of the visible log.
 *
 * The window shows the last few hundred lines because a restore prints thousands and the DOM is
 * what gives out first. Copying or saving a log that stops where the display does would be a quiet
 * lie, so the text is kept apart from it - a string per line, and nothing else.
 */
const allLines = [];

function flushLog() {
  flushingLog = false;

  const lines = pendingLines.splice(0, pendingLines.length);
  if (lines.length === 0) return;

  const atBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 40;
  const batch = document.createDocumentFragment();
  const needle = filterText();

  for (const [line, kind] of lines) {
    allLines.push(line);

    const entry = document.createElement('span');
    if (kind) entry.className = kind;
    // textContent: log lines are whatever the repository's commands printed.
    entry.textContent = `${line}\n`;
    if (needle && !line.toLowerCase().includes(needle)) entry.hidden = true;
    batch.append(entry);
  }

  log.append(batch);
  shownLines += lines.length;

  while (shownLines > LOG_LINES && log.firstChild) {
    log.firstChild.remove();
    shownLines -= 1;
  }

  if (needle) countMatches();
  if (atBottom) log.scrollTop = log.scrollHeight;
}

/* ---- reading the log ---------------------------------------------------------------------- */

const filter = document.getElementById('filter');
const filterCount = document.getElementById('filterCount');

function filterText() {
  return filter.value.trim().toLowerCase();
}

function applyFilter() {
  const needle = filterText();

  for (const entry of log.children) {
    entry.hidden = needle !== '' && !entry.textContent.toLowerCase().includes(needle);
  }

  countMatches();
}

function countMatches() {
  const needle = filterText();

  if (!needle) {
    filterCount.textContent = '';
    return;
  }

  const shown = [...log.children].filter((entry) => !entry.hidden).length;
  filterCount.textContent = `${shown} of ${log.children.length} shown`;
}

filter.addEventListener('input', applyFilter);

/** What the filter leaves, or everything when nothing is filtered - scrolled-off lines included. */
function logText() {
  const needle = filterText();
  const lines = needle ? allLines.filter((line) => line.toLowerCase().includes(needle)) : allLines;
  return lines.join('\n');
}

document.getElementById('copyLog').addEventListener('click', async (event) => {
  const button = event.currentTarget;

  try {
    await navigator.clipboard.writeText(logText());
    button.textContent = 'Copied';
  } catch {
    button.textContent = 'Could not copy';
  }

  setTimeout(() => { button.textContent = 'Copy'; }, 1500);
});

document.getElementById('saveLog').addEventListener('click', () => {
  const name = (run?.displayName || run?.repo || 'quickrun').replace(/[^a-z0-9._-]+/gi, '-');
  const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-');

  const url = URL.createObjectURL(new Blob([logText()], { type: 'text/plain;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = `${name}-${stamp}.log`;
  link.click();

  // The object URL holds the whole log in memory until it is let go.
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
});
