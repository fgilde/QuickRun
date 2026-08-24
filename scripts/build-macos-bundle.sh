#!/bin/sh
# Wraps the quickrun binary in a minimal QuickRun.app.
#
# macOS registers URL schemes through CFBundleURLTypes in an Info.plist, which
# only exists inside an app bundle - a bare single-file binary cannot claim
# quickrun://. The bundle is otherwise identical to the plain binary, and the
# plain binary stays available for CLI-only use.
#
# Usage: build-macos-bundle.sh <binary> <output-dir> <version>
set -eu

binary=${1:?binary path required}
outdir=${2:?output directory required}
version=${3:?version required}

app="$outdir/QuickRun.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

cp "$binary" "$app/Contents/MacOS/quickrun"
chmod +x "$app/Contents/MacOS/quickrun"

if [ -f assets/icon.png ]; then
  cp assets/icon.png "$app/Contents/Resources/icon.png"
fi

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>QuickRun</string>
  <key>CFBundleDisplayName</key>
  <string>QuickRun</string>
  <key>CFBundleIdentifier</key>
  <string>org.fgilde.quickrun</string>
  <key>CFBundleVersion</key>
  <string>${version}</string>
  <key>CFBundleShortVersionString</key>
  <string>${version}</string>
  <key>CFBundleExecutable</key>
  <string>quickrun</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <!-- No dock icon: QuickRun is a background daemon with a browser UI. -->
  <key>LSUIElement</key>
  <true/>
  <key>CFBundleURLTypes</key>
  <array>
    <dict>
      <key>CFBundleURLName</key>
      <string>QuickRun</string>
      <key>CFBundleURLSchemes</key>
      <array>
        <string>quickrun</string>
      </array>
    </dict>
  </array>
</dict>
</plist>
PLIST

echo "built $app"
