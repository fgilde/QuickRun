import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

test('the inline icon is icons/icon-32.png', () => {
  const read = (path) => readFileSync(new URL(`../src/${path}`, import.meta.url));
  const inline = /const ICON = 'data:image\/png;base64,([^']+)'/.exec(read('content.js').toString())?.[1];

  // Replaced the PNG? Paste its new base64 into content.js as well.
  assert.equal(inline, read('icons/icon-32.png').toString('base64'));
});
