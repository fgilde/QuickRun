# Download

Everything here always refers to the newest release. There is no version in any link, so a
bookmark never goes stale.

## The application

<DownloadHero lang="en" />

Unpack the archive and put `quickrun` somewhere on your `PATH`. Run it with no arguments — or
double-click it — and QuickRun puts an icon in the tray and opens its window. No console window
appears: the binary is a desktop application that also works as a command line tool when you start
it from a terminal.

`winget install fgilde.QuickRun` works once the package is accepted into the winget repository; the
submission is [under review](https://github.com/microsoft/winget-pkgs/pulls?q=fgilde.QuickRun).
Until then use scoop, which installs from the manifest this site serves.

The Homebrew formula and the scoop manifest are regenerated with every release and served from here,
so neither needs a separate tap or bucket repository.

### macOS: the cask, not the formula

```bash
brew install --cask fgilde/tap/quickrun
```

This is the install a Mac expects. It puts **QuickRun.app** into `/Applications`, so it appears in
Launchpad and Spotlight with its icon, it can claim the `quickrun://` scheme - which lives in an app
bundle's `Info.plist` and cannot be claimed by a bare binary - and it links the binary inside the
bundle onto your `PATH`, so `quickrun` in a terminal and the app are the same installation.

The formula installs the command line only:

```bash
brew install fgilde/tap/quickrun   # no app bundle, no Launchpad entry, no quickrun://
```

Both are also served from this site, so neither needs the tap:

```bash
brew install --cask https://fgilde.github.io/QuickRun/quickrun-cask.rb
brew install https://fgilde.github.io/QuickRun/quickrun.rb
```

Either way, `brew upgrade quickrun` updates it and QuickRun leaves its own binary alone, because
Homebrew owns it.

The bundle is also a plain download, if you would rather not use Homebrew:

- [QuickRun-osx-arm64.app.zip](https://github.com/fgilde/QuickRun/releases/latest/download/QuickRun-osx-arm64.app.zip) — Apple silicon
- [QuickRun-osx-x64.app.zip](https://github.com/fgilde/QuickRun/releases/latest/download/QuickRun-osx-x64.app.zip) — Intel

Downloaded by hand, it carries the quarantine flag and macOS calls it damaged. Clear it once:

```bash
xattr -dr com.apple.quarantine /Applications/QuickRun.app
```

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

<ExtensionCards lang="en" />

Until the store listings are live, load it unpacked. Unzip the download, then:

- **Chrome, Edge, Opera** — `chrome://extensions` → Developer mode → Load unpacked → the unpacked
  folder
- **Firefox** — `about:debugging` → This Firefox → Load Temporary Add-on → the `manifest.json`
  inside the folder

That is all: there is nothing to pair. QuickRun accepts requests only from a browser extension.

## What to do next

- [First run](/install)
- [Config reference](/config) — for making your own repository runnable
- [How the extension works](/extension)
- [Security model](/security)
