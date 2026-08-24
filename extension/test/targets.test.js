import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

// targets.js is a plain script for the content script's sake, so it is loaded the way a browser
// loads it rather than imported as a module. runInThisContext, not runInNewContext: objects from
// another realm have a different Object prototype and deepStrictEqual would reject them.
const source = readFileSync(fileURLToPath(new URL('../src/targets.js', import.meta.url)), 'utf8');
vm.runInThisContext(source);

const { parseLocation, refFromTreeHref } = globalThis.QuickRunTargets;

test('a repository home page is a run target', () => {
  assert.deepEqual(parseLocation('/acme/app'), { repo: 'acme/app', kind: 'repo' });
});

test('a trailing slash does not change the meaning', () => {
  assert.deepEqual(parseLocation('/acme/app/'), { repo: 'acme/app', kind: 'repo' });
});

test('a tree path carries its ref', () => {
  assert.deepEqual(parseLocation('/acme/app/tree/main'), { repo: 'acme/app', kind: 'tree', ref: 'main' });
});

test('a ref containing slashes is kept whole', () => {
  assert.deepEqual(parseLocation('/acme/app/tree/feature/login'), {
    repo: 'acme/app',
    kind: 'tree',
    ref: 'feature/login',
  });
});

test('a percent-encoded ref is decoded', () => {
  assert.equal(parseLocation('/acme/app/tree/release%2F1.0').ref, 'release/1.0');
});

test('a tree path with a file below the ref keeps the whole tail as the ref', () => {
  // GitHub does not tell us where the ref ends, so the daemon resolves it. Reporting the full tail
  // is honest; guessing a split would silently run the wrong thing.
  assert.equal(parseLocation('/acme/app/tree/main/src').ref, 'main/src');
});

test('a pull request carries its number', () => {
  assert.deepEqual(parseLocation('/acme/app/pull/42'), { repo: 'acme/app', kind: 'pull', pr: 42 });
});

test('a pull request sub-page still resolves to the pull request', () => {
  assert.equal(parseLocation('/acme/app/pull/42/files').pr, 42);
});

test('a non-numeric pull request is rejected', () => {
  assert.equal(parseLocation('/acme/app/pull/not-a-number'), null);
});

test('pull request zero is rejected', () => {
  assert.equal(parseLocation('/acme/app/pull/0'), null);
});

test('the branch list is recognised', () => {
  assert.deepEqual(parseLocation('/acme/app/branches'), { repo: 'acme/app', kind: 'branches' });
});

test('unrelated repository pages yield nothing', () => {
  for (const path of ['/acme/app/issues', '/acme/app/actions', '/acme/app/settings/keys']) {
    assert.equal(parseLocation(path), null, path);
  }
});

test('GitHub own pages are not repositories', () => {
  for (const path of ['/settings/profile', '/notifications', '/explore/x', '/marketplace/y', '/orgs/acme/repositories']) {
    assert.equal(parseLocation(path), null, path);
  }
});

test('a single segment is not a repository', () => {
  assert.equal(parseLocation('/acme'), null);
  assert.equal(parseLocation('/'), null);
  assert.equal(parseLocation(''), null);
});

test('a tree path with no ref falls back to the repository', () => {
  assert.deepEqual(parseLocation('/acme/app/tree'), { repo: 'acme/app', kind: 'repo' });
});

test('refFromTreeHref reads the ref out of a branch row link', () => {
  assert.equal(refFromTreeHref('/acme/app/tree/feature/login'), 'feature/login');
  assert.equal(refFromTreeHref('https://github.com/acme/app/tree/main'), 'main');
});

test('refFromTreeHref decodes and strips query and fragment', () => {
  assert.equal(refFromTreeHref('/acme/app/tree/release%2F2.0?tab=readme'), 'release/2.0');
  assert.equal(refFromTreeHref('/acme/app/tree/main#readme'), 'main');
});

test('refFromTreeHref returns null for a link that is not a tree link', () => {
  assert.equal(refFromTreeHref('/acme/app/commits/main'), null);
  assert.equal(refFromTreeHref(''), null);
  assert.equal(refFromTreeHref(null), null);
});
