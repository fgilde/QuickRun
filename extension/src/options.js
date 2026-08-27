const stored = await chrome.storage.local.get({
  port: 9876,
  useProtocolFallback: true,
  showOn: 'always',
});

document.getElementById('port').value = stored.port;
document.getElementById('useProtocolFallback').checked = stored.useProtocolFallback;

const chosen = document.querySelector(`input[name="showOn"][value="${stored.showOn}"]`)
  ?? document.querySelector('input[name="showOn"][value="always"]');
chosen.checked = true;

await refreshStatus();

async function refreshStatus() {
  const status = await chrome.runtime.sendMessage({ type: 'status' });
  const target = document.getElementById('status');

  switch (status?.state) {
    case 'ready':
      target.textContent = `connected to QuickRun ${status.version}`;
      break;
    default:
      target.textContent = 'QuickRun is not running on this machine';
  }
}



document.getElementById('save').addEventListener('click', async () => {
  await chrome.storage.local.set({
    port: Number(document.getElementById('port').value) || 9876,
    useProtocolFallback: document.getElementById('useProtocolFallback').checked,
    showOn: document.querySelector('input[name="showOn"]:checked')?.value ?? 'always',
  });
  document.getElementById('saveResult').textContent = 'saved';
  await refreshStatus();
});
