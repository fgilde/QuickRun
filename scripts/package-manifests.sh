#!/bin/sh
# Generates the scoop manifest and the Homebrew formula for a release.
#
# Both need the release's checksums, so this runs after SHA256SUMS exists.
# winget is not generated here: its manifests live in microsoft/winget-pkgs and
# are submitted by pull request, documented in docs/publishing.md.
#
# Usage: package-manifests.sh <version> <sha256sums-file> <output-dir>
set -eu

version=${1:?version required}
sums=${2:?SHA256SUMS path required}
outdir=${3:?output directory required}

REPO=fgilde/QuickRun
BASE="https://github.com/$REPO/releases/download/v$version"

mkdir -p "$outdir"

sum_for() {
  awk -v a="$1" '$2 == a { print $1 }' "$sums" | head -n 1
}

# Asset names carry no version; the version is in the tag, which BASE already includes.
win_x64="quickrun-win-x64.zip"
win_arm64="quickrun-win-arm64.zip"
osx_x64="quickrun-osx-x64.tar.gz"
osx_arm64="quickrun-osx-arm64.tar.gz"
linux_x64="quickrun-linux-x64.tar.gz"
linux_arm64="quickrun-linux-arm64.tar.gz"

cat > "$outdir/quickrun.json" <<JSON
{
  "version": "$version",
  "description": "Run any git repository with one click",
  "homepage": "https://fgilde.github.io/QuickRun",
  "license": "MIT",
  "architecture": {
    "64bit": {
      "url": "$BASE/$win_x64",
      "hash": "$(sum_for "$win_x64")"
    },
    "arm64": {
      "url": "$BASE/$win_arm64",
      "hash": "$(sum_for "$win_arm64")"
    }
  },
  "bin": "quickrun.exe",
  "post_install": [
    "New-Item -ItemType Directory -Force (Join-Path \$env:LOCALAPPDATA 'QuickRun') | Out-Null",
    "Set-Content -Path (Join-Path \$env:LOCALAPPDATA 'QuickRun/install-source') -Value 'scoop'"
  ],
  "checkver": "github",
  "autoupdate": {
    "architecture": {
      "64bit": { "url": "https://github.com/$REPO/releases/download/v\$version/quickrun-win-x64.zip" },
      "arm64": { "url": "https://github.com/$REPO/releases/download/v\$version/quickrun-win-arm64.zip" }
    }
  }
}
JSON

cat > "$outdir/quickrun.rb" <<RUBY
# Homebrew formula for QuickRun. Lives in the fgilde/homebrew-tap repository:
#   brew install fgilde/tap/quickrun
class Quickrun < Formula
  desc "Run any git repository with one click"
  homepage "https://fgilde.github.io/QuickRun"
  version "$version"
  license "MIT"

  on_macos do
    on_arm do
      url "$BASE/$osx_arm64"
      sha256 "$(sum_for "$osx_arm64")"
    end
    on_intel do
      url "$BASE/$osx_x64"
      sha256 "$(sum_for "$osx_x64")"
    end
  end

  on_linux do
    on_arm do
      url "$BASE/$linux_arm64"
      sha256 "$(sum_for "$linux_arm64")"
    end
    on_intel do
      url "$BASE/$linux_x64"
      sha256 "$(sum_for "$linux_x64")"
    end
  end

  def install
    bin.install "quickrun"
    # Tells auto-update that Homebrew owns this binary, so QuickRun reports new
    # versions instead of overwriting itself.
    (var/"quickrun").mkpath
    (etc/"quickrun/install-source").write("brew\n")
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/quickrun --version")
  end
end
RUBY

echo "wrote $outdir/quickrun.json and $outdir/quickrun.rb"
