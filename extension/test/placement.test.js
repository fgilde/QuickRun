import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

// placement.js is a plain script, like the browser loads it. targets.js has to come first: the
// manifest loads them in that order and placement uses it.
for (const file of ['../src/targets.js', '../src/placement.js']) {
  vm.runInThisContext(readFileSync(fileURLToPath(new URL(file, import.meta.url)), 'utf8'));
}

const { commonRow } = globalThis.QuickRunPlacement;

/** The two DOM methods commonRow uses, and nothing else. */
function node(name, children = []) {
  const self = {
    name,
    children,
    parentElement: null,
    contains(other) {
      if (other === self) return true;
      return self.children.some((child) => child.contains(other));
    },
  };
  for (const child of children) child.parentElement = self;
  return self;
}

test('finds the row that holds both elements', () => {
  const branch = node('branch');
  const search = node('search');
  const row = node('row', [node('left', [branch]), node('right', [search])]);
  const root = node('root', [row]);

  assert.equal(commonRow(branch, search, root), row);
});

test('returns the nearest such ancestor, not the outermost', () => {
  const a = node('a');
  const b = node('b');
  const inner = node('inner', [a, b]);
  const outer = node('outer', [inner]);
  const root = node('root', [outer]);

  assert.equal(commonRow(a, b, root), inner);
});

test('stops at the root rather than walking out of the page', () => {
  const branch = node('branch');
  const stranger = node('stranger');
  const root = node('root', [node('row', [branch]), stranger]);

  assert.equal(commonRow(branch, stranger, root), null);
});

test('an element whose parent already contains the other is matched immediately', () => {
  const branch = node('branch');
  const search = node('search');
  const row = node('row', [branch, search]);
  const root = node('root', [row]);

  assert.equal(commonRow(branch, search, root), row);
});

test('a missing starting point yields nothing rather than throwing', () => {
  assert.equal(commonRow(null, node('x'), node('root')), null);
  assert.equal(commonRow(undefined, node('x'), node('root')), null);
});

test('an element with no parent yields nothing', () => {
  const orphan = node('orphan');
  assert.equal(commonRow(orphan, node('x'), node('root')), null);
});

// --- picking a container that is actually on screen -----------------------------------------

const { visible, firstVisible } = globalThis.QuickRunPlacement;

/** A candidate element: `shown` decides whether it has layout boxes. */
function box(selector, shown) {
  return {
    selector,
    getClientRects: () => (shown ? [{ width: 100, height: 30 }] : []),
  };
}

/** A root whose querySelectorAll answers from a fixed selector map. */
function root(map) {
  return { querySelectorAll: (selector) => map[selector] ?? [] };
}

test('an element GitHub has hidden for this viewport does not count as visible', () => {
  assert.equal(visible(box('a', false)), false);
  assert.equal(visible(box('a', true)), true);
  assert.equal(visible(null), false);
});

test('skips a hidden container and takes the next visible one', () => {
  const hidden = box('first', false);
  const shown = box('second', true);

  assert.equal(firstVisible(root({ first: [hidden], second: [shown] }), ['first', 'second']), shown);
});

test('selector order wins when several are visible', () => {
  const first = box('first', true);
  const second = box('second', true);

  assert.equal(firstVisible(root({ first: [first], second: [second] }), ['first', 'second']), first);
});

test('a container with several copies takes the visible copy, not the first', () => {
  const narrow = box('actions', false);
  const wide = box('actions', true);

  assert.equal(firstVisible(root({ actions: [narrow, wide] }), ['actions']), wide);
});

test('all candidates hidden yields nothing rather than a hidden element', () => {
  assert.equal(firstVisible(root({ actions: [box('actions', false)] }), ['actions']), null);
});

test('no candidates at all yields nothing', () => {
  assert.equal(firstVisible(root({}), ['nothing']), null);
});
