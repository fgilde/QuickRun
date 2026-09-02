// What a run was answered with, in the words whoever answered it chose.
//
// Once a run is going its form is gone - it has to be, the values are applied - and with it went
// the only record of what was picked. Two runs of the same repository then looked identical on
// screen while doing entirely different things, which is the question a log window gets opened to
// answer: "which database did this one start with?"
//
// Its own module because it is a rule rather than a rendering, and because the interesting half of
// it is what it refuses to say: a password is never printed. The daemon does not even send one - a
// secret comes back with a null value on purpose - so a secret field is reported as being there
// and nothing more.

/**
 * One line per input the config declared, in its order.
 *
 * @param run The run as the daemon reports it: `inputs` are the definitions, `values` what they
 *   hold, with a secret's value nulled by the daemon.
 * @returns `{label, text, secret}` per input. `secret` means "do not present this as a value" -
 *   set for a password and for a field nobody filled in, both of which are styled quietly.
 */
export function chosenValues(run) {
  const defs = run?.inputs ?? [];
  const values = run?.values ?? {};

  return defs.map((def) => {
    const raw = values[def.id];
    const label = def.label || def.id;

    // Never the value, whatever arrived here. The daemon nulls it, and if a future one ever stopped
    // doing that, this still would not print it.
    if (def.type === 'password') return { label, text: 'hidden', secret: true };

    if (def.type === 'bool') return { label, text: raw === 'true' ? 'yes' : 'no' };

    // The option's label, not its value. A value is often a flag - "-v --remove-orphans" - while
    // the label is the sentence somebody read before choosing it.
    if (def.type === 'select') {
      const option = (def.options ?? []).find((o) => o.value === raw);
      return { label, text: option?.label || option?.value || String(raw ?? '') };
    }

    const empty = raw === null || raw === undefined || raw === '';
    return { label, text: empty ? '(empty)' : String(raw), secret: empty };
  });
}
