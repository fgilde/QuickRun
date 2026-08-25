// The confirmation gate, and afterwards the run's log.
//
// It runs in an extension window, which a web page cannot overlay: the user must see the real
// command list, not one a page drew on top of it. After approval the window stays open and streams
// everything QuickRun does, so the log has somewhere to live that is not a 200px toolbar button.

import { httpUrl } from './safeurl.js';
import { inputForm } from './inputs.js';

const { pendingRun } = await chrome.storage.session.get('pendingRun');

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

// What "Stop" would actually stop. Tasks report when they start and when they exit, so a run whose
// processes have all gone gets a disabled button instead of one that does nothing.
let liveTasks = 0;
let sawTask = false;

if (!run) {
  document.getElementById('name').textContent = 'Nothing to confirm';
  approve.hidden = true;
} else {
  render(run);
}

function render(run) {
  text('name', run.displayName || run.repo);
  text('subtitle', run.commands.length === 0
    ? 'fill in the values below to see what would run'
    : `${run.commands.length} command(s)`);
  text('repo', run.repo);
  text('ref', run.ref);
  text('commit', run.commit ? run.commit.slice(0, 10) : 'unknown');
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
 * With no plan, or with values that have been changed since the plan was built, it applies the
 * values first - the command list has to be the one those values produced, because that list is
 * what the user is approving.
 */
function updateApprove() {
  const needsValues = run.state === 'awaitingInput' || (form?.dirty() ?? false);
  approve.textContent = needsValues ? 'Continue' : 'Run';
}

function text(id, value) {
  document.getElementById(id).textContent = value ?? '';
}

approve.addEventListener('click', async () => {
  if (run.state === 'awaitingInput' || (form?.dirty() ?? false)) {
    await applyValues();
    return;
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

  if (result?.run) {
    run = result.run;
    render(run);
  }

  if (result?.error) {
    text('inputError', result.error);
    return;
  }

  text('inputError', run.commands.length > 0
    ? 'this is what those values would run - check it and press Run'
    : '');
  updateApprove();
}

document.getElementById('inputs').addEventListener('input', updateApprove);
document.getElementById('inputs').addEventListener('change', updateApprove);
cancel.addEventListener('click', () => decide(false));
close.addEventListener('click', () => window.close());

// A page cannot open a local folder, and neither can an extension window: the daemon does it,
// and only for a path it already holds for this run.
document.getElementById('openDir').addEventListener('click', async () => {
  await chrome.runtime.sendMessage({ type: 'reveal', runId: run?.id });
});

stop.addEventListener('click', async () => {
  stop.disabled = true;
  setState('Stopping', 'running');
  await chrome.runtime.sendMessage({ type: 'stop', runId: run.id });
});

async function decide(approved) {
  if (decided) return;
  decided = true;

  await chrome.runtime.sendMessage({ type: 'confirmResult', runId: run?.id, approved });
  await chrome.storage.session.remove('pendingRun');

  if (!approved) {
    window.close();
    return;
  }

  // Hand the window over to the run.
  document.getElementById('name').textContent = run.displayName || run.repo;
  review.hidden = true;
  progress.hidden = false;
  approve.hidden = true;
  cancel.hidden = true;
  stop.hidden = false;
  setState('Running', 'running');
}

/**
 * The outcome, said plainly. "finished" with twenty error lines behind it is not the same thing as
 * "finished", so the count travels with it.
 */
function setState(label, kind) {
  document.getElementById('state').textContent = label;
  banner.className = `banner banner--${kind}`;

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

/** Stoppable only while something is actually running. */
function updateStop() {
  stop.disabled = sawTask && liveTasks === 0;
  stop.title = stop.disabled ? 'nothing is running any more' : '';
}

chrome.runtime.onMessage.addListener((message) => {
  if (message?.type !== 'runEvent' || message.runId !== run?.id) return;

  const event = message.event;

  if (event.progress) {
    document.getElementById('fill').style.width = `${event.progress.percent}%`;
    document.getElementById('percent').textContent = `${event.progress.percent}%`;
    document.getElementById('phase').textContent = event.progress.detail || event.progress.phase;
    append(event.progress.detail, 'meta');
    return;
  }

  // The runner announces the address it would have opened as an ordinary log line.
  if (event.kind === 'info' && event.text.startsWith('open ')) showAddress(event.text.slice(5).trim());

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
  if (event.kind === 'finished' || event.kind === 'failed' || event.kind === 'cancelled') {
    const outcome = {
      finished: ['Finished', 'ok', 'finished'],
      failed: ['Failed', 'bad', 'failed'],
      cancelled: ['Stopped', 'warn', 'stopped'],
    }[event.kind];

    document.getElementById('phase').textContent = outcome[2];
    setState(outcome[0], outcome[1]);
    stop.hidden = true;
    close.hidden = false;
  }
});

function append(line, kind) {
  if (!line) return;

  const entry = document.createElement('span');
  if (kind) entry.className = kind;
  // textContent: log lines are whatever the repository's commands printed.
  entry.textContent = `${line}\n`;

  const atBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 40;
  log.append(entry);
  if (atBottom) log.scrollTop = log.scrollHeight;
}
