// One rule, in one place: a URL that came out of a repository's own config is untrusted.
//
// The address a run reports is read from quickrun.yml, which anyone can write. Turning that string
// into a link without checking its scheme is how "open the app" becomes "run javascript: in the
// extension window", so nothing becomes a link unless it is plain http or https.

export function httpUrl(text) {
  if (typeof text !== 'string') return null;

  let parsed;
  try {
    parsed = new URL(text.trim());
  } catch {
    return null;
  }

  return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? parsed.href : null;
}
