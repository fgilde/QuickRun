// QuickRun's web components, for any page that wants a Run button of its own.
//
//   <script type="module" src="https://quickrun.org/components.js"></script>
//   <quickrun-btn repo="acme/app">Try it</quickrun-btn>
//
// One file, no build step, no dependency. Served from quickrun.org and always the current one:
// a page embedding this gets today's behaviour rather than whatever was current when it was
// written, which matters because what happens on a click has already changed twice.
//
// What a click can and cannot do is the whole design. A page may ask whether QuickRun is there -
// /api/ping answers anyone on purpose - and it may hand a repository over. It may not start
// anything: the plan appears in QuickRun's own window and waits for a person there. That is why
// there is no component here that draws a plan or a log. One that did would be the convincing fake
// the confirmation window exists to prevent, and a page that could be talked into approving
// commands is the one thing this project must never ship.

const DEFAULT_PORT = 9876;
const RUN_PAGE = 'https://quickrun.org/run';

/** Long enough for a loopback answer, short enough that a page is not held up by it. */
const PING_TIMEOUT = 1500;

/** QuickRun's mark: the same triangle the README badge carries, drawn rather than downloaded. */
const MARK = '<svg viewBox="0 0 10 10" part="icon" class="icon" aria-hidden="true">'
  + '<path d="M0.5 0 L9 5 L0.5 10 Z" fill="currentColor"/></svg>';

/**
 * Whether QuickRun answers on a port, asked once per port for the whole page.
 *
 * Every button and every status badge wants the same answer, and a page with a list of
 * repositories would otherwise open one connection per row.
 */
const asked = new Map();

function ping(port) {
  if (asked.has(port)) return asked.get(port);

  const answer = (async () => {
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), PING_TIMEOUT);

    try {
      const response = await fetch(`http://127.0.0.1:${port}/api/ping`,
        { cache: 'no-store', signal: stop.signal });

      if (!response.ok) return { running: false };

      const body = await response.json();
      return { running: true, version: body.version, busy: Boolean(body.busy) };
    } catch {
      // Nothing listening, or a browser that refused the request. Either way: not running.
      return { running: false };
    } finally {
      clearTimeout(timer);
    }
  })();

  asked.set(port, answer);
  return answer;
}

/**
 * What a hand-over may name, as a query.
 *
 * Never commands. A config is named - by a path inside the repository, by an address, or by the
 * word "collection" for the one QuickRun keeps - and QuickRun reads it itself, so what runs is
 * always a file somebody can look at rather than a string out of a link.
 */
function carry({ repo, ref, pr, config }) {
  const parts = [`repo=${encodeURIComponent(repo)}`];

  if (ref) parts.push(`ref=${encodeURIComponent(ref)}`);
  if (pr) parts.push(`pr=${encodeURIComponent(pr)}`);
  if (config) parts.push(`config=${encodeURIComponent(config)}`);

  return parts.join('&');
}

/**
 * Hands a target to the QuickRun on this machine.
 *
 * Two ways, tried in this order for a reason. A site the reader has trusted may ask the local
 * QuickRun to open its window, which is seamless - quickrun.org is trusted out of the box, and
 * anyone can add their own site in QuickRun's settings. Everywhere else the quickrun:// scheme does
 * the same job, at the cost of one "open QuickRun?" from the browser. Both end at the same window
 * with the same plan waiting to be approved; the difference is only who is asked first.
 *
 * @returns 'window' when QuickRun opened it directly, 'scheme' when the browser was asked to.
 */
async function handOver(target, port) {
  const query = carry(target);

  try {
    const response = await fetch(`http://127.0.0.1:${port}/api/show?${query}`,
      { method: 'POST', mode: 'cors', cache: 'no-store' });

    if (response.ok && (await response.json())?.shown === true) return 'window';
  } catch {
    // Not a trusted site here, or an older QuickRun. Neither is an error - it is the answer.
  }

  location.href = `quickrun://run?${query}`;
  return 'scheme';
}

const STYLE = `
  :host {
    display: inline-block;

    /* Everything worth changing without writing a selector. A page that wants more can reach the
       parts - ::part(button), ::part(icon), ::part(label) - or turn the styling off entirely with
       the unstyled attribute and dress the markup itself. */
    --quickrun-bg: #6d4ac4;
    --quickrun-fg: #fff;
    --quickrun-border: transparent;
    --quickrun-radius: 8px;
    --quickrun-padding: 8px 14px;
    --quickrun-gap: 8px;
    --quickrun-font: inherit;
    --quickrun-size: 14px;
    --quickrun-icon-size: 0.85em;
    --quickrun-weight: 600;
  }

  :host([hidden]) { display: none; }

  button {
    display: inline-flex;
    align-items: center;
    gap: var(--quickrun-gap);
    font: inherit;
    font-family: var(--quickrun-font);
    font-size: var(--quickrun-size);
    font-weight: var(--quickrun-weight);
    line-height: 1.2;
    padding: var(--quickrun-padding);
    border: 1px solid var(--quickrun-border);
    border-radius: var(--quickrun-radius);
    background: var(--quickrun-bg);
    color: var(--quickrun-fg);
    cursor: pointer;
  }

  button:hover { filter: brightness(1.08); }
  button:active { filter: brightness(0.94); }
  button:focus-visible { outline: 2px solid var(--quickrun-fg); outline-offset: 2px; }
  button[disabled] { cursor: progress; opacity: .75; }

  /* The icon after the label rather than before it, without the markup changing order - so a
     screen reader reads the same thing either way. */
  button[data-icon="right"] { flex-direction: row-reverse; }

  .icon { width: var(--quickrun-icon-size); height: var(--quickrun-icon-size); flex: none; }
`;

/**
 * A Run button for a repository.
 *
 * Attributes: repo, ref, pr, config ("collection", a path inside the repository, or an https
 * address), icon (left | right | none), label, mode (window | link), port, unstyled.
 */
class QuickRunButton extends HTMLElement {
  static observedAttributes = ['label', 'icon', 'unstyled'];

  #button;
  #label;
  #style;

  constructor() {
    super();

    const root = this.attachShadow({ mode: 'open' });

    root.innerHTML = `<style>${STYLE}</style>
      <button part="button" type="button">${MARK}<span part="label" class="label"></span></button>`;

    this.#style = root.querySelector('style');
    this.#button = root.querySelector('button');
    this.#label = root.querySelector('.label');

    this.#button.addEventListener('click', () => this.run());
  }

  connectedCallback() {
    this.#render();

    // What the button says before anything is known: the label, and no promise about QuickRun
    // being there. Whether it is decides only what a press does, not whether the button works.
    ping(this.port).then((state) => {
      // On the host rather than inside, so a page can style the button for either case:
      //   quickrun-btn[data-running] { ... }   quickrun-btn:not([data-running]) { ... }
      if (state.running) this.dataset.running = '';
      else delete this.dataset.running;

      this.dispatchEvent(new CustomEvent('quickrun-status', {
        bubbles: true,
        detail: state,
      }));
    });
  }

  attributeChangedCallback() {
    if (this.shadowRoot) this.#render();
  }

  get port() {
    return Number(this.getAttribute('port') || DEFAULT_PORT);
  }

  get target() {
    return {
      repo: this.getAttribute('repo') ?? '',
      ref: this.getAttribute('ref'),
      pr: this.getAttribute('pr'),
      config: this.getAttribute('run-cfg') ?? this.getAttribute('config'),
    };
  }

  /**
   * Does what a press does, so a page can trigger it from its own control.
   *
   * The event goes out first and can be prevented: a page that wants to ask something of its own
   * before QuickRun opens - a licence, a warning, its own dialog - has somewhere to do that.
   */
  async run() {
    const target = this.target;

    if (!target.repo) {
      this.#say('no repository');
      return;
    }

    const event = new CustomEvent('quickrun-run', {
      bubbles: true,
      cancelable: true,
      detail: { target },
    });

    if (!this.dispatchEvent(event)) return;

    if (this.getAttribute('mode') === 'link') {
      location.href = `${RUN_PAGE}?${carry(target)}`;
      return;
    }

    this.#button.disabled = true;

    try {
      const state = await ping(this.port);

      // Not installed: the run page explains what to install and carries the repository with it,
      // so the press is not lost on the way.
      if (!state.running) {
        location.href = `${RUN_PAGE}?${carry(target)}&executeQuickRun=true`;
        return;
      }

      const how = await handOver(target, this.port);

      this.dispatchEvent(new CustomEvent('quickrun-handover', {
        bubbles: true,
        detail: { target, how },
      }));
    } finally {
      this.#button.disabled = false;
    }
  }

  #render() {
    // Decided here rather than in the constructor: a page that builds a button with
    // createElement - which is what every framework does - sets its attributes afterwards, and a
    // constructor has already run by then. Reading it in the constructor worked in hand-written
    // markup and silently did nothing everywhere else.
    const styled = !this.hasAttribute('unstyled');

    if (styled && !this.#style.isConnected) this.shadowRoot.prepend(this.#style);
    if (!styled && this.#style.isConnected) this.#style.remove();

    const icon = this.getAttribute('icon') ?? 'left';
    const mark = this.shadowRoot.querySelector('.icon');

    if (mark) mark.style.display = icon === 'none' ? 'none' : '';
    this.#button.dataset.icon = icon;

    this.#label.textContent = this.getAttribute('label')
      ?? (this.textContent.trim() || 'Run this');
  }

  #say(text) {
    this.#label.textContent = text;
  }
}

/**
 * Whether QuickRun is on this machine, as a line of text.
 *
 * Built on the one endpoint that answers any page on purpose, and it reveals nothing else: no
 * repository names, no paths, no runs. A page can use it to decide between "press Run" and "get
 * QuickRun first" rather than finding out after the click.
 */
class QuickRunStatus extends HTMLElement {
  connectedCallback() {
    // Moving an element in the DOM disconnects and reconnects it, and a second attachShadow on the
    // same host throws.
    if (this.shadowRoot) return;

    const root = this.attachShadow({ mode: 'open' });

    root.innerHTML = `<style>
      :host { display: inline-block; font: inherit; color: var(--quickrun-fg, inherit); }
      span { display: inline-flex; align-items: center; gap: 6px; }
      .icon { width: .8em; height: .8em; }
    </style><span part="status">${MARK}<span part="label" class="label">…</span></span>`;

    const label = root.querySelector('.label');

    ping(Number(this.getAttribute('port') || DEFAULT_PORT)).then((state) => {
      label.textContent = state.running
        ? (this.getAttribute('running') ?? `QuickRun ${state.version ?? ''}`.trim())
        : (this.getAttribute('missing') ?? 'QuickRun not running');

      this.dataset.running = state.running ? '1' : '';

      this.dispatchEvent(new CustomEvent('quickrun-status', { bubbles: true, detail: state }));
    });
  }
}

if (!customElements.get('quickrun-btn')) customElements.define('quickrun-btn', QuickRunButton);
if (!customElements.get('quickrun-status')) customElements.define('quickrun-status', QuickRunStatus);

export { QuickRunButton, QuickRunStatus, ping, carry, handOver };
