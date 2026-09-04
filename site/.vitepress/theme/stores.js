// Where each browser's store listing lives, once it exists.
//
// Kept apart from the card that renders it so that publishing a listing is one line and one deploy,
// with nothing else to remember. A listing that is submitted but not yet public still belongs here
// as null: a button leading to a 404 is worse than an honest "review pending", and an add-on under
// review is not reachable at its future address.
//
// Opera installs from the Chrome Web Store - "Install Chrome Extensions" is a first-party Opera
// add-on and the route Opera itself recommends - so it points at the Chrome listing rather than
// waiting for its own.
export const STORES = {
  chrome: null,
  edge: 'https://microsoftedge.microsoft.com/addons/detail/quickrun/dbnknhijahmiildfabckibabpieobnhd',
  // Without a language in the path: AMO sends the reader to their own.
  firefox: 'https://addons.mozilla.org/firefox/addon/quickrun/',
  // Safari takes an extension inside a signed native app, and there is no such app to point at yet.
  // The build is downloadable and Safari loads it temporarily, which is what its card offers.
  safari: null,
};

/** The listing a browser installs from, or null while there is none to send anyone to. */
export function listingFor(browser) {
  if (browser === 'opera') return STORES.chrome;
  return STORES[browser] ?? null;
}

/**
 * Which browser is reading this page, as one of the names above.
 *
 * User agent strings, because that is all a page gets: Edge and Opera both claim to be Chrome and
 * add a token of their own, so the specific ones have to be ruled out first. Null for anything the
 * list does not cover, because guessing wrong is worse than saying nothing.
 */
export function currentBrowser(agent = typeof navigator === 'undefined' ? '' : navigator.userAgent) {
  if (/OPR\//.test(agent) || /Opera/.test(agent)) return 'opera';
  if (/Edg\//.test(agent)) return 'edge';
  if (/Firefox\//.test(agent)) return 'firefox';

  // Chromium-based and none of the above.
  if (/Chrome\//.test(agent) || /Chromium\//.test(agent)) return 'chrome';

  // Safari last, and only after every Chromium browser has been ruled out: all of them carry
  // "Safari" in their agent string, so testing for it earlier would read Chrome as Safari and hand
  // a Chrome user a build that will not load in their browser.
  if (/Safari\//.test(agent)) return 'safari';

  return null;
}
