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
scoop install https://quickrun.org/quickrun.json
```

### Homebrew

The generated `quickrun.rb` is served from GitHub Pages, which the `pages`
workflow fetches out of the latest release. That makes it installable with no tap
repository at all:

```bash
brew install https://quickrun.org/quickrun.rb
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

## Automatic distribution

A tag pushes through four jobs: `build` (six targets), `release` (assets, checksums, manifests,
the GitHub Release), then `distribute` and `winget`.

Every distribution step **skips itself when its credential is missing** and says so in the log, so
a release never fails because a store account is not set up. Add the secrets and the corresponding
step starts working on the next tag; nothing else needs changing.

| Target | Secrets |
|---|---|
| Homebrew tap | `TAP_TOKEN` |
| winget | `WINGET_TOKEN` |
| Chrome Web Store | `CHROME_EXTENSION_ID`, `CHROME_CLIENT_ID`, `CHROME_CLIENT_SECRET`, `CHROME_REFRESH_TOKEN` |
| Firefox Add-ons | `AMO_JWT_ISSUER`, `AMO_JWT_SECRET` |
| Edge Add-ons | `EDGE_PRODUCT_ID`, `EDGE_CLIENT_ID`, `EDGE_API_KEY` |

Where each one comes from, the exact page and menu path, what to run to set it, and how to check
whether it works: **[store-credentials.md](store-credentials.md)**.

### What still needs a human, once

- **Developer accounts.** Chrome charges a one-off 5 USD registration fee; Edge and Firefox are
  free. All three require accepting a developer agreement, which cannot be automated.
- **The first store submission.** Chrome needs one manual upload to allocate an extension id;
  Edge and Firefox cannot create a listing over their APIs at all. Every store reviews a first
  listing by hand. After that the workflow updates them.
- **The first winget submission.** `wingetcreate update` needs the package to exist in
  `microsoft/winget-pkgs`. Run `wingetcreate new` once locally with the release URLs, or let the
  first workflow run fall back to it.
- **Store listing copy.** Description, category, screenshots and the privacy declaration live in
  each store's dashboard, not in this repository.

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
