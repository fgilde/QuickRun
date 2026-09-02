import { test, before } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

/**
 * The manifests as they are actually shipped.
 *
 * All three of these are store submissions, and a store rejects an upload for a missing line
 * without telling anyone until the upload. The Firefox one did: "The data_collection_permissions
 * property is missing", after the release had been built. These read the built files, because the
 * built files are what gets uploaded.
 */

const TARGETS = ['chromium', 'firefox', 'safari'];

const extension = dirname(dirname(fileURLToPath(import.meta.url)));

// dist/ is not in the repository - it is what the build produces - so the build runs if it has not.
before(() => {
  if (TARGETS.every((target) => existsSync(join(extension, 'dist', target, 'manifest.json')))) return;
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

test('the safari manifest keeps loopback reachable from the background', () => {
  const manifest = read('dist/safari/manifest.json');

  // Not a service worker, deliberately. Safari does not grant an MV3 service worker the
  // cross-origin access that host_permissions gives, so a fetch from it to http://127.0.0.1 is
  // refused as "not allowed by Access-Control-Allow-Origin" - which is every feature this
  // extension has. A background page does get that access.
  assert.equal(manifest.background.service_worker, undefined);
  assert.deepEqual(manifest.background.scripts, ['background.js']);
  assert.equal(manifest.background.type, 'module');

  // 16.4 or nothing: storage.session and an ES module background page both arrived there, and
  // installing on an older Safari would fail at the first click instead of at install time.
  assert.equal(manifest.browser_specific_settings.safari.strict_min_version, '16.4');

  // Safari ignores the gecko key, but the App Store review reads the manifest: a Firefox-only
  // block in a Safari submission is a question nobody wants to answer.
  assert.equal(manifest.browser_specific_settings.gecko, undefined);
});

test('every manifest asks only for what the extension uses', () => {
  for (const target of TARGETS) {
    const manifest = read(`dist/${target}/manifest.json`);

    assert.equal(manifest.manifest_version, 3, target);
    assert.deepEqual(manifest.permissions.sort(), ['storage'], target);

    // Loopback only, by both names. A host permission for anything else would be a new review
    // question every release, and there is nothing else to talk to.
    assert.deepEqual(manifest.host_permissions.sort(),
      ['http://127.0.0.1/*', 'http://localhost/*'], target);
  }
});
