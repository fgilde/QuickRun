#!/bin/sh
# Builds both extension targets from the single source tree in src/.
#
# Chrome, Edge and Opera consume the Chromium build unchanged. Firefox needs
# browser_specific_settings and a non-module background script entry, so its manifest is patched
# rather than maintained separately - one source, two manifests.
#
# Usage: build.sh [version]
set -eu

version=${1:-$(python -c "import json,io;print(json.load(io.open('src/manifest.json',encoding='utf-8'))['version'])")}

rm -rf dist
mkdir -p dist/chromium dist/firefox

cp -r src/. dist/chromium/
cp -r src/. dist/firefox/

python - "$version" <<'PY'
import io, json, sys

version = sys.argv[1]

for target in ("chromium", "firefox"):
    path = f"dist/{target}/manifest.json"
    manifest = json.load(io.open(path, encoding="utf-8"))
    manifest["version"] = version

    if target == "firefox":
        # Firefox MV3 uses an event page, not a service worker, and requires an explicit id.
        manifest["background"] = {"scripts": ["background.js"], "type": "module"}
        manifest["browser_specific_settings"] = {
            "gecko": {"id": "quickrun@fgilde.org", "strict_min_version": "121.0"}
        }
        # Firefox has no Private Network Access gate, so localhost needs no extra permission dance.

    io.open(path, "w", encoding="utf-8", newline="\n").write(json.dumps(manifest, indent=2) + "\n")
    print(f"wrote {path}")
PY

# python's zipfile rather than the zip binary, which is not present on every developer machine
python - "$version" <<'PY'
import io, os, sys, zipfile

version = sys.argv[1]

for target in ("chromium", "firefox"):
    root = os.path.join("dist", target)
    archive = os.path.join("dist", f"quickrun-{target}-{version}.zip")

    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for directory, _, files in os.walk(root):
            for name in files:
                full = os.path.join(directory, name)
                bundle.write(full, os.path.relpath(full, root).replace(os.sep, "/"))

    print(f"packed {archive}")
PY

echo
echo "built:"
ls -1 dist/*.zip
echo
echo "load unpacked for testing:"
echo "  Chrome/Edge : chrome://extensions -> Developer mode -> Load unpacked -> extension/dist/chromium"
echo "  Firefox     : about:debugging -> This Firefox -> Load Temporary Add-on -> extension/dist/firefox/manifest.json"
