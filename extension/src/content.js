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

  const progress = document.createElement('span');
  progress.className = 'quickrun-progress';

  button.append(icon, label, progress);
  setLabel(button, target.label);

  button.addEventListener('click', (event) => {
    event.preventDefault();
    event.stopPropagation();
    onClick(button, target);
  });

  return button;
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

function setLabel(button, text) {
  const label = button.querySelector('.quickrun-label');
  if (label) label.textContent = text;
  button.title = `QuickRun: ${text}`;
}

function setState(button, state) {
  button.dataset.state = state;
}

async function onClick(button, target) {
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

  const paired = await send({ type: 'status' });
  if (paired.state === 'not-paired') {
    setState(button, 'error');
    setLabel(button, 'Pair QuickRun first');
    await chrome.runtime.openOptionsPage?.();
    return;
  }

  setState(button, 'working');
  setLabel(button, 'Preparing...');

  const result = await send({
    type: 'run',
    target: { repo: target.repo, ref: target.ref ?? null, pr: target.pr ?? null },
  });

  if (result.cancelled) {
    setState(button, 'ready');
    setLabel(button, target.label);
    return;
  }

  if (result.error) {
    setState(button, 'error');
    setLabel(button, result.error.slice(0, 60));
    return;
  }

  button.dataset.runId = result.runId;
  setState(button, 'running');
  setLabel(button, 'Running...');
}

chrome.runtime.onMessage.addListener((message) => {
  if (message?.type !== 'runEvent') return;

  const button = document.querySelector(`.${BUTTON_CLASS}[data-run-id="${message.runId}"]`);
  if (!button) return;

  const { kind, progress, text } = message.event;

  if (progress) {
    setState(button, 'running');
    // Coarse action only; the full log lives in the confirmation window.
    setLabel(button, `${progress.percent}% ${phaseOf(progress)}`);
    button.style.setProperty('--quickrun-progress', `${progress.percent}%`);
    return;
  }

  if (kind === 'failed') {
    setState(button, 'error');
    setLabel(button, text.slice(0, 60));
  } else if (kind === 'finished') {
    setState(button, 'done');
    setLabel(button, 'Finished');
  }
});

function send(message) {
  return chrome.runtime.sendMessage(message).catch((error) => ({ error: String(error) }));
}

async function inject() {
  const status = await send({ type: 'status' });

  for (const target of targets()) {
    if (!target.anchor || target.anchor.querySelector(`.${BUTTON_CLASS}`)) continue;

    const button = makeButton(target);
    setState(button, status.state === 'ready' ? 'ready' : status.state);
    if (status.state === 'not-installed') setLabel(button, 'Install QuickRun');

    target.anchor.appendChild(button);
  }
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
