const stored = await chrome.storage.local.get({
  port: 9876,
  token: null,
  useProtocolFallback: true,
});

document.getElementById('port').value = stored.port;
document.getElementById('useProtocolFallback').checked = stored.useProtocolFallback;

await refreshStatus();

async function refreshStatus() {
  const status = await chrome.runtime.sendMessage({ type: 'status' });
  const target = document.getElementById('status');

  switch (status?.state) {
    case 'ready':
      target.textContent = `connected to QuickRun ${status.version}`;
      break;
    case 'not-paired':
      target.textContent = `QuickRun ${status.version} is running but not paired yet`;
      break;
    default:
      target.textContent = 'QuickRun is not running on this machine';
  }
}

document.getElementById('pair').addEventListener('click', async () => {
  const result = await chrome.runtime.sendMessage({ type: 'pair' });
  document.getElementById('pairResult').textContent = result?.ok
    ? 'paired'
    : result?.error ?? 'pairing failed';
  await refreshStatus();
});

document.getElementById('unpair').addEventListener('click', async () => {
  await chrome.storage.local.remove('token');
  document.getElementById('pairResult').textContent = 'token forgotten';
  await refreshStatus();
});

document.getElementById('save').addEventListener('click', async () => {
  await chrome.storage.local.set({
    port: Number(document.getElementById('port').value) || 9876,
    useProtocolFallback: document.getElementById('useProtocolFallback').checked,
  });
  document.getElementById('saveResult').textContent = 'saved';
  await refreshStatus();
});
