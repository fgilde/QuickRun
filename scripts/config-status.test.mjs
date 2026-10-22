import assert from 'node:assert/strict';
import { test } from 'node:test';
import { pick, record, skipped } from './config-status.mjs';

test('never-tested configs come first, then the longest ago, and skipped ones never', () => {
  const status = { 'a/old.yml': { date: '2026-01-01' }, 'a/new.yml': { date: '2026-09-01' } };
  const skip = skipped('# heavy\na/heavy.yml\n');

  assert.deepEqual(
    pick(['a/heavy.yml', 'a/new.yml', 'a/old.yml', 'b/untested.yml'], status, skip, 3),
    ['b/untested.yml', 'a/old.yml', 'a/new.yml']);
});

test('a result replaces the earlier one and a pass drops the old error', () => {
  const before = { 'a/x.yml': { ok: false, date: '1', error: 'boom' } };
  const after = record(before, [{ config: 'a/x.yml', ok: true, date: '2', commit: 'abc' }]);

  assert.deepEqual(after['a/x.yml'], { ok: true, date: '2', commit: 'abc' });
});
