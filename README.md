<p align="center">
  <img src="assets/logo.png" alt="QuickRun" width="420">
</p>

<p align="center"><strong>Run any git repository with one click.</strong></p>

<p align="center">
  <a href="https://fgilde.github.io/QuickRun/download">Download</a> ·
  <a href="https://fgilde.github.io/QuickRun/">Documentation (English)</a> ·
  <a href="https://fgilde.github.io/QuickRun/de/">Dokumentation (Deutsch)</a> ·
  <a href="samples/">Example configs</a>
</p>

---

A repository owner commits a `quickrun.yml` describing how to start their project. Anyone
with QuickRun installed runs it without reading a single line of setup documentation:

```bash
quickrun run acme/app
```

QuickRun checks the repository out into a managed workspace, verifies the prerequisites,
asks for whatever inputs the config declares, shows you the exact commands it is about to
execute, and supervises the resulting processes until you stop them.

Run the binary with no arguments — or double-click it — and QuickRun puts an icon in the tray and
opens a desktop window. On Windows that window shows the same page the listener serves, in the
system's WebView, so there is one interface rather than two; where no system WebView exists it draws
a native view of the same data. The page is also available in a browser at `http://127.0.0.1:9876`
when you would rather have it there.

Repositories with no config still work. A repository written for [Pinokio](https://pinokio.co)
runs from its own `install.js` and `start.js` — including the Python virtual environments, the
`when` branches picking a CUDA, DirectML or CPU install, and the pattern that says the service is
up. Everything else is detected: compose files, `package.json` scripts, .NET projects, Python apps
(Django, Streamlit, Gradio, FastAPI), `Procfile`, `.replit`, Makefiles, Taskfile, justfile, Cargo,
Go, Maven, Gradle — with the port worked out where possible, so you get a link rather than a log to
search. `quickrun detect --save` writes the generated `quickrun.yml` back for you to commit. See
[Repositories without a config](https://fgilde.github.io/QuickRun/no-config).

A config builder comes with the tool: the local UI writes a `quickrun.yml` with completion from the
published schema, checks it with the same validator a run uses, tests it against the repository
before anything is committed, and keeps your own config for repositories you do not own - stored in
QuickRun's own directory, not in someone else's checkout. See
[the config builder](https://fgilde.github.io/QuickRun/builder).

## What a config looks like

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
version: 1
name: Vite dev server
requires:
  - tool: node
    version: ">=20"
    install: https://nodejs.org/en/download
setup:
  - npm ci
tasks:
  - name: web
    run: npm run dev
    readyWhen: {port: 5173}
    open: true
```

Every block is optional. The shortest config that works is one line:

```yaml
run: ./run.sh
```

See [`samples/`](samples/) for eight worked examples, including a multi-service stack
(Postgres + .NET API + Vite), a generated input form with a validated secret, and a
repository that installs its own SDK.

## Commands

| Command | What it does |
|---|---|
| `quickrun run <repo>` | Check out a repository and run it |
| `quickrun validate [path]` | Validate a `quickrun.yml` without running anything |
| `quickrun detect [path] [--save]` | Show how QuickRun would start a repository, optionally write the config |
| `quickrun ls` | List workspaces with their size and last use |
| `quickrun clean --all \| --older-than 30d \| <id>` | Remove workspaces |
| `quickrun` (no arguments) | Start with a tray icon and open the desktop window |
| `quickrun daemon` | Run the localhost listener without a tray icon |
| `quickrun doctor` | Check that this installation works: listener, extension contract, workspace, tray, window |
| `quickrun update` | Check for a newer QuickRun and install it when QuickRun owns the binary |

Useful `run` options: `--ref <branch\|tag\|sha>`, `--pr <number>`, `--subdir <path>`,
`--input key=value` (repeatable), `--token <token>` for private repositories, `--fresh` to
discard the workspace and clone again, `--yes` to skip the confirmation prompt, `--no-open`
to suppress browser launches.

## Workspaces

Repositories are checked out under the OS application-data directory, never under `%TEMP%`:

```
Windows  %LOCALAPPDATA%\QuickRun\runs\
Linux    ~/.local/share/QuickRun/runs/
macOS    ~/Library/Application Support/QuickRun/runs/
```

A second run of the same repository and ref reuses its workspace (`git fetch` +
`git reset --hard`, keeping `node_modules` and friends), so starting again takes seconds.
`QUICKRUN_HOME` overrides the root.

## Security

**QuickRun executes code from the repository you point it at, with your privileges, outside
any sandbox.** Two things follow from that, and neither is optional:

- Every run shows the repository, ref, resolved commit and the **exact commands** that will
  execute, and waits for your confirmation. There is no way to disable that prompt globally.
- A repository you have marked as trusted is trusted for *those commands*. QuickRun stores a
  hash of them, so a repository cannot be trusted once and changed into something else later.

Access tokens are scrubbed from every log line and error message. Values from `password`
inputs are passed to child processes as environment variables and never written to logs or
run history.

Only run repositories you would be willing to `git clone && ./run.sh` by hand.

## Browser extension

The extension puts a Run button next to the branch dropdown, in pull request headers and on every
row of the branch list, and shows the run's progress in place.

A browser cannot be asked whether a URL scheme has a handler, so the localhost listener is what
tells the extension that QuickRun is installed — and what carries progress back. `quickrun://` has
one job left: starting a daemon that is installed but not running. If neither answers, the button
offers the download page.

Clicking Run does not start anything. QuickRun checks the repository out, builds the plan, and the
extension opens a window listing the exact commands. Only the button in that window starts them —
and that window is an extension page, not part of GitHub, because a web page can draw a convincing
fake panel over its own content.

```bash
cd extension
sh build.sh     # dist/chromium for Chrome, Edge and Opera; dist/firefox for Firefox
```

Store listings are pending review. Until then the [download page](https://fgilde.github.io/QuickRun/download)
has the builds and the steps to load them unpacked.

## Updating

```bash
quickrun update
```

QuickRun works out who owns its binary from where it is installed. If a package manager put it
there, `update` reports the version and the right command instead of overwriting the file. The
standalone path verifies the download's SHA-256 against the checksums published with the release
before replacing anything.

## Building from source

```bash
dotnet test                          # 420+ tests, no network access required
node --test extension/test/*.test.js # extension unit tests
dotnet run --project src/QuickRun.App -- --help
```

Requires the .NET 10 SDK. Releases ship as a self-contained single-file binary per platform, so
users need no runtime installed. That single file is around 128 MB: it carries the .NET runtime
and the native libraries the tray icon needs, and bundling them is what keeps the download to one
file with nothing to install. See [docs/publishing.md](docs/publishing.md) for how a release is
cut.

## License

MIT
