import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

// Loaded the way the browser loads it, like targets.test.js: a plain script, no module wrapper.
const source = readFileSync(fileURLToPath(new URL('../src/targets.js', import.meta.url)), 'utf8');
vm.runInThisContext(source);

const { parseAutorun } = globalThis.QuickRunTargets;

test('an ordinary address asks for nothing', () => {
  assert.equal(parseAutorun(''), null);
  assert.equal(parseAutorun('?tab=readme'), null);
  assert.equal(parseAutorun('?q=executeQuickRun'), null);
});

test('the bare parameter means the config the repository would use', () => {
  assert.deepEqual(parseAutorun('?executeQuickRun'), { config: null });
  assert.deepEqual(parseAutorun('?executeQuickRun='), { config: null });
  assert.deepEqual(parseAutorun('?executeQuickRun=true'), { config: null });
  assert.deepEqual(parseAutorun('?executeQuickRun=1'), { config: null });
  assert.deepEqual(parseAutorun('?tab=readme&executeQuickRun=yes'), { config: null });
});

test('the name is read however it is capitalised', () => {
  assert.deepEqual(parseAutorun('?executequickrun=true'), { config: null });
  assert.deepEqual(parseAutorun('?ExecuteQuickRun=TRUE'), { config: null });
});

test('switching it off is the same as not asking', () => {
  assert.equal(parseAutorun('?executeQuickRun=false'), null);
  assert.equal(parseAutorun('?executeQuickRun=0'), null);
});

test('a config file is taken as the config to use', () => {
  assert.deepEqual(parseAutorun('?executeQuickRun=other.yml'), { config: 'other.yml' });
  assert.deepEqual(parseAutorun('?executeQuickRun=ci/demo.yaml'), { config: 'ci/demo.yaml' });
  assert.deepEqual(parseAutorun('?executeQuickRun=A.YML'), { config: 'A.YML' });
});

test('a percent-encoded name arrives decoded', () => {
  assert.deepEqual(parseAutorun('?executeQuickRun=ci%2Fdemo.yml'), { config: 'ci/demo.yml' });
});

/**
 * The value comes out of an address somebody else may have written, and it decides which file
 * QuickRun opens. Everything that is not a file inside the repository is refused here - and again
 * by the daemon, which is the one that actually opens it.
 */
test('anything that is not a file in the repository is refused', () => {
  for (const value of [
    '/etc/passwd.yml',
    '../outside.yml',
    'ci/../../outside.yml',
    './quickrun.yml',
    'C:/Windows/win.yml',
    'https://evil.example.com/run.yml',
    'quickrun.txt',
    'quickrun.yml.exe',
  ]) {
    const answer = parseAutorun(`?executeQuickRun=${encodeURIComponent(value)}`);
    assert.ok(answer?.error, `${value} should have been refused`);
    assert.equal(answer.config, undefined);
  }
});

test('a name with a control character is refused', () => {
  const answer = parseAutorun('?executeQuickRun=run%00.yml');
  assert.ok(answer?.error);
});

test('a very long name is refused', () => {
  const answer = parseAutorun(`?executeQuickRun=${'a'.repeat(300)}.yml`);
  assert.ok(answer?.error);
});

test('the last one wins when the parameter is repeated', () => {
  assert.deepEqual(parseAutorun('?executeQuickRun=true&executeQuickRun=ci/demo.yml'),
    { config: 'ci/demo.yml' });
});
