#!/bin/sh
# QuickRun installer for Linux and macOS.
#
#   curl -fsSL https://quickrun.org/install.sh | sh
#
# Downloads the latest release asset for this platform, verifies it against the
# published SHA256SUMS, and installs it to ~/.local/bin (override with PREFIX).
#
# On macOS, prefer `brew install fgilde/tap/quickrun`: Homebrew strips the
# quarantine attribute, so Gatekeeper does not block the binary. This script
# removes it explicitly for the same reason.
set -eu

REPO=fgilde/QuickRun
PREFIX=${PREFIX:-$HOME/.local/bin}
BASE="https://github.com/$REPO/releases/latest/download"

die() { printf 'error: %s\n' "$1" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || die "$1 is required"; }

need curl
need tar

case "$(uname -s)" in
  Linux)  os=linux ;;
  Darwin) os=osx ;;
  *) die "unsupported operating system: $(uname -s)" ;;
esac

case "$(uname -m)" in
  x86_64|amd64) arch=x64 ;;
  aarch64|arm64) arch=arm64 ;;
  *) die "unsupported architecture: $(uname -m)" ;;
esac

rid="$os-$arch"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

# Asset names carry no version, so the download URL is fixed and no API call is needed.
asset="quickrun-$rid.tar.gz"

printf 'fetching checksums\n'
curl -fsSL "$BASE/SHA256SUMS" -o "$tmp/SHA256SUMS" \
  || die "could not download SHA256SUMS - is there a published release yet?"

printf 'downloading %s\n' "$asset"
curl -fsSL "$BASE/$asset" -o "$tmp/$asset"

printf 'verifying checksum\n'
expected=$(awk -v a="$asset" '$2 == a { print $1 }' "$tmp/SHA256SUMS")
[ -n "$expected" ] || die "no checksum for $asset"

if command -v sha256sum >/dev/null 2>&1; then
  actual=$(sha256sum "$tmp/$asset" | cut -d' ' -f1)
elif command -v shasum >/dev/null 2>&1; then
  actual=$(shasum -a 256 "$tmp/$asset" | cut -d' ' -f1)
else
  die "need sha256sum or shasum to verify the download"
fi

[ "$actual" = "$expected" ] || die "checksum mismatch - refusing to install
  expected $expected
  actual   $actual"

tar -xzf "$tmp/$asset" -C "$tmp"
[ -f "$tmp/quickrun" ] || die "archive did not contain a quickrun binary"

mkdir -p "$PREFIX"
install -m 0755 "$tmp/quickrun" "$PREFIX/quickrun"

# Unsigned binary: without this macOS refuses to run a downloaded file at all.
if [ "$os" = osx ]; then
  xattr -d com.apple.quarantine "$PREFIX/quickrun" 2>/dev/null || true
fi

# Records how QuickRun got here, so auto-update knows it owns this binary even when PREFIX points
# somewhere a package manager would normally own, such as /usr/bin.
# XDG_DATA_HOME, not XDG_CONFIG_HOME: this must match .NET's LocalApplicationData, which QuickRun
# reads, and that is ~/.local/share.
config="${XDG_DATA_HOME:-$HOME/.local/share}/QuickRun"
mkdir -p "$config"
printf 'standalone\n' > "$config/install-source"

printf '\ninstalled %s to %s\n' "$asset" "$PREFIX/quickrun"

case ":$PATH:" in
  *":$PREFIX:"*) ;;
  *) printf '\nwarning: %s is not on your PATH\n  add: export PATH="%s:$PATH"\n' "$PREFIX" "$PREFIX" ;;
esac

printf '\nnext: quickrun install    # register quickrun:// and start the daemon\n'
