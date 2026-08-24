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
