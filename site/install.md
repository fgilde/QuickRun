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
  the one you would have picked. A pull request number and a token for a private repository are
  behind *More*. Preparing shows the plan - and, when the config declares inputs, a form for them
  first; nothing runs until you confirm it
- **Runs** — what is running, with live progress and log output. Each task shows its state, its
  address once it has one, and the process id of what it started. **Stop** asks the run to stop: it
  says *stopping* while the config's stop commands run, gives them 30 seconds, and then kills what
  is left. That means everything the command started, not only what still has an unbroken line back
  to it: `dotnet run` launches the application and the process in between is often already gone, and
  such an application used to survive being stopped and keep answering on its port. The run always
  leaves that state. A finished run can be taken off the list with
  **Remove**, which deletes nothing: the checkout stays in Workspaces
- **Config builder** — write, check and test a `quickrun.yml`, see [the config builder](/builder)
- **Workspaces** — what is checked out, how much disk it uses, and a way to remove it
- **Browser extension** — how the extension works
- **Settings** — whether QuickRun starts when you sign in, whether `quickrun` works in a
  terminal, and what the command line can do
- **About** — version, install source, update check

The window opens where you left it, at the size you left it, maximised if that is how you left it -
kept in `window.json` next to the workspaces.

The same view is available in a browser at `http://127.0.0.1:9876` if you would rather have it
there. `quickrun --browser` opens it that way; `quickrun --no-tray` skips the tray icon entirely.

## Settings

Two switches, both per-user, neither needing administrator rights:

- **Start QuickRun when I sign in** — the browser button needs QuickRun to be running. On Windows
  this is a value under `HKCU\...\CurrentVersion\Run`, on Linux a `.desktop` file in
  `~/.config/autostart`, on macOS a launch agent in `~/Library/LaunchAgents`. The Settings tab shows
  which, so it can be undone by hand as well - and says so when it points at an executable that has
  since moved.
- **Make `quickrun` work in a terminal** — on Windows this adds the program's own directory to your
  PATH and tells running shells about it, so a *new* terminal has the command. On Linux and macOS it
  links `quickrun` into a bin directory that is already on the PATH (`~/.local/bin`, or Homebrew's
  directory on macOS when it is writable) rather than editing anyone's shell profile.

`quickrun install` does both of these plus the `quickrun://` handler in one go, and
`quickrun uninstall` undoes them.

## Register the protocol and start at login

```bash
quickrun install
```

This registers the `quickrun://` scheme and adds an autostart entry. The scheme has one job: it lets
the browser extension start QuickRun when it is installed but not running. Without it everything
still works, as long as QuickRun is running when you click the button.

On Linux it also installs the icon into `~/.local/share/icons` and writes a normal application entry
alongside the handler, so QuickRun appears in the menu with its icon like any other program. On
Windows the icon is in the executable; on macOS it is in the app bundle.

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
