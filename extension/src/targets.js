// Works out what a GitHub URL means, with no DOM involved, so it can be tested outside a browser.
//
// This is where pull request numbers and branch refs come from, and getting a ref wrong means
// running the wrong code - so it is kept pure and covered by tests.
//
// A plain script, not a module: MV3 content scripts cannot use import, and a build-time transform
// to strip exports is exactly the kind of cleverness that breaks silently at 3am.

globalThis.QuickRunTargets = (() => {
  const RESERVED_OWNERS = new Set([
    'settings', 'notifications', 'explore', 'marketplace', 'sponsors',
    'topics', 'orgs', 'apps', 'codespaces', 'pulls', 'issues', 'new', 'about',
  ]);

  /**
   * @param {string} pathname a github.com pathname
   * @returns {{repo: string, kind: string, ref?: string, pr?: number}|null}
   */
  function parseLocation(pathname) {
    const parts = (pathname || '').split('/').filter(Boolean).map(decodeSegment);
    if (parts.length < 2) return null;

    const [owner, repo, section, ...rest] = parts;
    if (RESERVED_OWNERS.has(owner.toLowerCase())) return null;
    if (!repo) return null;

    const base = { repo: `${owner}/${repo}` };

    if (!section) return { ...base, kind: 'repo' };

    switch (section) {
      case 'tree': {
        const ref = rest.join('/');
        return ref ? { ...base, kind: 'tree', ref } : { ...base, kind: 'repo' };
      }
      case 'pull': {
        const number = Number(rest[0]);
        return Number.isInteger(number) && number > 0
          ? { ...base, kind: 'pull', pr: number }
          : null;
      }
      case 'branches':
        return { ...base, kind: 'branches' };
      default:
        return null;
    }
  }

  /** The ref a branch-list row points at, taken from its tree link. */
  function refFromTreeHref(href) {
    if (!href) return null;

    const marker = '/tree/';
    const index = href.indexOf(marker);
    if (index === -1) return null;

    const ref = href.slice(index + marker.length).split('?')[0].split('#')[0];
    return ref ? decodeSegment(ref) : null;
  }

  function decodeSegment(segment) {
    try {
      return decodeURIComponent(segment);
    } catch {
      return segment;
    }
  }

  return { parseLocation, refFromTreeHref };
})();
