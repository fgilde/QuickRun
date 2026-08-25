// Which run belongs to the button that is asking.
//
// A page that was reloaded has forgotten the run it started, so the button asks the daemon "is this
// branch running right now". Answering it means comparing what a run recorded - a URL, a .git
// suffix, whatever case the user typed - against what a GitHub page knows about itself.

/** owner/repo, however it was written down. */
export function sameRepo(left, right) {
  return normalise(left) === normalise(right) && normalise(left).length > 0;
}

function normalise(repo) {
  return String(repo ?? '')
    .trim()
    .replace(/\.git$/i, '')
    .replace(/^[a-z]+:\/\/[^/]+\//i, '')
    .replace(/^git@[^:]+:/i, '')
    .replace(/\/+$/, '')
    .toLowerCase();
}

/**
 * Whether a run is the one this target is about.
 *
 * A branch button speaks only for its own branch: offering to stop a run of a different ref would
 * be worse than offering nothing. A target with no ref is the repository's default branch, and any
 * run of that repository is close enough to be worth showing - there is nothing else it could mean.
 */
export function matchesTarget(run, target) {
  if (!run || !target) return false;
  if (!sameRepo(run.repo, target.repo)) return false;

  if (target.pr) {
    const ref = String(run.ref ?? '');
    return ref === `pull/${target.pr}/head`
      || ref === `refs/pull/${target.pr}/head`
      || ref === String(target.pr);
  }

  if (!target.ref) return true;
  return String(run.ref ?? '') === String(target.ref);
}

/** The states in which a run is worth offering actions for. */
export const ACTIVE_RUN_STATES = ['awaitingConfirmation', 'awaitingInput', 'running', 'stopping'];

/**
 * Whether there is still something to act on: going, or finished with processes it left behind -
 * which is precisely when stopping still matters.
 */
export function stillWorthActing(run) {
  return ACTIVE_RUN_STATES.includes(run?.state) || (run?.leftovers ?? 0) > 0;
}
