// Where the button goes on each kind of GitHub page.
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

  /** The pull request header's action area, where Code and the merge button live. */
  function pullRequestActions() {
    return document.querySelector('[data-component="PageHeader.ContextAreaActions"]')
      ?? document.querySelector('[data-testid="issue-header"] [data-component="PageHeader.Actions"]')
      ?? document.querySelector('[data-testid="issue-header"]')
      ?? document.querySelector('.gh-header-actions');
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
      // Last cell where there is one, so the button lines up with the row's other actions.
      rows.push({ ref, anchor: row.querySelector('td:last-child') ?? row });
    }

    return rows;
  }

  return { commonRow, repoToolbar, pullRequestActions, branchRows };
})();
