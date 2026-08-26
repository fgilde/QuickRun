import { test, before } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

/**
 * The manifests as they are actually shipped.
 *
 * Both of these are store submissions, and a store rejects an upload for a missing line without
 * telling anyone until the upload. The Firefox one did: "The data_collection_permissions property is
 * missing", after the release had been built. These read the built files, because the built files
 * are what gets uploaded.
 */

const extension = dirname(dirname(fileURLToPath(import.meta.url)));

// dist/ is not in the repository - it is what the build produces - so the build runs if it has not.
before(() => {
  if (existsSync(join(extension, 'dist', 'firefox', 'manifest.json'))) return;
  execFileSync('sh', ['build.sh', '0.0.0-test'], { cwd: extension, stdio: 'pipe' });
});

const read = (path) => JSON.parse(readFileSync(join(extension, path), 'utf8'));

test('the firefox manifest declares that no data is collected', () => {
  const manifest = read('dist/firefox/manifest.json');
  const gecko = manifest.browser_specific_settings?.gecko;

  assert.ok(gecko, 'browser_specific_settings.gecko is missing');

  // Under gecko, not at the top level, and "none" is exclusive - anything alongside it is invalid.
  assert.deepEqual(gecko.data_collection_permissions, { required: ['none'] });
  assert.ok(gecko.id, 'the add-on needs a stable id');
});

test('both manifests ask only for what the extension uses', () => {
  for (const target of ['chromium', 'firefox']) {
    const manifest = read(`dist/${target}/manifest.json`);

    assert.equal(manifest.manifest_version, 3, target);
    assert.deepEqual(manifest.permissions.sort(), ['storage'], target);

    // Loopback only, by both names. A host permission for anything else would be a new review
    // question every release, and there is nothing else to talk to.
    assert.deepEqual(manifest.host_permissions.sort(),
      ['http://127.0.0.1/*', 'http://localhost/*'], target);
  }
});
