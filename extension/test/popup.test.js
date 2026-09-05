import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

/**
 * What the popup says about versions.
 *
 * It named only the version of the tool it had reached, which is the one that is never in question:
 * it answered, so it is current. The extension is the half that lags - a store takes days to
 * approve one - and a window missing something that shipped weeks ago cannot be diagnosed without
 * knowing which build drew it. Answering that took reading a git tag once.
 */
const source = readFileSync(new URL('../src/popup.js', import.meta.url), 'utf8');

test('the popup names its own version, not only the one it connected to', () => {
  assert.match(source, /chrome\.runtime\.getManifest\(\)\.version/,
    'the popup never asks which extension it is');

  // Both, and labelled, or the two numbers side by side say nothing about which is which.
  assert.match(source, /extension \$\{MINE\}/);
  assert.match(source, /connected to QuickRun \$\{state\.version\}/);
});

test('it says which extension it is even when nothing is running', () => {
  // The case where knowing the version matters most: nothing answered, and the question is whether
  // this extension is too old to have asked properly.
  const notRunning = source.slice(source.indexOf('default:'));

  assert.match(notRunning, /extension \$\{MINE\}/);
});
