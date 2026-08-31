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

  /**
   * What `?executeQuickRun` in the address asks for, so a link can do what the button does.
   *
   * It opens the confirmation window - the same window the button opens, with the same command list
   * and the same Run to press. A link that started commands by itself would be a one-click way to
   * run a stranger's code, so the parameter saves a click on the page and none of the deciding.
   *
   * `?executeQuickRun` or `=true` means the config the repository would use anyway; a file name
   * means that config instead. The name is checked here so a typo says so rather than travelling to
   * the daemon - which checks it again, because a value out of an address is a stranger's string.
   *
   * @returns null when not asked for, {config} when it is, or {error} when the value is unusable.
   */
  function parseAutorun(search) {
    const query = new URLSearchParams(search ?? '');

    let value = null;
    let asked = false;

    for (const [key, raw] of query.entries()) {
      if (key.toLowerCase() !== 'executequickrun') continue;
      asked = true;
      value = (raw ?? '').trim();
    }

    if (!asked) return null;
    if (value === '' || ['true', '1', 'yes', 'on'].includes(value.toLowerCase())) return { config: null };
    if (['false', '0', 'no', 'off'].includes(value.toLowerCase())) return null;

    if (!/\.ya?ml$/i.test(value)) return { error: 'executeQuickRun needs a .yml file' };
    if (value.length > 200) return { error: 'that config name is too long' };
    if (/[\u0000-\u001f\u007f]/.test(value)) return { error: 'that config name is not a file name' };
    if (value.includes('://')) return { error: 'a config is a file in the repository, not a URL' };

    // Anchored anywhere but the repository root, or stepping out of it, is not a config of this
    // repository - and it is the shape an attacker would reach for first. A drive letter and a home
    // directory are anchors too, and neither leaves an empty first segment to catch them by.
    if (/^[a-z]:/i.test(value) || value.startsWith('~'))
      return { error: 'a config is named relative to the repository root' };

    const segments = value.split(/[\\/]/);
    if (segments.some((s) => s === '' || s === '.' || s === '..'))
      return { error: 'a config is named relative to the repository root' };

    return { config: value };
  }

  return { parseLocation, refFromTreeHref, parseAutorun };
})();
