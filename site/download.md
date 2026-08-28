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
brew install --cask https://quickrun.org/quickrun-cask.rb
brew install https://quickrun.org/quickrun.rb
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
- **Windows** shows a SmartScreen warning on first run, because the binary is not signed yet.
  `scoop` and `winget` installs avoid it.

  One release, v0.8.3, went further: the published zip was refused by browsers as "virus detected",
  because Defender's machine-learning model called it `Trojan:Script/Wacatac.B!ml` - `!ml` being a
  guess from shape rather than a match against anything known. It was wrong, and it was about that
  one file: the same source built locally scanned clean, and the same source rebuilt in CI
  downloaded clean. Every Windows build is now scanned on the build machine before it is published,
  so a release that would be refused fails there instead of reaching you.

  If a download is ever blocked anyway: check what you got against the release's own `SHA256SUMS`,
  ```powershell
  Get-FileHash .\quickrun-win-x64.zip -Algorithm SHA256
  ```
  and install through `winget` or `scoop`, which do not go through the browser's download path.

- **Linux** does not care.

A signature is the permanent answer, and the release workflow signs Windows builds as soon as the
signing credentials are configured. Until then the checksums above are what proves a download is
the file it claims to be.

## The browser extension

The extension puts a Run button on GitHub. It is not required — the application works on its own —
but it is the reason QuickRun exists.

<ExtensionCards lang="en" />

**Edge** installs it from the [Microsoft Edge Add-ons store](https://microsoftedge.microsoft.com/addons/detail/quickrun/dbnknhijahmiildfabckibabpieobnhd),
and updates come with the browser's own.

Where a store does not carry it yet, load it unpacked. Unzip the download, then:

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
