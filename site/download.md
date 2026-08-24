# Download

Everything here always refers to the newest release. There is no version in any link, so a
bookmark never goes stale.

## The application

<DownloadButtons lang="en" />

Unpack the archive and put `quickrun` somewhere on your `PATH`. Run it with no arguments — or
double-click it — and QuickRun puts an icon in the tray and opens its window.

### With a package manager

Package managers keep QuickRun up to date, and on macOS they avoid the Gatekeeper problem described
below.

::: code-group

```powershell [Windows]
scoop install https://fgilde.github.io/QuickRun/quickrun.json
```

```bash [macOS]
brew install fgilde/tap/quickrun
```

```bash [Linux]
curl -fsSL https://fgilde.github.io/QuickRun/install.sh | sh
```

:::

`winget install fgilde.QuickRun` works once the package is accepted into the winget repository; the
submission is [under review](https://github.com/microsoft/winget-pkgs/pulls?q=fgilde.QuickRun).

The Homebrew formula and the scoop manifest are served from this site and regenerated with every
release, so neither needs a separate tap or bucket repository. If you prefer the tap:
`brew install fgilde/tap/quickrun`.

### macOS app bundle

macOS registers the `quickrun://` scheme through an app bundle, which a bare binary is not. If you
want the browser extension to be able to start QuickRun when it is not running, use the bundle:

- [QuickRun-osx-arm64.app.zip](https://github.com/fgilde/QuickRun/releases/latest/download/QuickRun-osx-arm64.app.zip) — Apple silicon
- [QuickRun-osx-x64.app.zip](https://github.com/fgilde/QuickRun/releases/latest/download/QuickRun-osx-x64.app.zip) — Intel

### Verifying a download

Every release publishes
[SHA256SUMS](https://github.com/fgilde/QuickRun/releases/latest/download/SHA256SUMS) covering each
binary. The Linux installer checks it automatically; `quickrun update` checks it before replacing
anything.

```bash
sha256sum -c --ignore-missing SHA256SUMS
```

### Unsigned binaries

QuickRun is not code-signed yet:

- **macOS** refuses to run a downloaded unsigned binary. Homebrew strips the quarantine attribute,
  which is why it is the recommended path; `install.sh` removes it explicitly. For a manual
  download, clear it yourself:
  ```bash
  xattr -d com.apple.quarantine ./quickrun
  ```
- **Windows** shows a SmartScreen warning on first run. `scoop` and `winget` installs avoid it.
- **Linux** does not care.

Signing certificates cost money every year and buy nothing until there are users to protect, so
they wait.

## The browser extension

The extension puts a Run button on GitHub. It is not required — the application works on its own —
but it is the reason QuickRun exists.

| Browser | Store | Direct download |
|---|---|---|
| Chrome | *review pending* | [quickrun-extension-chromium.zip](https://github.com/fgilde/QuickRun/releases/latest/download/quickrun-extension-chromium.zip) |
| Edge | *review pending* | same Chromium build |
| Opera | *review pending* | same Chromium build |
| Firefox | *review pending* | [quickrun-extension-firefox.zip](https://github.com/fgilde/QuickRun/releases/latest/download/quickrun-extension-firefox.zip) |

Until the store listings are live, load it unpacked. Unzip the download, then:

- **Chrome, Edge, Opera** — `chrome://extensions` → Developer mode → Load unpacked → the unpacked
  folder
- **Firefox** — `about:debugging` → This Firefox → Load Temporary Add-on → the `manifest.json`
  inside the folder

Then pair it: open QuickRun, go to **Browser extension**, click **Open pairing window**, and click
**Pair** in the extension within 60 seconds.

## What to do next

- [First run and pairing](/install)
- [Config reference](/config) — for making your own repository runnable
- [How the extension works](/extension)
- [Security model](/security)
