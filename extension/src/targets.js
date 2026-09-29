// Works out what a GitHub, GitLab, Bitbucket or Azure DevOps URL means, with no DOM involved, so it can be tested outside a browser.
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
   * Which kind of site a hostname is, for the hosts known without being told. A self-hosted
   * server is whatever the person said it is when they added it.
   *
   * @returns {'github'|'gitlab'|'bitbucket'|'azure'}
   */
  function typeOf(host) {
    const name = (host || '').toLowerCase();
    if (name === 'gitlab.com') return 'gitlab';
    if (name === 'bitbucket.org') return 'bitbucket';
    if (name === 'dev.azure.com' || name.endsWith('.visualstudio.com')) return 'azure';
    return 'github';
  }

  /**
   * What an address on one of the supported hosts points at.
   *
   * GitHub's `repo` stays the `owner/repo` shorthand the daemon has always been sent. Every other
   * host's is the full clone URL, because the daemon reads a bare `owner/repo` as GitHub.
   *
   * @param {string} pathname
   * @param {string} [host] the page's hostname
   * @param {string} [search] the page's query string, which is where Azure DevOps keeps the branch
   * @param {{type: string, origin: string}|null} [custom] a self-hosted server the person added
   * @returns {{repo: string, kind: string, ref?: string, pr?: number}|null}
   */
  function parseLocation(pathname, host = 'github.com', search = '', custom = null) {
    const parts = (pathname || '').split('/').filter(Boolean).map(decodeSegment);
    const origin = custom?.origin ?? `https://${(host || '').toLowerCase()}`;

    switch (custom?.type ?? typeOf(host)) {
      case 'gitlab': return gitlab(parts, origin);
      case 'bitbucket': return bitbucket(parts, origin);
      case 'azure': return azure(parts, origin, search);
      default: return github(parts);
    }
  }

  function github(parts) {
    if (parts.length < 2) return null;

    const [owner, repo, section, ...rest] = parts;
    if (RESERVED_OWNERS.has(owner.toLowerCase())) return null;
    if (!repo) return null;

    return sections({ repo: `${owner}/${repo}` }, section, rest, 'pull');
  }

  const GITLAB_RESERVED = new Set([
    '-', 'explore', 'dashboard', 'users', 'help', 'admin', 'groups', 'search', 'projects', 'api',
  ]);

  /**
   * GitLab nests projects in groups to any depth, and `/-/` is where the project path ends. A path
   * without it is the project's home, or a group page - which has no toolbar, so no button either.
   */
  function gitlab(parts, origin) {
    if (parts.length < 2 || GITLAB_RESERVED.has(parts[0].toLowerCase())) return null;

    const dash = parts.indexOf('-');
    const project = dash === -1 ? parts : parts.slice(0, dash);
    if (project.length < 2) return null;

    const base = { repo: `${origin}/${project.join('/')}` };
    if (dash === -1) return { ...base, kind: 'repo' };

    const [section, ...rest] = parts.slice(dash + 1);
    return sections(base, section, rest, 'merge_requests');
  }

  const BITBUCKET_RESERVED = new Set([
    'account', 'dashboard', 'repo', 'snippets', 'workspace', 'product', 'site', 'socialauth', 'api',
  ]);

  function bitbucket(parts, origin) {
    if (parts.length < 2 || BITBUCKET_RESERVED.has(parts[0].toLowerCase())) return null;

    const [workspace, repo, section, ...rest] = parts;
    const base = { repo: `${origin}/${workspace}/${repo}` };

    // /src/<ref> is the file view, /branch/<name> a branch's own page.
    if (section === 'src' || section === 'branch') return sections(base, 'tree', rest, 'pull-requests');
    return sections(base, section, rest, 'pull-requests');
  }

  /**
   * Azure DevOps: `<org>/<project>/_git/<repo>` on dev.azure.com, `<project>/_git/<repo>` on the
   * older <org>.visualstudio.com, `[tfs/]<collection>/<project>/_git/<repo>` on a server. Whatever
   * comes before `_git` belongs to the clone URL. The branch is not in the path at all but in
   * `?version=GB<name>`.
   */
  function azure(path, origin, search) {
    const git = path.indexOf('_git');
    if (git < 1 || !path[git + 1]) return null;

    const base = { repo: `${origin}/${path.slice(0, git + 2).join('/')}` };
    const [section, number] = path.slice(git + 2);

    if (!section) {
      const ref = refFromVersion(search);
      return ref ? { ...base, kind: 'tree', ref } : { ...base, kind: 'repo' };
    }
    if (section === 'branches') return { ...base, kind: 'branches' };
    if (section === 'pullrequest') return pull(base, number);
    return null;
  }

  /** The part every host shares once the repository is known: a ref, a pull request, the branches. */
  function sections(base, section, rest, pullSection) {
    if (!section) return { ...base, kind: 'repo' };

    switch (section) {
      case 'tree': {
        const ref = rest.join('/');
        return ref ? { ...base, kind: 'tree', ref } : { ...base, kind: 'repo' };
      }
      case pullSection:
        return pull(base, rest[0]);
      case 'branches':
        return { ...base, kind: 'branches' };
      default:
        return null;
    }
  }

  function pull(base, value) {
    const number = Number(value);
    return Number.isInteger(number) && number > 0 ? { ...base, kind: 'pull', pr: number } : null;
  }

  /**
   * The origin of a server somebody typed in: `git.example.org`, with or without a scheme or a
   * path. https unless they said http. Null for anything that is not a web address.
   */
  function originOf(input) {
    const value = String(input ?? '').trim();
    if (!value) return null;

    try {
      const url = new URL(/^[a-z]+:\/\//i.test(value) ? value : `https://${value}`);
      if (url.protocol !== 'https:' && url.protocol !== 'http:') return null;
      if (!url.hostname.includes('.') && url.hostname !== 'localhost') return null;
      return url.origin;
    } catch {
      return null;
    }
  }

  /** Azure DevOps' `version=GB<branch>`. GT is a tag and GC a commit, neither of them a branch. */
  function refFromVersion(search) {
    const version = new URLSearchParams(search ?? '').get('version') ?? '';
    return version.startsWith('GB') && version.length > 2 ? version.slice(2) : null;
  }

  /**
   * The ref a branch-list row points at, taken from its link. The marker is what precedes the ref
   * on that host: /tree/ on GitHub, /-/tree/ on GitLab, /branch/ on Bitbucket.
   */
  function refFromTreeHref(href, marker = '/tree/') {
    if (!href) return null;

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

  return { typeOf, parseLocation, originOf, refFromTreeHref, refFromVersion, parseAutorun };
})();
