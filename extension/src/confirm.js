// The confirmation gate. Runs in an extension window, which a web page cannot overlay: the user
// must see the real command list, not one a page drew on top of it.

const { pendingRun: run } = await chrome.storage.session.get('pendingRun');

if (!run) {
  document.getElementById('name').textContent = 'Nothing to confirm';
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

document.getElementById('approve').addEventListener('click', () => decide(true));
document.getElementById('cancel').addEventListener('click', () => decide(false));

async function decide(approved) {
  await chrome.runtime.sendMessage({ type: 'confirmResult', runId: run?.id, approved });
  await chrome.storage.session.remove('pendingRun');
  window.close();
}
