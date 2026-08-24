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

  /** The pull request header's action area, where Code and the merge button live. */
  function pullRequestActions() {
    const header = document.querySelector('[data-testid="issue-header"], .gh-header-actions');
    if (!header) return null;

    return firstVisible(header, [
      '[data-component="PageHeader.Actions"]',
      '[data-component="PageHeader.ContextAreaActions"]',
      '[class*="buttonContainer"]',
      '[class*="menuActionsContainer"]',
    ]) ?? (visible(header) ? header : null);
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

  return { commonRow, visible, firstVisible, repoToolbar, pullRequestActions, branchRows };
})();
