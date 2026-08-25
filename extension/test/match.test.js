import { test } from 'node:test';
import assert from 'node:assert/strict';

import { matchesTarget, sameRepo, stillWorthActing } from '../src/match.js';

test('a repository is the same however it was written down', () => {
  assert.ok(sameRepo('acme/app', 'acme/app'));
  assert.ok(sameRepo('https://github.com/acme/app.git', 'acme/app'));
  assert.ok(sameRepo('git@github.com:acme/app.git', 'ACME/App'));
  assert.ok(sameRepo('https://github.com/acme/app/', 'acme/app'));

  assert.ok(!sameRepo('acme/app', 'acme/other'));
  assert.ok(!sameRepo('', ''));
  assert.ok(!sameRepo(undefined, undefined));
});

test('a branch button speaks only for its own branch', () => {
  const run = { repo: 'acme/app', ref: 'feature/login' };

  assert.ok(matchesTarget(run, { repo: 'acme/app', ref: 'feature/login' }));
  assert.ok(!matchesTarget(run, { repo: 'acme/app', ref: 'main' }));

  // No ref means the default branch, and any run of that repository is the only thing it could be.
  assert.ok(matchesTarget(run, { repo: 'acme/app' }));
});

test('a pull request button matches the run of that pull request', () => {
  assert.ok(matchesTarget({ repo: 'acme/app', ref: 'pull/42/head' }, { repo: 'acme/app', pr: 42 }));
  assert.ok(matchesTarget({ repo: 'acme/app', ref: 'refs/pull/42/head' }, { repo: 'acme/app', pr: '42' }));
  assert.ok(!matchesTarget({ repo: 'acme/app', ref: 'pull/7/head' }, { repo: 'acme/app', pr: 42 }));
});

test('a finished run that left processes running is still worth acting on', () => {
  assert.ok(stillWorthActing({ state: 'running' }));
  assert.ok(stillWorthActing({ state: 'stopping' }));
  assert.ok(stillWorthActing({ state: 'awaitingConfirmation' }));

  // The case the whole thing exists for: finished, and still holding a port.
  assert.ok(stillWorthActing({ state: 'succeeded', leftovers: 2 }));

  assert.ok(!stillWorthActing({ state: 'succeeded', leftovers: 0 }));
  assert.ok(!stillWorthActing({ state: 'failed' }));
  assert.ok(!stillWorthActing(null));
});
