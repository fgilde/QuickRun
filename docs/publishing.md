# Publishing a release

## Cutting a release

```bash
git tag v0.1.0
git push origin v0.1.0
```

The `release` workflow then builds all six targets, wraps the two macOS binaries
in a `QuickRun.app` bundle, writes `SHA256SUMS`, generates the scoop manifest and
the Homebrew formula, and publishes a GitHub Release with everything attached.

To rehearse without tagging, run the workflow manually (`workflow_dispatch`) with
a version like `0.0.0-dev`. It builds and packages but publishes nothing.

## Release assets

| Asset | Consumed by |
|---|---|
| `quickrun-<rid>.zip` (Windows) | direct download, scoop |
| `quickrun-<rid>.tar.gz` (Linux, macOS) | direct download, `install.sh`, brew |
| `QuickRun-osx-<arch>.app.zip` | macOS `quickrun://` registration |
| `SHA256SUMS` | auto-update, `install.sh` |
| `quickrun.json` | scoop |
| `quickrun.rb` | Homebrew tap |

Asset names deliberately carry **no version**. `releases/latest/download/<name>`
requires the exact file name, so a versioned name would leave the website with no
stable link at all. The version lives in the release tag, and `quickrun --version`
reports it from the binary.

## Package managers

### scoop

`quickrun.json` is generated per release and served from GitHub Pages by the
`pages` workflow, so no separate bucket repository is needed:

```powershell
scoop install https://fgilde.github.io/QuickRun/quickrun.json
```

### Homebrew

The generated `quickrun.rb` is served from GitHub Pages, which the `pages`
workflow fetches out of the latest release. That makes it installable with no tap
repository at all:

```bash
brew install https://fgilde.github.io/QuickRun/quickrun.rb
```

A tap is still nicer for `brew upgrade`. To add one, create a repository named
exactly `fgilde/homebrew-tap` and copy `quickrun.rb` into its `Formula/`
directory each release; `brew install fgilde/tap/quickrun` then works too.

### winget

winget manifests live in `microsoft/winget-pkgs` and arrive by pull request. Use
[wingetcreate](https://github.com/microsoft/winget-create) to generate and submit:

```powershell
wingetcreate update fgilde.QuickRun --version 0.1.0 `
  --urls https://github.com/fgilde/QuickRun/releases/download/v0.1.0/quickrun-win-x64.zip `
  --submit
```

The first submission needs `wingetcreate new`. Review times are typically a day
or two, so winget lags a release rather than tracking it.

## Signing

Binaries are unsigned. Consequences, and what the docs tell users:

- **macOS** blocks a downloaded unsigned binary outright. Homebrew strips the
  quarantine attribute, so `brew install` is the documented path;
  `install.sh` removes the attribute explicitly. For a manual download the
  workaround is `xattr -d com.apple.quarantine ./quickrun`, or right-click →
  Open once.
- **Windows** shows a SmartScreen warning on first run. It clears itself once
  enough people have run the same binary; scoop and winget installs avoid it.
- **Linux** does not care.

Adding signing later touches one job in the release workflow. It needs an Apple
Developer account for notarization and an OV or EV certificate for Windows, both
annual costs, which is why they wait until there are users to protect.
