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
      return withAnchor({ repo: parsed.repo, label: 'Run this' }, repoHomeAnchor());
    case 'tree':
      return withAnchor({ repo: parsed.repo, ref: parsed.ref, label: 'Run this branch' }, repoHomeAnchor());
    case 'pull':
      return withAnchor({ repo: parsed.repo, pr: parsed.pr, label: `Run PR #${parsed.pr}` }, pullRequestAnchor());
    case 'branches':
      return branchRowTargets(parsed.repo);
    default:
      return [];
  }
}

function withAnchor(target, anchor) {
  return anchor ? [{ ...target, anchor }] : [];
}

function repoHomeAnchor() {
  return (
    document.querySelector('[data-testid="anchor-button"]')?.parentElement
    ?? document.querySelector('#branch-picker-repos-header-ref-selector')?.parentElement
    ?? document.querySelector('[data-testid="repos-header-ref-selector"]')?.parentElement
    ?? document.querySelector('.file-navigation')
  );
}

function pullRequestAnchor() {
  return (
    document.querySelector('[data-testid="pr-header-actions"]')
    ?? document.querySelector('.gh-header-actions')
    ?? document.querySelector('.gh-header-meta')
  );
}

function branchRowTargets(repo) {
  const rows = document.querySelectorAll('[data-testid="branch-row"], .Box-row');

  return Array.from(rows)
    .map((row) => {
      const link = row.querySelector('a[href*="/tree/"]');
      const ref = QuickRunTargets.refFromTreeHref(link?.getAttribute('href'));
      return ref ? { repo, ref, anchor: row, label: 'Run' } : null;
    })
    .filter(Boolean);
}

function makeButton(target) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = `${BUTTON_CLASS} btn btn-sm`;
  button.dataset.quickrun = 'true';
  button.innerHTML = `<span class="quickrun-icon" aria-hidden="true"></span><span class="quickrun-label"></span>`;
  setLabel(button, target.label);

  button.addEventListener('click', (event) => {
    event.preventDefault();
    event.stopPropagation();
    onClick(button, target);
  });

  return button;
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
    setLabel(button, `${progress.percent}% ${progress.detail}`.slice(0, 60));
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
