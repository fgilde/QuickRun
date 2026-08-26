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
  edge: null,
  firefox: null,
};

/** The listing a browser installs from, or null while there is none to send anyone to. */
export function listingFor(browser) {
  if (browser === 'opera') return STORES.chrome;
  return STORES[browser] ?? null;
}
