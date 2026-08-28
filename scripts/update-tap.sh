#!/usr/bin/env bash
#
# Put the current formula and cask into the Homebrew tap, which is a second repository.
#
# A workflow's own GITHUB_TOKEN may only write to the repository it runs in, so pushing here takes
# a credential of its own. Either works:
#
#   TAP_DEPLOY_KEY  an ed25519 private key with write access to fgilde/homebrew-tap. Narrower than
#                   a token - it can reach that one repository and nothing else - and it does not
#                   expire. Added under the tap's Settings, Deploy keys, with write access on.
#   TAP_TOKEN       a personal access token with `repo`. Reaches everything the account can, and
#                   expires.
#
# With neither, this says so and stops: the tap's own hourly sync is then the only thing keeping it
# level, and GitHub runs that when it feels like it.
#
# Usage: update-tap.sh [<directory holding quickrun.rb and quickrun-cask.rb>]
# With no directory the manifests come from the newest release, which is what a catch-up wants.

set -euo pipefail

source_dir=${1:-}
repo=fgilde/homebrew-tap
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

if [ -n "${TAP_DEPLOY_KEY:-}" ]; then
  key="$work/tap_key"
  printf '%s\n' "$TAP_DEPLOY_KEY" > "$key"
  chmod 600 "$key"
  export GIT_SSH_COMMAND="ssh -i $key -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new"
  remote="git@github.com:$repo.git"
elif [ -n "${TAP_TOKEN:-}" ]; then
  remote="https://x-access-token:$TAP_TOKEN@github.com/$repo.git"
else
  echo "::warning::no TAP_DEPLOY_KEY and no TAP_TOKEN - the tap keeps whatever its own sync last fetched"
  exit 0
fi

git clone --depth 1 "$remote" "$work/tap"
mkdir -p "$work/tap/Formula" "$work/tap/Casks"

if [ -n "$source_dir" ]; then
  cp "$source_dir/quickrun.rb" "$work/tap/Formula/quickrun.rb"
  cp "$source_dir/quickrun-cask.rb" "$work/tap/Casks/quickrun.rb"
else
  base=https://github.com/fgilde/QuickRun/releases/latest/download
  curl -fsSL -o "$work/tap/Formula/quickrun.rb" "$base/quickrun.rb"
  curl -fsSL -o "$work/tap/Casks/quickrun.rb" "$base/quickrun-cask.rb"
fi

# Both are generated, so a truncated download would otherwise be committed as a broken formula and
# every `brew install` after it would fail.
grep -q 'class Quickrun < Formula' "$work/tap/Formula/quickrun.rb"
grep -q 'cask "quickrun"' "$work/tap/Casks/quickrun.rb"

cd "$work/tap"
git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"

if git diff --quiet; then
  echo "::notice::the tap already has this formula"
  exit 0
fi

version=$(sed -n 's/.*version *"\([^"]*\)".*/\1/p' Formula/quickrun.rb | head -1)
git commit -am "feat: quickrun formula for QuickRun $version"
git push
echo "::notice::the tap is on $version"
