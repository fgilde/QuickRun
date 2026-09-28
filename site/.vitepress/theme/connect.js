/**
 * The contact and support widgets from connect.gilde.org: loading them, and making them look like
 * this site rather than like a guest on it.
 *
 * The script is 128KB and only two pages need it, so it is fetched when one of those pages is
 * opened rather than in the site's head.
 */
const SOURCE = 'https://connect.gilde.org/widgets/v1.js';

let loading = null;

/** Resolves true once the custom elements are defined, false if the script never arrived. */
export function loadConnect() {
  if (typeof window === 'undefined') return Promise.resolve(false);
  if (customElements.get('gilde-contact')) return Promise.resolve(true);
  if (loading) return loading;

  loading = new Promise((done) => {
    const script = document.createElement('script');
    script.type = 'module';
    script.src = SOURCE;
    script.onload = () => done(true);
    script.onerror = () => done(false);
    document.head.append(script);
  });

  return loading;
}

/**
 * What the widgets are painted with here.
 *
 * They draw into a shadow root, so a stylesheet on the page cannot reach them - but the page can
 * hand one over. Everything below is the widget's own variables pointed at this site's, which is
 * why these panels also follow the light/dark switch in the header: the widget's own "auto" reads
 * the operating system, and someone who turned this page light would otherwise get a dark panel
 * sitting in it.
 *
 * The selectors repeat the widget's own so the specificity matches; adopted sheets are applied
 * after the one the widget brings, so ours is the one that lands.
 */
const SKIN = `
  :host { font-family: inherit; }

  .surface,
  .surface[data-theme=auto],
  .surface[data-theme=dark],
  .surface[data-theme=light] {
    /* The widget writes the project's own accent onto this element as an inline style, which
       no ordinary rule can outrank. */
    --accent: var(--vp-c-brand-1) !important;
    --accent-ink: #fff;
    --bg: var(--vp-c-bg-soft);
    --raised: var(--vp-c-bg);
    --text: var(--vp-c-text-1);
    --muted: var(--vp-c-text-2);
    --line: var(--vp-c-divider);
    --input: var(--vp-c-bg);
    color-scheme: inherit;
  }

  /* The page already puts these in a layout. A drop shadow and a coloured edge on top of that is
     a second frame around the first. */
  .card { box-shadow: none; border: 1px solid var(--line); }
  .bar { display: none; }

  h2 { letter-spacing: -.02em; }
  button.primary { border-radius: 999px; }
  .support-link { border-radius: 14px; }

  /* The project marks are dark artwork on a transparent background, which vanishes on a dark page.
     A light tile behind them is what the QR codes beside them already are. */
  .support-icon { width: 34px; height: 34px; padding: 4px; background: #fff; border-radius: 9px; }
  .footer { padding-top: 16px; padding-bottom: 16px; }
`;

let sheet = null;

/** Hands the skin to one widget. Safe to call again: a sheet is only adopted once. */
export function skinConnect(element) {
  const root = element?.shadowRoot;
  if (!root) return false;

  if (!sheet) {
    sheet = new CSSStyleSheet();
    sheet.replaceSync(SKIN);
  }

  if (!root.adoptedStyleSheets.includes(sheet)) {
    root.adoptedStyleSheets = [...root.adoptedStyleSheets, sheet];
  }

  return true;
}

/**
 * Skins every widget on the page once it has a shadow root.
 *
 * The elements are upgraded when the script arrives, which is not the same moment Vue puts them in
 * the page, so this keeps looking for a short while rather than assuming one order.
 */
export function skinAll(selector = 'gilde-contact, gilde-support') {
  let tries = 0;

  const attempt = () => {
    const elements = [...document.querySelectorAll(selector)];
    const missing = elements.filter((element) => !skinConnect(element));

    if ((missing.length > 0 || elements.length === 0) && (tries += 1) < 20) {
      setTimeout(attempt, 50);
    }
  };

  attempt();
}
