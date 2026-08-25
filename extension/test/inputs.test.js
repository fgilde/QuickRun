import { test } from 'node:test';
import assert from 'node:assert/strict';

// A document just big enough for the form builder. Not a DOM: enough of one to assert what the
// fields end up being, which is the part that has to be right.
globalThis.document = {
  createElement(tag) {
    return {
      tag,
      children: [],
      className: '',
      textContent: '',
      value: '',
      checked: false,
      type: tag === 'input' ? 'text' : undefined,
      append(...nodes) {
        this.children.push(...nodes);
      },
    };
  },
};

const { inputForm } = await import('../src/inputs.js');

const fields = (form) => form.element.children.map((field) => field.children);
const controlOf = (form, index) => fields(form)[index].find((child) => child.tag !== 'span');

test('a config without inputs has no form', () => {
  assert.equal(inputForm({ inputs: [] }), null);
  assert.equal(inputForm({}), null);
});

test('a text input starts from the supplied value, then the default', () => {
  const withValue = inputForm({
    inputs: [{ id: 'name', type: 'text', default: 'from-default' }],
    values: { name: 'from-run' },
  });
  assert.equal(controlOf(withValue, 0).value, 'from-run');

  const withDefault = inputForm({ inputs: [{ id: 'name', type: 'text', default: 'from-default' }] });
  assert.equal(controlOf(withDefault, 0).value, 'from-default');
});

test('a select offers the options and preselects the value', () => {
  const form = inputForm({
    inputs: [{
      id: 'mode',
      type: 'select',
      default: 'fast',
      options: [{ value: 'fast', label: 'Fast' }, { value: 'slow', label: null }],
    }],
  });

  const select = controlOf(form, 0);
  assert.equal(select.tag, 'select');
  assert.deepEqual(select.children.map((o) => o.value), ['fast', 'slow']);
  assert.deepEqual(select.children.map((o) => o.textContent), ['Fast', 'slow']);
  assert.equal(select.value, 'fast');
});

test('a bool becomes a checkbox that reads back as a string', () => {
  const form = inputForm({ inputs: [{ id: 'clean', type: 'bool', default: 'true' }] });
  const box = controlOf(form, 0);

  assert.equal(box.type, 'checkbox');
  assert.equal(box.checked, true);
  assert.deepEqual(form.values(), { clean: 'true' });

  box.checked = false;
  assert.deepEqual(form.values(), { clean: 'false' });
});

/** A secret never travels back to the page, so its field cannot start from one. */
test('a password field starts empty even when the run has a value', () => {
  const form = inputForm({
    inputs: [{ id: 'apiKey', type: 'password', default: 'nope' }],
    values: { apiKey: 'should-not-appear' },
  });

  const input = controlOf(form, 0);
  assert.equal(input.type, 'password');
  assert.equal(input.value, '');
});

test('labels and descriptions are text, never markup', () => {
  const form = inputForm({
    inputs: [{
      id: 'x',
      label: '<img src=x onerror=alert(1)>',
      description: '<script>alert(2)</script>',
      type: 'text',
      required: true,
      env: 'X_VALUE',
    }],
  });

  const parts = fields(form)[0];
  const texts = parts.flatMap((part) => [part.textContent, ...(part.children ?? []).map((c) => c.textContent)]);

  assert.ok(texts.includes('<img src=x onerror=alert(1)>'));
  assert.ok(texts.includes('<script>alert(2)</script>'));
  assert.ok(texts.includes('passed to the run as X_VALUE'));
  assert.ok(texts.includes('required'));
});

test('dirty says whether anything was changed', () => {
  const form = inputForm({ inputs: [{ id: 'name', type: 'text', default: 'a' }] });
  assert.equal(form.dirty(), false);

  controlOf(form, 0).value = 'b';
  assert.equal(form.dirty(), true);
  assert.deepEqual(form.values(), { name: 'b' });
});

test('settle takes the values without touching the fields', () => {
  const form = inputForm({ inputs: [{ id: 'name', type: 'text', default: 'a' }] });
  const control = controlOf(form, 0);

  control.value = 'b';
  form.settle();

  // Nothing pending any more, and the field itself is untouched - the cursor stays where it is.
  assert.equal(form.dirty(), false);
  assert.equal(control.value, 'b');

  control.value = 'c';
  assert.equal(form.dirty(), true);
});
