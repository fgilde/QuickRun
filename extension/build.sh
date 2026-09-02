#!/bin/sh
# Builds every extension target from the single source tree in src/.
#
# Chrome, Edge and Opera consume the Chromium build unchanged. Firefox and Safari each need a
# different background entry and their own browser_specific_settings, so their manifests are
# patched rather than maintained separately - one source, three manifests.
#
# Usage: build.sh [version]
set -eu

# Anchor to this script's directory: run from the repository root, `cp -r src/.` would copy the
# .NET source tree instead of the extension.
cd "$(dirname "$0")"

# Ubuntu images ship python3 without always aliasing `python`.
PY=$(command -v python3 || command -v python) || {
  echo "python3 is required" >&2
  exit 1
}

version=${1:-$($PY -c "import json,io;print(json.load(io.open('src/manifest.json',encoding='utf-8'))['version'])")}

rm -rf dist
mkdir -p dist/chromium dist/firefox dist/safari

cp -r src/. dist/chromium/
cp -r src/. dist/firefox/
cp -r src/. dist/safari/

"$PY" - "$version" <<'PY'
import io, json, sys

version = sys.argv[1]

for target in ("chromium", "firefox", "safari"):
    path = f"dist/{target}/manifest.json"
    manifest = json.load(io.open(path, encoding="utf-8"))
    manifest["version"] = version

    if target == "firefox":
        # Firefox MV3 uses an event page, not a service worker, and requires an explicit id.
        manifest["background"] = {"scripts": ["background.js"], "type": "module"}
        manifest["browser_specific_settings"] = {
            "gecko": {
                "id": "quickrun@fgilde.org",
                "strict_min_version": "121.0",
                # addons.mozilla.org refuses a new submission without this, and it belongs under
                # gecko - declared at the root it is silently ignored and the upload still fails.
                # "none" says what is true: QuickRun talks to 127.0.0.1 and collects nothing, and
                # "none" must be the only entry. Firefox below 140 treats the key as an unknown
                # sub-key and installs anyway, so strict_min_version stays where it is.
                "data_collection_permissions": {"required": ["none"]},
            }
        }
        # Firefox has no Private Network Access gate, so localhost needs no extra permission dance.

    if target == "safari":
        # Safari does not give an MV3 background service worker the cross-origin access that
        # host_permissions grants - a fetch from it to http://127.0.0.1 is refused with "Origin
        # safari-web-extension://... is not allowed by Access-Control-Allow-Origin", which would
        # break the ping, every run and the log stream. A background page does get that access, so
        # Safari runs the same background.js as an event page, exactly as Firefox does.
        manifest["background"] = {"scripts": ["background.js"], "type": "module"}
        # 16.4 is the first Safari with storage.session - where the pending run is parked while the
        # confirmation window opens - and with background.type for an ES module background page.
        # Without the floor an older Safari installs this and then fails at the first click, which
        # is a worse answer than refusing to install.
        manifest["browser_specific_settings"] = {"safari": {"strict_min_version": "16.4"}}

    io.open(path, "w", encoding="utf-8", newline="\n").write(json.dumps(manifest, indent=2) + "\n")
    print(f"wrote {path}")
PY

# python's zipfile rather than the zip binary, which is not present on every developer machine
"$PY" - "$version" <<'PY'
import io, os, sys, zipfile

version = sys.argv[1]

for target in ("chromium", "firefox", "safari"):
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
# Safari's temporary install needs no Xcode project, which is the fastest way to see this build.
# dist/safari is also what scripts/build-safari.sh hands to the Xcode packager.
echo "  Safari      : Settings -> Developer -> Allow unsigned extensions, then Add Temporary Extension -> extension/dist/safari"
