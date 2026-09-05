// The browser action popup: one status line and one obvious action. Anything longer belongs on the
// options page, which has room for it.

const DOWNLOADS = 'https://quickrun.org/download';
const DOCS = 'https://quickrun.org/extension';

/**
 * This extension's own version.
 *
 * The status line used to name only the version of the tool it was talking to, which is the one
 * thing that is never in doubt: it answers, so it is current. The extension is the half that lags -
 * a store takes days to approve one - and a window missing something that shipped weeks ago is not
 * diagnosable without knowing which build is doing the drawing. It took reading a git tag to answer
 * that once; it should take opening this popup.
 */
const MINE = chrome.runtime.getManifest().version;

const status = document.getElementById('status');
const actions = document.getElementById('actions');
const hint = document.getElementById('hint');

document.getElementById('options').addEventListener('click', () => chrome.runtime.openOptionsPage());
document.getElementById('docs').addEventListener('click', () => open(DOCS));

render(await chrome.runtime.sendMessage({ type: 'status' }));

function render(state) {
  switch (state?.state) {
    case 'ready':
      // Both, and labelled: which is the extension and which is the application it reached.
      status.textContent = `extension ${MINE} · connected to QuickRun ${state.version}`;
      button('Open dashboard', 'primary', () => open(`http://127.0.0.1:${state.port ?? 9876}`));
      say('Open any repository on GitHub and use the Run button.');
      break;

    default:
      status.textContent = `extension ${MINE} · QuickRun not running`;
      button('Install QuickRun', 'primary', () => open(DOWNLOADS));
      button('Try to start it', 'secondary', start);
      say('QuickRun runs on your machine. The extension only talks to it.');
  }
}

function button(label, kind, action) {
  const element = document.createElement('button');
  element.type = 'button';
  element.className = kind;
  element.textContent = label;
  element.addEventListener('click', action);
  actions.append(element);
}

function say(text) {
  // textContent, not innerHTML: these are fixed strings, so markup buys nothing - and an unsafe
  // assignment to innerHTML is a warning every add-on reviewer has to stop and look at.
  hint.textContent = text;
}


async function start() {
  status.textContent = 'starting…';
  const result = await chrome.runtime.sendMessage({ type: 'bootstrapDaemon' });

  actions.replaceChildren();
  render(result?.started
    ? await chrome.runtime.sendMessage({ type: 'status' })
    : { state: 'not-installed' });
}

function open(url) {
  chrome.tabs.create({ url });
  window.close();
}
