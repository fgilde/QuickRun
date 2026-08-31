// Injects the QuickRun button into GitHub.
//
// GitHub is a Turbo-driven SPA, so injection hooks navigation events and a MutationObserver rather
// than running once. Selectors are anchored on data-testid and ARIA labels where those exist, and
// every failure is silent: a missing button is acceptable, a broken GitHub page is not.

const BUTTON_CLASS = 'quickrun-button';

/** Where a button belongs, and what it should run. Path handling lives in targets.js. */
function targets() {
  const parsed = QuickRunTargets.parseLocation(location.pathname);
  if (!parsed) return [];

  switch (parsed.kind) {
    case 'repo':
      return withAnchor({ repo: parsed.repo, label: 'Run this' }, QuickRunPlacement.repoToolbar());
    case 'tree':
      return withAnchor(
        { repo: parsed.repo, ref: parsed.ref, label: 'Run this branch' },
        QuickRunPlacement.repoToolbar(),
      );
    case 'pull':
      return withAnchor(
        { repo: parsed.repo, pr: parsed.pr, label: `Run PR #${parsed.pr}` },
        QuickRunPlacement.pullRequestActions(),
      );
    case 'branches':
      return QuickRunPlacement.branchRows(parsed.repo).map((row) => ({
        repo: parsed.repo,
        ref: row.ref,
        anchor: row.anchor,
        label: 'Run',
        compact: true,
      }));
    default:
      return [];
  }
}

function withAnchor(target, anchor) {
  return anchor ? [{ ...target, anchor }] : [];
}

function makeButton(target) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = BUTTON_CLASS;
  if (target.compact) button.classList.add('quickrun-button--compact');
  button.dataset.quickrun = 'true';

  const icon = document.createElement('img');
  icon.className = 'quickrun-icon';
  icon.alt = '';
  icon.src = chrome.runtime.getURL('icons/icon-32.png');

  const label = document.createElement('span');
  label.className = 'quickrun-label';
  // In a branch row the button stands among GitHub's icon buttons; the title carries the text.
  if (target.compact) label.hidden = true;

  const progress = document.createElement('span');
  progress.className = 'quickrun-progress';

  button.append(icon, label, progress);
  // What this button says when it has nothing to report. Kept on the element because the message
  // listener below sees the button and not the target it was built from.
  button.dataset.idleLabel = target.label;
  setLabel(button, target.label);

  button.addEventListener('click', (event) => {
    event.preventDefault();
    event.stopPropagation();

    // A run of this branch is already going: clicking again must not start a second one. The button
    // becomes the way into what can be done with the run instead.
    if (button.dataset.runId && ACTIVE_STATES.includes(button.dataset.state)) {
      openMenu(button, target);
      return;
    }

    onClick(button, target);
  });

  return button;
}

const ACTIVE_STATES = ['running', 'working', 'starting'];

/* ---- the menu on a running button ------------------------------------------------------------ */

/**
 * The actions for a run in progress.
 *
 * Floating and appended to the body rather than placed inside the button: this button sits in a 70px
 * grid column on the branch list, and anything that widens it pushes GitHub's own table around.
 */
async function openMenu(button, target) {
  closeMenu();

  const runId = button.dataset.runId;
  const answer = await send({ type: 'activeRun', target: asTarget(target) });
  const run = answer?.run ?? null;

  // It ended while the menu was being opened: back to a button that starts a run.
  if (!run) {
    delete button.dataset.runId;
    setState(button, 'ready');
    setLabel(button, target.label);
    return;
  }

  const menu = document.createElement('div');
  menu.className = 'quickrun-menu';
  menu.dataset.quickrun = 'true';

  const url = run.url;
  const items = [
    { label: 'Show log', action: () => send({ type: 'showLog', runId: run.id ?? runId }) },
    url ? { label: `Open ${short(url)}`, action: () => window.open(url, '_blank', 'noopener') } : null,
    {
      label: run.leftovers > 0 && run.state !== 'running' ? 'Stop what it left running' : 'Stop',
      danger: true,
      action: async () => {
        setLabel(button, 'Stopping...');
        await send({ type: 'stop', runId: run.id ?? runId });
      },
    },
  ].filter(Boolean);

  for (const item of items) {
    const entry = document.createElement('button');
    entry.type = 'button';
    entry.className = 'quickrun-menu-item';
    if (item.danger) entry.classList.add('quickrun-menu-item--danger');
    // textContent: an address comes from the repository's own output.
    entry.textContent = item.label;
    entry.addEventListener('click', (event) => {
      event.preventDefault();
      event.stopPropagation();
      closeMenu();
      item.action();
    });
    menu.append(entry);
  }

  const box = button.getBoundingClientRect();
  menu.style.top = `${box.bottom + window.scrollY + 4}px`;
  menu.style.left = `${Math.max(8, box.right + window.scrollX - 220)}px`;

  document.body.append(menu);
  document.addEventListener('click', closeMenu, { once: true });
  document.addEventListener('keydown', onEscape);
}

function closeMenu() {
  document.querySelector('.quickrun-menu')?.remove();
  document.removeEventListener('keydown', onEscape);
}

function onEscape(event) {
  if (event.key === 'Escape') closeMenu();
}

/** An address short enough for a menu item. */
function short(url) {
  try {
    const parsed = new URL(url);
    return parsed.host + (parsed.pathname === '/' ? '' : parsed.pathname);
  } catch {
    return url.slice(0, 40);
  }
}

function asTarget(target) {
  return { repo: target.repo, ref: target.ref ?? null, pr: target.pr ?? null };
}

/** A few words, not a log line: the button is a status light, not a console. */
function phaseOf(progress) {
  switch (progress.phase) {
    case 'checkout': return 'checking out';
    case 'setup': return 'setting up';
    case 'tasks': return 'starting';
    default: return progress.detail?.slice(0, 28) ?? '';
  }
}

/**
 * The button's line while a run is going.
 *
 * A finished bar reading "100% starting" is a contradiction, and it is what the button showed for
 * the whole time an application was actually up: the tasks phase ends at a hundred, and the word
 * for that is running.
 */
function describe(progress) {
  if (progress.phase === 'tasks' && progress.percent >= 100) return 'Running';
  return `${progress.percent}% ${phaseOf(progress)}`;
}

/**
 * Says how it ended, then goes back to offering a run.
 *
 * A button left reading "Stopped" is a dead end: the run is gone, the next click would start a new
 * one, and nothing on the button says so.
 */
function goIdleLater(button) {
  clearTimeout(Number(button.dataset.idleTimer));

  const timer = setTimeout(() => {
    // Unless a new run started in the meantime, which owns the button now.
    if (button.dataset.runId) return;

    setState(button, 'ready');
    setLabel(button, button.dataset.idleLabel ?? 'Run');
    button.style.removeProperty('--quickrun-progress');
  }, 6000);

  button.dataset.idleTimer = String(timer);
}

function setLabel(button, text) {
  const label = button.querySelector('.quickrun-label');
  if (label) label.textContent = text;
  // The version is in the tooltip because an unpacked extension does not update itself: when a
  // button misbehaves, the first question is always which build is actually loaded.
  button.title = `QuickRun ${chrome.runtime.getManifest().version}: ${text}`;
}

function setState(button, state) {
  button.dataset.state = state;
}

async function onClick(button, target, config = null) {
  const status = await send({ type: 'status' });

  if (status.state === 'not-installed') {
    setState(button, 'starting');
    setLabel(button, 'Starting QuickRun...');

    const bootstrap = await send({ type: 'bootstrapDaemon' });
    if (!bootstrap.started) {
      setState(button, 'not-installed');
      setLabel(button, 'Install QuickRun');
      await send({ type: 'openDownloads' });
      return;
    }
  }

  setState(button, 'working');
  setLabel(button, 'Preparing...');

  // config travels beside the target rather than inside it, so every other message this content
  // script sends keeps exactly the shape it had.
  const result = await send({ type: 'run', target: asTarget(target), config });

  if (result.error) {
    setState(button, 'error');
    setLabel(button, result.error.slice(0, 60));
    return;
  }

  button.dataset.runId = result.runId;

  // The plan is on screen and nothing has started yet. Saying "Running" here would be a lie for as
  // long as the person takes to read it - and the answer arrives as an event either way.
  if (result.state === 'awaitingConfirmation') {
    setState(button, 'working');
    setLabel(button, 'Confirm to start...');
    return;
  }

  setState(button, 'running');
  setLabel(button, 'Running...');
}

chrome.runtime.onMessage.addListener((message) => {
  if (message?.type !== 'runEvent') return;

  const button = document.querySelector(`.${BUTTON_CLASS}[data-run-id="${message.runId}"]`);
  if (!button) return;

  const { kind, progress, text } = message.event;

  if (kind === 'taskReady') {
    // Something answered. Starting is over, whatever the percentage says.
    setState(button, 'running');
    setLabel(button, 'Running');
    button.style.setProperty('--quickrun-progress', '100%');
    return;
  }

  if (progress) {
    setState(button, 'running');
    // Coarse action only; the full log lives in the log window.
    setLabel(button, describe(progress));
    button.style.setProperty('--quickrun-progress', `${progress.percent}%`);
    return;
  }

  if (kind === 'failed') {
    setState(button, 'error');
    setLabel(button, text.slice(0, 60));
  } else if (kind === 'finished') {
    setState(button, 'done');
    setLabel(button, 'Finished');
  } else if (kind === 'cancelled') {
    // Stopped on request is neither a failure nor a success, and the button has to stop saying
    // a percentage that will never move again.
    setState(button, 'done');
    setLabel(button, 'Stopped');
  }

  // Over, so the next click starts a run again rather than opening a menu about a dead one.
  if (kind === 'finished' || kind === 'failed' || kind === 'cancelled') {
    delete button.dataset.runId;
    goIdleLater(button);
  }
});

function send(message) {
  return chrome.runtime.sendMessage(message).catch((error) => ({ error: String(error) }));
}

async function inject() {
  const status = await send({ type: 'status' });

  for (const target of targets()) {
    if (!target.anchor || target.anchor.querySelector(`.${BUTTON_CLASS}`)) continue;

    // A setting decides whether this repository gets a button at all: every repository, only the
    // ones carrying instructions, or only the ones with a quickrun.yml.
    const verdict = await send({ type: 'shouldShow', target: asTarget(target) });
    if (verdict && verdict.show === false) continue;

    const button = makeButton(target);
    setState(button, status.state === 'ready' ? 'ready' : status.state);
    if (status.state === 'not-installed') setLabel(button, 'Install QuickRun');

    target.anchor.appendChild(button);

    // A reload forgets which run this tab started; the daemon has not. Without this the button
    // offers to start a second run of something that is already running.
    if (status.state === 'ready') adopt(button, target);

    // A link may ask for the plan straight away. Only the first button on the page, and only once
    // per address: GitHub renders through pushState, so inject() runs again and again on what is,
    // to a reader, the same page.
    autorun(button, target);
  }
}

/** Which address has already had its ?executeQuickRun acted on. */
let autorunFor = null;

/**
 * Acts on `?executeQuickRun` by doing what a click does: the plan, in the confirmation window.
 *
 * Not the running of it. The window still has to be read and Run still has to be pressed - a link
 * that ran commands on its own would be someone else's click on your machine.
 */
async function autorun(button, target) {
  const wanted = QuickRunTargets.parseAutorun(location.search);
  if (!wanted) return;
  if (autorunFor === location.href) return;

  autorunFor = location.href;

  if (wanted.error) {
    setState(button, 'error');
    setLabel(button, wanted.error);
    return;
  }

  if (button.dataset.runId) return;   // already running: the button is a menu, not a start

  await onClick(button, target, wanted.config);
}

/** Puts a button back in touch with the run of its own branch, if one is still going. */
async function adopt(button, target) {
  const answer = await send({ type: 'activeRun', target: asTarget(target) });
  const run = answer?.run;
  if (!run) return;

  button.dataset.runId = run.id;
  setState(button, 'running');
  setLabel(button, run.state === 'stopping' ? 'Stopping...'
    : run.progress ? describe(run.progress)
    : run.leftovers > 0 ? 'Still running' : 'Running');
  if (run.progress) button.style.setProperty('--quickrun-progress', `${run.progress.percent}%`);
}

function schedule() {
  // Coalesce the burst of mutations GitHub produces while rendering.
  clearTimeout(schedule.timer);
  schedule.timer = setTimeout(() => inject().catch(() => {}), 150);
}

schedule();
document.addEventListener('turbo:load', schedule);
document.addEventListener('pjax:end', schedule);
new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true });
