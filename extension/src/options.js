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

/* ---- self-hosted servers ----------------------------------------------------------------------- */

const TYPE_NAMES = { gitlab: 'GitLab', azure: 'Azure DevOps Server' };

async function customHosts() {
  return (await chrome.storage.local.get({ customHosts: [] })).customHosts;
}

async function renderCustomHosts() {
  const list = document.getElementById('customHosts');
  list.replaceChildren();

  for (const entry of await customHosts()) {
    const item = document.createElement('li');
    const remove = document.createElement('button');
    remove.type = 'button';
    remove.className = 'linkish';
    remove.textContent = 'remove';
    remove.addEventListener('click', () => removeCustom(entry.origin));

    // textContent: the address is whatever somebody typed.
    item.textContent = `${entry.origin} (${TYPE_NAMES[entry.type] ?? entry.type}) `;
    item.append(remove);
    list.append(item);
  }
}

document.getElementById('addCustom').addEventListener('click', async () => {
  const result = document.getElementById('customResult');
  const origin = QuickRunTargets.originOf(document.getElementById('customOrigin').value);
  const type = document.getElementById('customType').value;

  if (!origin) {
    result.textContent = 'that is not a web address';
    return;
  }

  // Already covered by the manifest; a second registration would put two buttons on every page.
  const host = new URL(origin).hostname;
  if (host === 'github.com' || QuickRunTargets.typeOf(host) !== 'github') {
    result.textContent = `${host} is supported already`;
    return;
  }

  // Asked before anything else is awaited: Firefox only shows the prompt while the click that
  // caused it is still being handled.
  const granted = await chrome.permissions.request({ origins: [`${origin}/*`] });
  if (!granted) {
    result.textContent = 'not added: access was not granted';
    return;
  }

  const others = (await customHosts()).filter((entry) => entry.origin !== origin);
  await chrome.storage.local.set({ customHosts: [...others, { origin, type }] });

  document.getElementById('customOrigin').value = '';
  result.textContent = 'added, reload its pages to see the button';
  await renderCustomHosts();
});

async function removeCustom(origin) {
  const rest = (await customHosts()).filter((entry) => entry.origin !== origin);
  await chrome.storage.local.set({ customHosts: rest });
  await chrome.permissions.remove({ origins: [`${origin}/*`] }).catch(() => {});
  document.getElementById('customResult').textContent = '';
  await renderCustomHosts();
}

await renderCustomHosts();
