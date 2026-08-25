// The form a config's `inputs` block asks for.
//
// Built with DOM calls rather than markup: a label, a description and the options of a select are
// all repository content, and none of it may become HTML.

/**
 * Renders the fields for a run's inputs.
 *
 * @returns {{element: HTMLElement, values: () => Record<string, string>, dirty: () => boolean}|null}
 *   null when the config declares no inputs.
 */
export function inputForm(run) {
  const defs = run.inputs ?? [];
  if (defs.length === 0) return null;

  const supplied = run.values ?? {};
  const element = document.createElement('div');
  element.className = 'inputs';

  const controls = new Map();
  const initial = new Map();

  for (const def of defs) {
    const field = document.createElement('label');
    field.className = 'field';

    const name = document.createElement('span');
    name.className = 'field-label';
    name.textContent = def.label || def.id;

    if (def.required) {
      const required = document.createElement('span');
      required.className = 'required';
      required.textContent = 'required';
      name.append(' ', required);
    }

    field.append(name);

    const control = controlFor(def, supplied[def.id]);
    controls.set(def.id, control);
    initial.set(def.id, read(control));
    field.append(control);

    for (const note of [def.description, def.env ? `passed to the run as ${def.env}` : null]) {
      if (!note) continue;
      const hint = document.createElement('span');
      hint.className = 'hint';
      hint.textContent = note;
      field.append(hint);
    }

    element.append(field);
  }

  const values = () => Object.fromEntries([...controls].map(([id, control]) => [id, read(control)]));

  return {
    element,
    values,
    dirty: () => [...controls].some(([id, control]) => read(control) !== initial.get(id)),
  };
}

function read(control) {
  return control.type === 'checkbox' ? String(control.checked) : control.value;
}

function controlFor(def, value) {
  if (def.type === 'select') {
    const select = document.createElement('select');

    for (const option of def.options ?? []) {
      const item = document.createElement('option');
      item.value = option.value;
      item.textContent = option.label || option.value;
      select.append(item);
    }

    select.value = value ?? def.default ?? def.options?.[0]?.value ?? '';
    return select;
  }

  if (def.type === 'bool') {
    const box = document.createElement('input');
    box.type = 'checkbox';
    box.checked = (value ?? def.default ?? '') === 'true';
    return box;
  }

  const input = document.createElement('input');
  input.autocomplete = 'off';
  input.spellcheck = false;
  input.type = def.type === 'password' ? 'password' : def.type === 'number' ? 'number' : 'text';

  // A secret is never sent back to the page, so its field starts empty even when a value is set.
  input.value = def.type === 'password' ? '' : value ?? def.default ?? '';

  if (def.type === 'number') {
    if (def.min !== null && def.min !== undefined) input.min = def.min;
    if (def.max !== null && def.max !== undefined) input.max = def.max;
  }

  if (def.pattern) input.pattern = def.pattern;

  input.placeholder = { path: 'a path', dir: 'a directory', file: 'a file' }[def.type] ?? '';

  return input;
}
