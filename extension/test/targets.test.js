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

const { parseLocation, refFromTreeHref, originOf } = globalThis.QuickRunTargets;

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

test('a GitLab project keeps its whole group path and ends at /-/', () => {
  const at = (path) => parseLocation(path, 'gitlab.com');
  const repo = 'https://gitlab.com/acme/tools/app';

  assert.deepEqual(at('/acme/tools/app'), { repo, kind: 'repo' });
  assert.deepEqual(at('/acme/tools/app/-/tree/feature/login'), { repo, kind: 'tree', ref: 'feature/login' });
  assert.deepEqual(at('/acme/tools/app/-/merge_requests/7/diffs'), { repo, kind: 'pull', pr: 7 });
  assert.deepEqual(at('/acme/tools/app/-/branches'), { repo, kind: 'branches' });
  assert.equal(at('/acme/tools/app/-/issues'), null);
  assert.equal(at('/explore/projects'), null);
  assert.equal(at('/acme'), null);
});

test('a Bitbucket repository reads its ref from /src/ and /branch/', () => {
  const at = (path) => parseLocation(path, 'bitbucket.org');
  const repo = 'https://bitbucket.org/acme/app';

  assert.deepEqual(at('/acme/app'), { repo, kind: 'repo' });
  assert.deepEqual(at('/acme/app/src/main/'), { repo, kind: 'tree', ref: 'main' });
  assert.deepEqual(at('/acme/app/src'), { repo, kind: 'repo' });
  assert.deepEqual(at('/acme/app/branch/feature/login'), { repo, kind: 'tree', ref: 'feature/login' });
  assert.deepEqual(at('/acme/app/pull-requests/3/overview'), { repo, kind: 'pull', pr: 3 });
  assert.deepEqual(at('/acme/app/branches/'), { repo, kind: 'branches' });
  assert.equal(at('/account/settings'), null);
});

test('an Azure DevOps repository, on both of its hosts', () => {
  const repo = 'https://dev.azure.com/acme/web/_git/app';

  assert.deepEqual(parseLocation('/acme/web/_git/app', 'dev.azure.com'), { repo, kind: 'repo' });
  assert.deepEqual(parseLocation('/acme/web/_git/app', 'dev.azure.com', '?version=GBfeature%2Flogin&path=/src'),
    { repo, kind: 'tree', ref: 'feature/login' });
  assert.deepEqual(parseLocation('/acme/web/_git/app', 'dev.azure.com', '?version=GTv1.0'), { repo, kind: 'repo' });
  assert.deepEqual(parseLocation('/acme/web/_git/app/pullrequest/12', 'dev.azure.com'), { repo, kind: 'pull', pr: 12 });
  assert.deepEqual(parseLocation('/acme/web/_git/app/branches', 'dev.azure.com'), { repo, kind: 'branches' });
  assert.equal(parseLocation('/acme/web/_boards', 'dev.azure.com'), null);

  assert.deepEqual(parseLocation('/web/_git/app', 'acme.visualstudio.com'),
    { repo: 'https://acme.visualstudio.com/web/_git/app', kind: 'repo' });
});

test('refFromTreeHref takes the host marker', () => {
  assert.equal(refFromTreeHref('/acme/app/-/tree/feature/x?ref_type=heads', '/-/tree/'), 'feature/x');
  assert.equal(refFromTreeHref('/acme/app/branch/renovate/node-24.x', '/branch/'), 'renovate/node-24.x');
});

test('a self-hosted server is read as the kind it was added as', () => {
  const gitlab = { type: 'gitlab', origin: 'http://git.example.org' };
  assert.deepEqual(parseLocation('/team/app/-/merge_requests/4', 'git.example.org', '', gitlab),
    { repo: 'http://git.example.org/team/app', kind: 'pull', pr: 4 });

  const azure = { type: 'azure', origin: 'https://tfs.example.org' };
  assert.deepEqual(parseLocation('/tfs/Main/Web/_git/app', 'tfs.example.org', '?version=GBdev', azure),
    { repo: 'https://tfs.example.org/tfs/Main/Web/_git/app', kind: 'tree', ref: 'dev' });
});

test('originOf takes a server however it was typed', () => {
  assert.equal(originOf('git.example.org'), 'https://git.example.org');
  assert.equal(originOf('  https://git.example.org/some/path '), 'https://git.example.org');
  assert.equal(originOf('http://git.example.org:8080'), 'http://git.example.org:8080');

  for (const bad of ['', 'nonsense', 'ftp://git.example.org', 'javascript:alert(1)']) {
    assert.equal(originOf(bad), null, bad);
  }
});
