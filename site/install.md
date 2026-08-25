# First run

Get the files from the [download page](/download) first. This page is what to do with them.

## Start it

Run the binary with no arguments, or double-click it:

```bash
quickrun
```

QuickRun starts its listener on `127.0.0.1:9876`, puts an icon in the tray and opens its window.

On Windows the window shows the same page the listener serves, in the system's WebView - one
interface rather than two, and the browser engine is the one Windows already ships. The page brings
its own header, so the window adds none, and an **Open in browser** link in it is the way out that a
window without an address bar otherwise lacks. Elsewhere, and whenever no WebView is available or
`QUICKRUN_NO_WEBVIEW` is set, the window draws its own native view of the same data.

The window has these sections:

- **Start a run** — a repository and a branch, without the browser extension. Type the repository
  and QuickRun lists its branches, putting the refs you have run before at the top and preselecting
  the one you would have picked. A pull request number, a token for a private repository and the
  config's inputs are behind *More*. Preparing shows the plan; nothing runs until you confirm it
- **Runs** — what is running, with live progress and log output
- **Config builder** — write, check and test a `quickrun.yml`, see [the config builder](/builder)
- **Workspaces** — what is checked out, how much disk it uses, and a way to remove it
- **Browser extension** — how the extension works
- **About** — version, install source, update check

The same view is available in a browser at `http://127.0.0.1:9876` if you would rather have it
there. `quickrun --browser` opens it that way; `quickrun --no-tray` skips the tray icon entirely.

## Register the protocol and start at login

```bash
quickrun install
```

This registers the `quickrun://` scheme and adds an autostart entry. The scheme has one job: it lets
the browser extension start QuickRun when it is installed but not running. Without it everything
still works, as long as QuickRun is running when you click the button.

The **Browser extension** tab of the local UI does the same for the scheme alone, and says what the
state is: *registered*, *not registered*, or *registered to another build* - which is what you get
after moving or reinstalling the binary, and the one failure that looks like success. No
administrator rights are involved: on Windows it is a key under `HKCU\Software\Classes\quickrun`,
on Linux a `.desktop` file in `~/.local/share/applications`.

On macOS the scheme needs the [app bundle](/download#macos-app-bundle); a bare binary cannot claim a
URL scheme.

## Check it works

Open any repository on GitHub. A **Run this** button appears next to the branch dropdown. Clicking it
does not start anything yet: QuickRun checks the repository out, then the extension shows you the
exact commands and waits for your confirmation.

## Updating

```bash
quickrun update          # installs when QuickRun owns the binary
quickrun update --check  # only reports
```

QuickRun works out who owns its binary from where it is installed. If a package manager put it
there, `update` reports the version and the right command instead of overwriting the file — two
updaters fighting over one file is how version chaos starts.

The download is verified against the checksums published with the release before anything is
replaced, and the update is applied on restart, never mid-run. `--no-update` disables checking.

## Uninstall

```bash
quickrun clean --all   # remove checked-out workspaces first
quickrun uninstall     # unregister quickrun:// and the autostart entry
```

Then remove the binary, or `scoop uninstall quickrun` / `brew uninstall quickrun` /
`winget uninstall fgilde.QuickRun`.
