// The confirmation gate, and afterwards the run's log.
//
// It runs in an extension window, which a web page cannot overlay: the user must see the real
// command list, not one a page drew on top of it. After approval the window stays open and streams
// everything QuickRun does, so the log has somewhere to live that is not a 200px toolbar button.

const { pendingRun: run } = await chrome.storage.session.get('pendingRun');

const review = document.getElementById('review');
const progress = document.getElementById('progress');
const log = document.getElementById('log');
const approve = document.getElementById('approve');
const cancel = document.getElementById('cancel');
const stop = document.getElementById('stop');
const close = document.getElementById('close');

let decided = false;

if (!run) {
  document.getElementById('name').textContent = 'Nothing to confirm';
  approve.hidden = true;
} else {
  render(run);
}

function render(run) {
  text('name', run.displayName || run.repo);
  text('subtitle', `${run.commands.length} command(s)`);
  text('repo', run.repo);
  text('ref', run.ref);
  text('commit', run.commit ? run.commit.slice(0, 10) : 'unknown');

  const list = document.getElementById('commands');
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

function text(id, value) {
  document.getElementById(id).textContent = value ?? '';
}

approve.addEventListener('click', () => decide(true));
cancel.addEventListener('click', () => decide(false));
close.addEventListener('click', () => window.close());

stop.addEventListener('click', async () => {
  stop.disabled = true;
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

  append(`${event.task ? `[${event.task}] ` : ''}${event.text}`, event.kind === 'error' ? 'err' : '');

  if (event.kind === 'finished' || event.kind === 'failed') {
    document.getElementById('phase').textContent =
      event.kind === 'finished' ? 'finished' : 'failed';
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
