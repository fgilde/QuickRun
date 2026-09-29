// Where the button goes on each kind of page, per host.
//
// GitHub's class names are hashed per deploy (OverviewContent-module__Box_3__wzlJx) and its pages
// are rendered client-side, so nothing here anchors on styling. Every lookup is semantic - the
// element that links to a branch, the row containing it, the region holding the page actions -
// and every one of them fails silently: a missing button is acceptable, a broken GitHub page is
// not.

globalThis.QuickRunPlacement = (() => {
  /**
   * The nearest ancestor of `from` that also contains `also`. Used to find the toolbar row without
   * knowing what GitHub currently calls it.
   */
  function commonRow(from, also, root = document.body) {
    let node = from?.parentElement;
    while (node && node !== root) {
      if (node.contains(also)) return node;
      node = node.parentElement;
    }
    return null;
  }

  /**
   * Whether an element is actually on screen.
   *
   * GitHub renders several copies of its header actions and hides all but the one that fits the
   * viewport - the desktop copy of PageHeader.ContextAreaActions carries data-hidden-regular.
   * Appending into a hidden copy injects a button nobody can see.
   */
  function visible(element) {
    return Boolean(element) && element.getClientRects().length > 0;
  }

  /** The first candidate that is both present and on screen. */
  function firstVisible(root, selectors) {
    for (const selector of selectors) {
      for (const candidate of (root ?? document).querySelectorAll(selector)) {
        if (visible(candidate)) return candidate;
      }
    }
    return null;
  }

  /** The repository toolbar: the row holding the branch selector and the file search. */
  function repoToolbar() {
    const branch = document.querySelector(
      '[data-testid="anchor-button"], #ref-picker-repos-header-ref-selector, '
      + '#branch-picker-repos-header-ref-selector',
    );
    if (!branch) return null;

    const search = document.querySelector(
      'input[aria-label="Go to file"], [data-testid="go-to-file-button"], #go-to-file-button',
    );

    // With the search box present the shared row is the real toolbar. Without it, the branch
    // selector's grandparent is the closest thing to it.
    return (search && commonRow(branch, search)) ?? branch.parentElement?.parentElement ?? null;
  }

  /**
   * The pull request header's action area, where the merge status and Code buttons live.
   *
   * Searched from the document rather than from a header element: the pull request view has no
   * single stable header container - the logged-out issue viewer uses data-testid="issue-header",
   * the pull request page uses none of it. PH_Actions is the region that actually holds the
   * buttons, and it is not the same thing as PageHeader.Actions.
   */
  function pullRequestActions() {
    return firstVisible(document, [
      '[data-component="PH_Actions"]',
      '[data-component="PageHeader.Actions"]',
      '[data-testid="issue-header"] [class*="buttonContainer"]',
      '[data-component="PageHeader.ContextAreaActions"]',
      '.gh-header-actions',
    ]);
  }

  /**
   * One entry per branch row. Keyed on the link to the branch rather than the row's markup, which
   * has changed shape more than once.
   */
  function branchRows(repo) {
    const links = document.querySelectorAll(`a[href*="/${repo}/tree/"]`);
    const seen = new Set();
    const rows = [];

    for (const link of links) {
      const row = link.closest('tr, li, [role="row"]');
      if (!row || seen.has(row)) continue;

      const ref = QuickRunTargets.refFromTreeHref(link.getAttribute('href'));
      if (!ref) continue;

      seen.add(row);

      // The branch name's own cell, next to the copy button - not the action cell at the end.
      // That table is a CSS grid whose last column is 70px wide, and a button placed there
      // overflows it and gives the whole table a horizontal scrollbar.
      const cell = link.closest('td') ?? row;
      const group = cell.querySelector('[class*="ActionGroup"]') ?? cell;

      rows.push({ ref, anchor: group });
    }

    return rows;
  }

  /** The path of a repository as its own links spell it: `/acme/app` out of any form of `repo`. */
  function pathOf(repo) {
    try {
      return new URL(repo).pathname.replace(/\/+$/, '');
    } catch {
      return `/${repo}`;
    }
  }

  /** One entry per row that links to a branch, placed right after that link. */
  function rowsOf(links, refOf) {
    const seen = new Set();
    const rows = [];

    for (const link of links) {
      const row = link.closest('tr, li, [role="row"]');
      if (!row || seen.has(row)) continue;

      const ref = refOf(link);
      if (!ref) continue;

      seen.add(row);
      rows.push({ ref, anchor: link.parentElement ?? row });
    }

    return rows;
  }

  const github = { repoToolbar, pullRequestActions, branchRows };

  // gitlab.com and a self-hosted GitLab draw the same pages.
  const gitlab = {
    // Holds Find file and the Code dropdown on the project page and in the file view.
    repoToolbar: () => document.querySelector('[data-testid="tree-controls-container"]'),
    pullRequestActions: () => firstVisible(document, [
      '.detail-page-header .js-issuable-actions',
      '.detail-page-header',
    ]),
    branchRows: (repo) => rowsOf(
      document.querySelectorAll(`[data-testid="branch-container"] a[href*="${pathOf(repo)}/-/tree/"]`),
      (link) => QuickRunTargets.refFromTreeHref(link.getAttribute('href'), '/-/tree/'),
    ),
  };

  const bitbucket = {
    // The group beside the repository name: Pull requests, Clone, Repository actions.
    repoToolbar: () =>
      document.querySelector('[data-testid="repo-actions-menu--trigger"]')?.closest('[role="group"]')
      ?? document.querySelector('[data-qa="page-header-wrapper"] [role="group"]'),
    pullRequestActions: () =>
      document.querySelector('[data-qa="pr-header-actions-drop-down-menu-styles"]')?.closest('[role="group"]')
      ?? document.querySelector('[data-testid="pr-header"] [role="group"]'),
    branchRows: (repo) => rowsOf(
      document.querySelectorAll(`a[href*="${pathOf(repo)}/branch/"]`),
      (link) => QuickRunTargets.refFromTreeHref(link.getAttribute('href'), '/branch/'),
    ),
  };

  // Not checked against a live page: Azure DevOps shows nothing to a visitor who is not signed in.
  // Azure DevOps Server draws the same pages, so these serve it too.
  // The header command bar is the one container every Repos page shares.
  const azure = {
    repoToolbar: () => firstVisible(document, [
      '.repos-files-header .bolt-header-commandbar',
      '.bolt-header-commandbar',
    ]),
    pullRequestActions: () => firstVisible(document, [
      '.repos-pr-header .bolt-header-commandbar',
      '.bolt-header-commandbar',
    ]),
    branchRows: (repo) => rowsOf(
      document.querySelectorAll(`a[href*="${pathOf(repo)}?"][href*="version=GB"]`),
      (link) => QuickRunTargets.refFromVersion(new URL(link.href, location.href).search),
    ),
  };

  /**
   * Bitbucket Cloud publishes no ref for a pull request, so its button runs the source branch - of
   * the fork it came from, when it came from one. The header names it as `owner/repo:branch`.
   */
  function bitbucketPullRequestSource(repo) {
    const text = document.querySelector(
      '[data-qa="pr-branches-and-state-styles"] [role="presentation"] span[aria-hidden="true"]',
    )?.textContent?.trim();
    if (!text) return null;

    const colon = text.lastIndexOf(':');
    if (colon === -1) return { repo, ref: text };

    const ref = text.slice(colon + 1);
    return ref ? { repo: `${new URL(repo).origin}/${text.slice(0, colon)}`, ref } : null;
  }

  /** The placements for a kind of site, as QuickRunTargets.typeOf or a self-hosted entry names it. */
  function forType(type) {
    return { gitlab, bitbucket, azure }[type] ?? github;
  }

  return {
    commonRow, visible, firstVisible, repoToolbar, pullRequestActions, branchRows,
    forType, bitbucketPullRequestSource,
  };
})();
