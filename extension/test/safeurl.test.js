import { test } from 'node:test';
import assert from 'node:assert/strict';

import { httpUrl } from '../src/safeurl.js';

test('loopback and public http addresses become links', () => {
  assert.equal(httpUrl('http://localhost:5082'), 'http://localhost:5082/');
  assert.equal(httpUrl('  https://example.com/app  '), 'https://example.com/app');
});

test('anything that is not http becomes nothing', () => {
  // The whole point of the guard: a config-supplied string must not execute.
  assert.equal(httpUrl('javascript:alert(1)'), null);
  assert.equal(httpUrl('file:///C:/Windows'), null);
  assert.equal(httpUrl('data:text/html,<script>x</script>'), null);
  assert.equal(httpUrl('not a url'), null);
  assert.equal(httpUrl(undefined), null);
});
