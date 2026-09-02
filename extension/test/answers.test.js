import { test } from 'node:test';
import assert from 'node:assert/strict';

import { chosenValues } from '../src/answers.js';

/**
 * What the window says a run was answered with.
 *
 * The reason this exists: once a run starts, its form is replaced by a log, and there was nothing
 * left on screen saying which values it had been given - so two runs of the same repository looked
 * identical while doing different things.
 *
 * The reason it is tested: one of these branches must never print what it is given.
 */

const run = (inputs, values) => ({ inputs, values });

test('a password is reported as being there and never as a value', () => {
  const [answered] = chosenValues(run(
    [{ id: 'apiKey', label: 'API key', type: 'password' }],
    // The daemon nulls a secret. Given one anyway - a future daemon, a replayed response - this
    // still must not put it on screen.
    { apiKey: 'sk-do-not-print-me' },
  ));

  assert.equal(answered.label, 'API key');
  assert.equal(answered.text, 'hidden');
  assert.equal(answered.secret, true);
  assert.ok(!JSON.stringify(answered).includes('sk-do-not-print-me'));
});

test('a choice is reported by the sentence somebody picked, not by its value', () => {
  const [answered] = chosenValues(run(
    [{
      id: 'database',
      label: 'Database',
      type: 'select',
      options: [
        { value: '--remove-orphans', label: 'Keep the existing data' },
        { value: '-v --remove-orphans', label: 'Start over' },
      ],
    }],
    { database: '-v --remove-orphans' },
  ));

  // The value is a docker flag. Printing that instead would be technically true and useless.
  assert.equal(answered.text, 'Start over');
});

test('a choice whose value is not among the options still says what it was', () => {
  const [answered] = chosenValues(run(
    [{ id: 'mode', type: 'select', options: [{ value: 'fast', label: 'Fast' }] }],
    { mode: 'careful' },
  ));

  assert.equal(answered.text, 'careful');
});

test('an option with no label falls back to its value', () => {
  const [answered] = chosenValues(run(
    [{ id: 'mode', type: 'select', options: [{ value: 'fast' }] }],
    { mode: 'fast' },
  ));

  assert.equal(answered.text, 'fast');
});

test('a switch reads as yes or no', () => {
  const defs = [{ id: 'seed', label: 'Seed demo data', type: 'bool' }];

  assert.equal(chosenValues(run(defs, { seed: 'true' }))[0].text, 'yes');
  assert.equal(chosenValues(run(defs, { seed: 'false' }))[0].text, 'no');

  // Nothing supplied is not "yes": a switch nobody set is off.
  assert.equal(chosenValues(run(defs, {}))[0].text, 'no');
});

test('a field nobody filled in says so quietly', () => {
  const defs = [{ id: 'note', label: 'Note', type: 'text' }];

  for (const values of [{ note: '' }, { note: null }, {}]) {
    const [answered] = chosenValues(run(defs, values));

    assert.equal(answered.text, '(empty)');
    assert.equal(answered.secret, true, 'an empty field is styled quietly rather than as a value');
  }
});

test('a field with no label is named by its id', () => {
  assert.equal(chosenValues(run([{ id: 'PORT', type: 'text' }], { PORT: '3000' }))[0].label, 'PORT');
});

test('the order is the order the config declares', () => {
  const answered = chosenValues(run(
    [{ id: 'a', type: 'text' }, { id: 'b', type: 'text' }, { id: 'c', type: 'text' }],
    { a: '1', b: '2', c: '3' },
  ));

  assert.deepEqual(answered.map((v) => v.label), ['a', 'b', 'c']);
});

test('a run that was asked nothing has nothing to say', () => {
  // The section has to disappear rather than stand there empty, which is what an empty list means
  // to both windows.
  assert.deepEqual(chosenValues(run([], {})), []);
  assert.deepEqual(chosenValues({}), []);
  assert.deepEqual(chosenValues(undefined), []);
});
