// Talking to a QuickRun on the reader's own machine, from a page that is not allowed to.
//
// The daemon accepts requests from a browser extension or from something with no origin at all -
// never from a web page, whoever serves it. That is deliberate and it stays: if quickrun.org could
// drive the local listener, so could any site that got a script onto it.
//
// So a page can do exactly two things, and both are here:
//
//   answering()  - ask whether anything is listening, and learn nothing else. A no-cors request
//                  comes back opaque; that it came back at all is the whole answer.
//   present()    - ask the local QuickRun to open its own window on a plan. Works only where the
//                  reader has this site in their trusted list, which quickrun.org is by default:
//                  the site the application was downloaded from. Refused anywhere else, and the
//                  refusal is not an error - it is the answer, and open() takes over.
//   open()       - hand a target over the way that always works. With QuickRun running that is a
//                  tab on its own page, which shows the plan and asks; without it, the quickrun://
//                  scheme, which starts it first.
//
// None of them can start anything. What arrives on the other side is a plan waiting for a person -
// and present() is a shortcut to the same window, not a way around it.

export const DEFAULT_PORT = 9876;

/**
 * Whether a QuickRun is listening here.
 *
 * Two attempts. A current QuickRun lets a page read /api/ping, which is the better answer; an older
 * one does not, and then an opaque response is still evidence that something is there. Anything
 * else - nothing listening, a firewall - throws, and the answer is no.
 */
export async function answering(port = DEFAULT_PORT) {
  const url = `http://127.0.0.1:${port}/api/ping`;

  try {
    return (await fetch(url, { cache: 'no-store' })).ok;
  } catch {
    // Not readable: either nothing is there, or it is a version that does not let a page read it.
  }

  try {
    await fetch(url, { mode: 'no-cors', cache: 'no-store' });
    return true;
  } catch {
    return false;
  }
}

/**
 * The query a run target becomes, from the parts a page may name.
 *
 * `fromCollection` names a source, not a config: the link says "use the one QuickRun keeps for this
 * repository", and QuickRun fetches it itself. The commands never travel in a URL - a link that
 * could carry commands would be a link that can put commands in front of somebody.
 */
export function carry({ repo, ref = null, pr = null, file = null, fromCollection = false }) {
  if (file) return `file=${encodeURIComponent(file)}`;

  const parts = [`repo=${encodeURIComponent(repo)}`];
  if (ref) parts.push(`ref=${encodeURIComponent(ref)}`);
  if (pr) parts.push(`pr=${encodeURIComponent(pr)}`);
  if (fromCollection) parts.push('config=collection');
  return parts.join('&');
}

/** Where a target goes: QuickRun's own page when it is running, the scheme when it is not. */
export function targetFor(running, target, port = DEFAULT_PORT) {
  const query = carry(target);

  return running
    ? `http://127.0.0.1:${port}/#run?${query}`
    : `quickrun://run?${query}`;
}

/**
 * Asks the local QuickRun to open its window on this target.
 *
 * Only a site in the reader's trusted list gets an answer here, so a refusal is expected and means
 * nothing is wrong. Returns true when the window was opened and there is nothing left to do.
 */
export async function present(target, { port = DEFAULT_PORT } = {}) {
  try {
    const answer = await fetch(`http://127.0.0.1:${port}/api/show?${carry(target)}`, {
      method: 'POST',
      mode: 'cors',
      cache: 'no-store',
    });

    // Not trusted here answers 403, and an older QuickRun answers it too - both mean "use the link".
    if (!answer.ok) return false;

    return (await answer.json())?.shown === true;
  } catch {
    // Nothing listening, or the browser refused the cross-origin request. Either way: the link.
    return false;
  }
}

/**
 * Hands a target over, by whichever way works.
 *
 * The window first, where the reader trusts this site: they stay on this page and the plan appears
 * beside it. Otherwise the way that has always worked. Navigating rather than opening a tab for the
 * fallback is deliberate - after waiting for the answer above, the click is over as far as the
 * browser is concerned, and a blocked popup looks exactly like nothing happening.
 */
export async function hand(running, target, { port = DEFAULT_PORT } = {}) {
  if (running && await present(target, { port })) return 'window';

  const url = targetFor(running, target, port);

  // A tab is still friendlier where the browser allows one this late. Where it does not, window.open
  // returns nothing rather than throwing, and navigating is what is left.
  if (running) {
    const tab = window.open(url, '_blank', 'noopener');
    if (tab) return 'tab';
  }

  location.href = url;
  return running ? 'tab' : 'scheme';
}

/**
 * Hands a target to QuickRun. The plan opens there; nothing runs until it is confirmed there.
 *
 * @param sameTab Navigate instead of opening a tab. For a click a new tab is friendlier - this page
 *   stays where it is - but a browser blocks window.open that no click asked for, so anything
 *   automatic has to navigate.
 */
export function open(running, target, { sameTab = false, port = DEFAULT_PORT } = {}) {
  const url = targetFor(running, target, port);

  // The scheme always navigates: a blank tab is all that would be left of it where no handler picks
  // it up.
  if (sameTab || !running) location.href = url;
  else window.open(url, '_blank', 'noopener');

  return url;
}
