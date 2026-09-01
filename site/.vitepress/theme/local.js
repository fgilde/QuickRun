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
//   open()       - hand a target over. With QuickRun running that is its own page, which shows the
//                  plan and asks; without it, the quickrun:// scheme, which starts it first.
//
// Neither can start anything. What arrives on the other side is a plan waiting for a person.

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
