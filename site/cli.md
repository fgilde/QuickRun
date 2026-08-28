# CLI

QuickRun is fully usable without the browser extension.

## `quickrun` with no arguments

Starts the listener, puts an icon in the tray and opens the dashboard. This is what a double-click
does, and it is the entry point for everything else:

- **Run a repository** — start any repository without the browser extension
- **Runs** — what is running, with live progress and log output
- **Workspaces** — what is checked out, how much disk it uses, and a way to remove it
- **Browser extension** — how the extension works
- **About** — version, install source, update check

```bash
quickrun                 # tray icon + desktop window
quickrun --browser       # open the dashboard in a browser instead of the window
quickrun --no-window     # tray icon only; the icon opens a window when you want one
quickrun --no-tray       # no tray icon, so the browser is the UI
quickrun daemon          # listener only, no tray and no window
```

The window is drawn natively rather than in an embedded browser. That avoids shipping a browser
engine to render a few lists, and avoids the browser offering to translate a local tool page.

The binary is built for the GUI subsystem, so double-clicking it opens no console window. Started
from a terminal it attaches to that terminal, which is what keeps the commands below usable. The
one visible consequence: your shell prints its next prompt before the output arrives, so a prompt
can appear above it.

## `quickrun run`

```bash
quickrun run acme/app
quickrun run acme/app --ref feature/login
quickrun run https://github.com/acme/app --pr 42
quickrun run acme/app --input apiKey=sk-1 --input port=3000
```

| Option | Meaning |
|---|---|
| `-r, --ref` | branch, tag or commit; defaults to the repository's default branch |
| `-p, --pr` | pull request number, fetched as `refs/pull/<n>/head` so forks work |
| `-d, --subdir` | treat a subdirectory as the project root |
| `-i, --input` | fill a declared input, repeatable |
| `-t, --token` | access token for a private repository |
| `-c, --config` | use a different config file, relative to the project root |
| `--path` | run a folder on this machine instead of checking a repository out |
| `--copy` | with `--path`: run a copy under `runs/`, leaving the folder alone |
| `--fresh` | delete the workspace and clone again |
| `-y, --yes` | skip the confirmation prompt; missing required inputs then fail rather than prompt |
| `--no-open` | do not open any browser URL the config asks for |

Nothing executes before the plan is printed and confirmed, unless `--yes` is given.

## `quickrun run` on a folder

```bash
quickrun run .                       # this folder, where it is
quickrun run --path ~/dev/planner    # the same, said explicitly
quickrun run --path . --copy         # a copy under runs/, leaving the folder alone
```

No checkout, no clone: QuickRun reads the folder's `quickrun.yml` and runs the commands in it, in
that folder. A repository shorthand is never a directory that exists, so the argument form does not
collide with `quickrun run acme/app`. Shell verbs use `--path`, where nothing may be guessed.

What appears in the workspace list is a **note** saying where the folder is, not a copy of it. Its
size reads `in place`, and removing that workspace removes the note - `Remove`, `Remove all` and
`clean` cannot reach outside `runs/`, so they can never delete a working copy of yours.

`--copy` is for the case where the run must not touch the original: the folder is copied under
`runs/` and everything happens there. The copy leaves out what a build puts back - `.git`,
`node_modules`, `.venv`, `obj`, `bin`, `target`, `.next` and friends - and says so before it starts.
A copy is a workspace QuickRun owns, so removing it removes the copy.

If the folder is a git working copy, its branch and commit are reported, so a local run is
identifiable in the list afterwards. Otherwise the ref reads `local`.

Only from here, deliberately: a folder on this machine can be run from the command line and from a
shell verb, and never through the browser extension. The extension asks for repositories, and a
request naming a path or a `file:` URL is refused there.

### From the window

The run form has one field, and it takes either: `owner/repo`, a git URL, or a folder on this
machine. What you type decides what else appears - a branch picker for a repository, the copy switch
for a folder - and a line under the field says which one it read. The Browse button is always there,
because a page cannot be handed a path by a file input in any browser, so the window opens the system
picker on its behalf. Without a window - a QuickRun started headless - the path is typed or pasted.

The daemon decides for itself as well: a path that is really there is a folder, whatever the form
guessed while you were typing.

The native window - what you get where there is no system WebView - has the same one field, the same
Browse button and the same copy switch. It can do one thing the page cannot: it asks the file system
rather than guessing, so a path that is not there says so instead of being checked out as a
repository.

A folder with no `quickrun.yml` is not a dead end: QuickRun reads the project and proposes commands,
exactly as it does for a repository, and says that it guessed them.

## `quickrun open`

```bash
quickrun open .                      # hand this folder to QuickRun and show the plan
quickrun open ~/dev/planner --copy
```

What the context-menu entry calls. It runs nothing itself: it asks the QuickRun that is running -
starting one first if there is none - to prepare the folder and put the plan in its window, where the
decision belongs. Given a `quickrun.yml` it takes the folder that holds it, because a config is not a
thing that runs on its own.

That indirection is the point. A process the shell started would take the run with it when its window
closed, and the binary is built for the GUI subsystem, so a confirmation prompt in a console nobody
can see is no confirmation at all.

### "Run with QuickRun" in the file manager

QuickRun adds it when it starts, and keeps it pointing at the copy that is running - an update moves
the binary, and a menu entry aimed at where it used to be is worse than none. `quickrun uninstall`
takes it away. On a folder, on the empty space inside a folder, and on a `quickrun.yml`.

None of this needs administrator rights: everything is written under `HKCU` on Windows and in your
own home directory elsewhere. Autostart is the one thing QuickRun never switches on by itself - that
is a decision, and it lives in Settings.

**Windows** writes four keys under `HKCU\Software\Classes` - no administrator rights, and the `.yml`
entries go under `SystemFileAssociations`, which adds a verb beside whatever already opens YAML
rather than taking the file type over. On **Windows 11** the entry lives under *Show more options*:
the short menu only accepts a packaged handler, which is separate work.

**Linux** gets one file per file manager, because there is no shared standard: a KIO service menu for
Dolphin, an action file for Nemo, and a script for Nautilus - which dropped menu extensions of this
kind and offers only its Scripts submenu. Thunar is left out on purpose: its actions live in a single
`uca.xml` that people also edit by hand, and appending to that is not worth the risk of breaking it.

**macOS** has none of this yet. A Finder extension needs a Developer ID signature QuickRun does not
have, and the alternatives are worth doing properly rather than guessing at. `quickrun open .` works
there today.

## `quickrun validate`

```bash
quickrun validate
quickrun validate ./my-repo
```

Exit codes: `0` valid, `1` invalid, `2` no config found.

## `quickrun detect`

```bash
quickrun detect
quickrun detect . --save
```

Shows how QuickRun would start a repository that has no config: a foreign launcher's scripts first
(see [Repositories without a config](/no-config)), then what detection found. `--save` writes the
highest-ranked candidate to `quickrun.yml` and refuses to overwrite an existing file.

## `quickrun ls` and `quickrun clean`

```bash
quickrun ls
quickrun clean --all
quickrun clean --older-than 30d
quickrun clean acme__app__main-1a2b3c
```

`clean` requires exactly one selector. Deleting everything by default would be the worst possible
guess, so no selector is a usage error.

## `quickrun daemon`

```bash
quickrun daemon              # listen on 127.0.0.1:9876
```

## `quickrun doctor`

```bash
quickrun doctor              # check that this installation works, here
quickrun doctor --no-ui      # skip the window and tray checks, for a machine with no screen
```

Every check stands for something that actually broke once. It starts a listener of its own on a free
port and asks it the questions the browser extension asks - including the two that are a security
boundary: a request to start a run from an ordinary page must be refused, and one from an extension
must not be. Then it creates a window and a tray icon for real, because showing a window is what
loads the executable's icon, and a malformed icon there is fatal inside the UI framework rather than
catchable.

It also reports what it cannot fix by itself: no `git` on the `PATH`, a workspace directory that
cannot be written, a `quickrun://` registration pointing at an executable that is no longer there,
no daemon listening where the extension looks for one. A failing check exits non-zero; a warning -
autostart, the URL scheme - does not, because those are conveniences and not requirements.

## `quickrun update`

```bash
quickrun update
quickrun update --check
```

## Workspaces

```
Windows  %LOCALAPPDATA%\QuickRun\runs\
Linux    ~/.local/share/QuickRun/runs/
macOS    ~/Library/Application Support/QuickRun/runs/
```

Deliberately not `%TEMP%`: system cleaners delete from there, and a half-removed `node_modules`
mid-run is a bug factory. `QUICKRUN_HOME` overrides the root.

A second run of the same repository and ref reuses its workspace — `git fetch` plus
`git reset --hard`, keeping `node_modules`, `.venv`, `obj`, `bin`, `target` and friends — so
starting again takes seconds. `--fresh` is the escape hatch when a workspace is broken.

A workspace QuickRun has no record of is still listed, as `unknown - no QuickRun metadata`. That is
a checkout that died before it was written down, or one whose removal got halfway - and a directory
that is not listed cannot be removed either, which is the only reason this is visible at all. One
with no file left in it is swept up without asking, because there is nothing in it to lose.

A removal that cannot finish says so rather than reporting success. On Windows the usual reason is a
file something still has open - a run of that repository still going, a virus scanner reading it as
it goes, an Explorer window sitting in the directory - and the answer is to close that and try
again. `Remove all` attempts every workspace and names the ones that refused, rather than stopping
at the first.

## Authentication

For a private repository, first hit wins:

1. `--token`
2. `QUICKRUN_TOKEN`
3. `gh auth token`, if you are logged into the GitHub CLI
4. plain `git clone`, which picks up SSH keys and Git Credential Manager

Tokens are removed from every log line and error message. Credential prompts are disabled
throughout, including the GUI dialog Git Credential Manager would otherwise open — for a background
daemon that would be an invisible hang.
