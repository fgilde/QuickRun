#!/bin/sh
# Wraps the Safari build in the Xcode project Safari insists on.
#
# Safari has no "load unpacked" that survives a restart: an extension is delivered inside a native
# app, and the only thing that turns a folder of JavaScript into that app is Apple's packager, which
# is part of Xcode. So this script exists on macOS and nowhere else, and it says so rather than
# producing half an artifact somewhere it cannot finish.
#
# Apple renamed the tool from safari-web-extension-converter to safari-web-extension-packager, so
# both names are tried - an older Xcode only has the first, a newer one only the second.
#
# Usage: build-safari.sh [version] [output-dir]
set -eu

cd "$(dirname "$0")/.."

version=${1:-}
outdir=${2:-safari}

if [ "$(uname -s)" != "Darwin" ]; then
  echo "build-safari.sh runs on macOS only: the packager ships with Xcode and there is no" >&2
  echo "port of it. Run 'sh extension/build.sh' anywhere to get extension/dist/safari, then" >&2
  echo "either hand that folder to this script on a Mac or upload it to the Safari Web" >&2
  echo "Extension Packager in App Store Connect, which needs no Mac at all." >&2
  exit 1
fi

command -v xcrun >/dev/null 2>&1 || {
  echo "xcrun is missing - install Xcode from the App Store, then run" >&2
  echo "'sudo xcode-select --switch /Applications/Xcode.app' so xcrun finds the packager." >&2
  exit 1
}

packager=safari-web-extension-packager
xcrun --find "$packager" >/dev/null 2>&1 || packager=safari-web-extension-converter
xcrun --find "$packager" >/dev/null 2>&1 || {
  echo "neither safari-web-extension-packager nor safari-web-extension-converter is in this" >&2
  echo "Xcode. Safari web extension support arrived in Xcode 12; update Xcode." >&2
  exit 1
}

sh extension/build.sh ${version:+"$version"}

rm -rf "$outdir"

# --copy-resources, or the project only references extension/dist/safari - and dist/ is rebuilt from
# scratch by the next build.sh, which would leave the project pointing at files that no longer exist.
# --macos-only, because the confirmation window is the whole security model and iOS Safari supports
# no windows.create at all, so an iOS build would ship a Run button that can approve nothing.
xcrun "$packager" extension/dist/safari \
  --project-location "$outdir" \
  --app-name QuickRun \
  --bundle-identifier org.fgilde.quickrun.safari \
  --swift \
  --macos-only \
  --copy-resources \
  --no-open \
  --no-prompt \
  --force

# The packager only writes the project. Building it is what produces something installable, and an
# unsigned build is still useful: Safari runs it once "Allow unsigned extensions" is ticked, which
# is exactly the beta-testing path Apple documents. A signature for everyone else needs a Developer
# ID, which is a credential this script deliberately does not require.
if [ -n "${SKIP_XCODEBUILD:-}" ]; then
  echo "SKIP_XCODEBUILD set - the Xcode project is in $outdir and was not built"
  exit 0
fi

# Found rather than assumed: the packager decides where inside --project-location the project lands
# and has moved it between Xcode versions, and a wrong hard-coded path would fail a release for a
# directory name.
project=$(find "$outdir" -maxdepth 3 -name '*.xcodeproj' -print | head -n 1)
[ -n "$project" ] || {
  echo "the packager wrote no .xcodeproj under $outdir" >&2
  exit 1
}

# -alltargets, not -scheme: the scheme name depends on the platforms the packager generated for
# ("QuickRun" for a macOS-only project, "QuickRun (macOS)" when iOS is there too), and building
# every target needs no guess. SYMROOT so the app has one predictable path to hand to an artifact.
xcodebuild -project "$project" \
  -alltargets \
  -configuration Release \
  SYMROOT="$PWD/$outdir/build" \
  CODE_SIGN_IDENTITY=- \
  CODE_SIGNING_REQUIRED=NO \
  CODE_SIGNING_ALLOWED=NO \
  build

echo
echo "built: $outdir/build/Release/QuickRun.app"
echo "to try it: copy the app to /Applications, run it once, then in Safari"
echo "  Settings -> Developer -> Allow unsigned extensions, and enable QuickRun under Extensions."
