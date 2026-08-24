# Install

## Package managers

The recommended path on every platform. Package managers keep QuickRun up to date and, on macOS,
avoid the Gatekeeper problem described below.

::: code-group

```powershell [Windows]
winget install fgilde.QuickRun
# or
scoop install https://fgilde.github.io/QuickRun/quickrun.json
```

```bash [macOS]
brew install fgilde/tap/quickrun
```

```bash [Linux]
curl -fsSL https://fgilde.github.io/QuickRun/install.sh | sh
```

:::

The Linux installer downloads the release asset for your architecture, verifies it against the
published `SHA256SUMS`, and installs it to `~/.local/bin`. Set `PREFIX` to install elsewhere.

## Direct download

<DownloadButtons lang="en" />

Unpack the archive and put `quickrun` somewhere on your `PATH`.

### Unsigned binaries

QuickRun is not code-signed yet, and that has consequences worth knowing before you download:

- **macOS** refuses to run a downloaded unsigned binary at all. Either use Homebrew, which strips
  the quarantine attribute, or clear it yourself:
  ```bash
  xattr -d com.apple.quarantine ./quickrun
  ```
- **Windows** shows a SmartScreen warning the first time. `winget` and `scoop` installs avoid it.
- **Linux** does not care.

Signing certificates cost money every year and buy nothing until there are users to protect, so
they wait. The release pipeline is structured so that adding a signing step later touches one job.

## First run

```bash
quickrun install     # register quickrun:// and start the daemon at login
quickrun daemon      # or run the listener in the foreground
quickrun pair        # then click Pair in the browser extension
```

`quickrun install` is what registers the `quickrun://` scheme, which is how the extension can start
a daemon that is installed but not running.

## Browser extension

The extension is what puts the Run button on GitHub. It is not required — the CLI works on its own —
but it is the reason QuickRun exists.

| Browser | Where |
|---|---|
| Chrome | Chrome Web Store *(pending review)* |
| Edge | Edge Add-ons *(pending review)* |
| Firefox | Firefox Add-ons *(pending review)* |
| Opera | install the Chrome build via Opera's Chrome extension support |

Until the store listings are live, download the build from the latest release and load it
unpacked:

- [quickrun-extension-chromium.zip](https://github.com/fgilde/QuickRun/releases/latest/download/quickrun-extension-chromium.zip)
  — Chrome, Edge, Opera
- [quickrun-extension-firefox.zip](https://github.com/fgilde/QuickRun/releases/latest/download/quickrun-extension-firefox.zip)
  — Firefox

Unpack it, then in Chrome or Edge: `chrome://extensions` → Developer mode → Load unpacked → the
unpacked folder. In Firefox: `about:debugging` → This Firefox → Load Temporary Add-on → the
`manifest.json` inside it.

Or build it yourself:

```bash
git clone https://github.com/fgilde/QuickRun
cd QuickRun/extension
sh build.sh
```

## Updating

```bash
quickrun update          # installs when QuickRun owns the binary
quickrun update --check  # only reports
```

QuickRun works out who owns its binary from where it is installed. If a package manager put it
there, `update` reports the version and the right command instead of overwriting the file — two
updaters fighting over one file is how version chaos starts.

## Uninstall

```bash
quickrun clean --all   # remove checked-out workspaces first
quickrun uninstall     # unregister quickrun:// and the autostart entry
```

Then remove the binary, or `winget uninstall fgilde.QuickRun` / `brew uninstall quickrun` /
`scoop uninstall quickrun`.
