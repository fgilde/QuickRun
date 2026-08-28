#!/bin/sh
# Generates the scoop manifest, the Homebrew formula and the Homebrew cask for a release.
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
app_x64="QuickRun-osx-x64.app.zip"
app_arm64="QuickRun-osx-arm64.app.zip"
linux_x64="quickrun-linux-x64.tar.gz"
linux_arm64="quickrun-linux-arm64.tar.gz"

cat > "$outdir/quickrun.json" <<JSON
{
  "version": "$version",
  "description": "Run any git repository with one click",
  "homepage": "https://quickrun.org",
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
# Homebrew formula for QuickRun. Served from the project site, so no tap is needed:
#   brew install https://quickrun.org/quickrun.rb
#
# If a fgilde/homebrew-tap repository exists, copying this file into its Formula/
# directory also makes "brew install fgilde/tap/quickrun" work, with upgrade tracking.
class Quickrun < Formula
  desc "Run any git repository with one click"
  homepage "https://quickrun.org"
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

# The cask, which is what a Mac actually wants: a formula installs a binary into the Cellar, so
# nothing appears in Launchpad, nothing has an icon, and quickrun:// cannot be claimed - a URL scheme
# lives in an app bundle's Info.plist. The cask installs the bundle into /Applications and links the
# binary inside it onto the PATH, so the terminal command and the app are the same install.
cat > "$outdir/quickrun-cask.rb" <<CASK
cask "quickrun" do
  arch arm: "arm64", intel: "x64"

  version "$version"
  sha256 arm:   "$(sum_for "$app_arm64")",
         intel: "$(sum_for "$app_x64")"

  url "$BASE/QuickRun-osx-#{arch}.app.zip",
      verified: "github.com/$REPO/"
  name "QuickRun"
  desc "Run any git repository with one click"
  homepage "https://quickrun.org"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on macos: ">= :monterey"

  app "QuickRun.app"
  # The command line and the app are one install: this is the same binary the bundle runs.
  binary "#{appdir}/QuickRun.app/Contents/MacOS/quickrun"

  postflight do
    # The binaries are unsigned, so Gatekeeper would refuse the first launch with "damaged".
    # Removing the download flag from a bundle the user explicitly asked Homebrew to install is
    # what every unsigned cask does, and it is visible here rather than hidden in a support script.
    system_command "/usr/bin/xattr",
                   args: ["-dr", "com.apple.quarantine", "#{appdir}/QuickRun.app"],
                   sudo: false

    # Tells auto-update that Homebrew owns this install, so QuickRun reports new versions rather
    # than replacing a file Homebrew is tracking.
    support = Pathname.new("#{Dir.home}/Library/Application Support/QuickRun")
    support.mkpath
    (support/"install-source").write("brew\n")
  end

  zap trash: [
    "~/Library/Application Support/QuickRun",
    "~/Library/LaunchAgents/org.fgilde.quickrun.plist",
  ]
end
CASK

echo "wrote $outdir/quickrun.json, $outdir/quickrun.rb and $outdir/quickrun-cask.rb"
