# QuickRun — Design

Status: approved (2026-08-24)

## 1. Purpose

Run any git repository with one click, from the place you found it.

A repo owner commits a `quickrun.yml` describing how to start their project.
Anyone with QuickRun installed clicks a button on the GitHub page — a PR, a
branch, the repo home — and QuickRun checks the code out into a managed
workspace, collects any required inputs, verifies prerequisites, and starts it.

Two audiences:

- **Repo owners** get a declarative file that makes their project runnable by
  strangers without a README archaeology session.
- **Users** get "click, confirm, running" for projects they have never set up.

### Goals

- One binary per OS, no runtime prerequisites for QuickRun itself.
- Fully usable from the CLI. The GUI is a convenience, never a requirement.
- Config is optional. A repo with a `run.sh` and nothing else still works.
- Config is declarative and auditable — the user sees the exact commands
  before anything executes.
- Repos without any config still get a useful offer via detection.

### Non-goals

- Not a package manager, not a container orchestrator, not a CI system.
- No sandboxing in v1 (see §10 — the trust dialog is the control).
- No hosted service. Everything runs on the user's machine.

## 2. Decisions

| Area | Decision |
|---|---|
| Engine + CLI | .NET 10, self-contained single-file per target |
| Process model | Background daemon holds Kestrel; CLI and protocol handler talk to it |
| GUI | Blazor Server UI served on localhost, opened in the user's browser |
| Tray icon | Deferred to Phase 6 — native dependency, little value in v1 |
| Config format | YAML, `quickrun.yml`, published JSON Schema |
| Config model | `requires` / `inputs` / `setup` / `tasks`, all optional, with shorthand |
| No config | Detect candidates, present them, user picks; never auto-run |
| Trust | Confirmation dialog always; "trust this repo" invalidated by config hash |
| Workspace | Reuse per repo+branch, `fetch` + `reset --hard`; "clone fresh" escape hatch |
| Trigger | `quickrun://` by default, localhost listener optional |
| Extension | One MV3 source for Chrome/Edge/Opera, Firefox variant; button always shown |
| Docs | VitePress with `en`/`de`, plus one Blazor WASM playground page |
| Default port | 9876 |

## 3. Architecture

```
GitHub page (extension button)
   │
   ├── quickrun://run?…                → OS launches QuickRun.App → daemon   (default)
   └── POST 127.0.0.1:9876/api/run     → already-running daemon              (opt-in)
   │
   ▼
QuickRun.App ── Kestrel ──▶ Blazor Server UI in a browser tab
   │                        (trust dialog, input form, log stream, workspaces)
   ▼
QuickRun.Core
   Git        clone / fetch / reset, auth chain, token scrubbing
   Workspace  managed directories, reuse, size accounting, cleanup
   Config     parse → expand shorthand → validate
   Detector   fallback candidates when no config exists
   Supervisor child processes, output streaming, readiness, kill-tree
```

`quickrun run …` from a terminal follows the identical path minus the browser
tab: inputs come from `--input key=value` or console prompts. One engine, three
frontends (CLI, web UI, extension).

### Repository layout

```
QuickRun/
├─ README.md                      english
├─ quickrun.yml                   dogfooding: QuickRun runs itself
├─ assets/                        logo.png, icon.png, icon-round.png + generated sizes
├─ schema/quickrun.schema.json    generated from Core types, published to Pages
├─ samples/                       example configs, validated in CI
├─ src/
│  ├─ QuickRun.Core/              parser, validator, detector, git, workspace, supervisor
│  ├─ QuickRun.App/               single binary: CLI + Kestrel + Blazor Server UI + protocol
│  └─ QuickRun.Playground/        Blazor WASM, references Core (real validator in the browser)
├─ extension/                     one MV3 source → build to chromium/ + firefox/
├─ site/                          VitePress, en + de
└─ .github/workflows/
```

`Core` is a separate project only because the WASM playground needs the same
parser. CLI and daemon are **one** binary with subcommands:

```
quickrun run <repo> [--ref x] [--pr n] [--subdir p] [--input k=v] [--token t] [--fresh] [--yes]
quickrun validate [path]        validate a config against the schema
quickrun detect <repo>          print detected candidates, run nothing
quickrun ls                     list workspaces with size and last use
quickrun clean [--all] [--older-than 30d] [<workspace>]
quickrun daemon [--port 9876]
quickrun ui                     open the dashboard in the browser
quickrun install                register quickrun:// and autostart
quickrun uninstall
```

### Publish

Self-contained single-file for `win-x64`, `win-arm64`, `linux-x64`,
`linux-arm64`, `osx-x64`, `osx-arm64`. No NativeAOT — Blazor Server needs
reflection. Roughly 80 MB per binary; acceptable for a tool whose entire point
is sparing the user a runtime install.

## 4. `quickrun.yml`

Every block is optional. The only requirement is that the config describes
*something* to execute — a `run`, a `tasks` list, or nothing at all if a
detectable entry point exists.

### Shorthand

The parser expands shorthand into the canonical form before validation, so the
engine has exactly one shape to execute.

| Written | Expands to |
|---|---|
| `run: ./run.sh` | `tasks: [{name: run, run: "./run.sh"}]` |
| `run: {linux: ./run.sh, windows: ./run.ps1}` | one task, command picked per platform |
| `setup: [npm ci, dotnet restore]` | two sequential steps |
| `tasks: [npm start, python api.py]` | two tasks named `task-1`, `task-2` |
| a string anywhere a step/task object is expected | `{run: "<string>"}` |

A `run:` value that is a mapping is a **platform map** if all its keys are in
`{windows, linux, macos}`, otherwise it is an error. Step and task objects are
recognised by the presence of `run:`.

Minimum viable config:

```yaml
run: ./run.sh
```

If a repo has `run.sh`, `run.ps1`, `quickrun.sh` or `quickrun.ps1` in its root
and no `quickrun.yml` at all, QuickRun offers that script directly. Config is
only needed for inputs, parallelism, prerequisites, or readiness.

### Canonical form

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
version: 1                     # optional, default 1
name: My App                   # optional, default = repo name
description: ...               # optional
icon: assets/logo.png          # optional, repo-relative path or URL
docs: https://...              # optional, link shown in the UI

requires:                      # optional prerequisite checks
  - tool: dotnet               # any command name; known tools get better version probes
    version: ">=9.0"           # optional, semver range
    install: https://dot.net   # optional, shown when missing
    optional: false            # default false; true = warn instead of block

inputs:                        # optional, drive the generated form
  - id: apiKey                 # required, [A-Za-z_][A-Za-z0-9_]*
    label: OpenAI API Key      # default = id
    type: password             # text|password|number|bool|select|path|dir|file
    description: ...
    default: null
    required: true             # default false
    pattern: "^sk-"            # text/password only
    min: 1                     # number only
    max: 65535                 # number only
    options: [dev, prod]       # select only; also [{value, label}]
    env: OPENAI_API_KEY        # exported to every command
    persist: false             # default false; true = offer to remember (OS credential store)

env:                           # optional, static env for every command
  ASPNETCORE_ENVIRONMENT: Development

setup:                         # optional, sequential, before tasks
  - run: npm ci
    cwd: web
    when: [linux, macos]       # optional platform filter; scalar `when: linux` also allowed
    continueOnError: false

tasks:                         # optional, started in parallel unless dependsOn says otherwise
  - name: db
    run: docker compose up -d db
    readyWhen: {port: 5432}
  - name: api
    run: dotnet run --project src/Api
    dependsOn: [db]
    env: {PORT: "5000"}
    readyWhen: {port: 5000}
    restart: onFailure         # never|onFailure, default never
  - name: web
    run: npm run dev
    cwd: web
    readyWhen: {http: "http://localhost:5173"}
    open: true                 # true = open the readyWhen URL; or an explicit URL

stop:                          # optional, run when the user stops the app
  - docker compose down
```

`readyWhen` accepts exactly one of `port`, `http`, `log` (regex against
stdout/stderr) or `delay` (e.g. `5s`). Absent means "ready once the process
started".

`dependsOn` is a simple wait on the named task's readiness, not a full
scheduler. Cycles are rejected at validation time. Included despite the
minimal-model decision because `readyWhen` already exists and "database before
API" is the common case.

### Interpolation

`${inputs.apiKey}`, `${env.HOME}`, `${workspace}`, `${repo.name}`,
`${repo.ref}` are substituted in `run`, `cwd`, `env` values and `open`. Secret
inputs are substituted but never written to logs or the run history.

### Shell selection

Commands run through the platform shell: `cmd /c` on Windows, `/bin/sh -c`
elsewhere. A command starting with a `.sh` script on Windows is retried through
Git for Windows' `bash.exe` if present, otherwise it fails with a clear message
pointing at the platform map. This is what makes a `run.sh`-only repo behave
sensibly on Windows.

## 5. Generated input UI

Inputs are a flat declarative list — there is no model to reflect over, so no
ObjectEdit-style machinery is warranted. Each type maps to one control:

| type | control | validation |
|---|---|---|
| `text` | text field | `required`, `pattern` |
| `password` | masked field | `required`, `pattern`, never logged |
| `number` | number field | `required`, `min`, `max` |
| `bool` | switch | — |
| `select` | dropdown | value must be in `options` |
| `path` / `dir` / `file` | text field + browse | existence checked for `dir`/`file` |

The same declarations drive the CLI: `--input key=value` for each, console
prompts for anything missing, `--yes` fails instead of prompting when a
required input is absent. Validation lives in Core and runs identically in both
frontends.

## 6. Detection fallback

When a repo has no `quickrun.yml` and no root run script, the detector scans
the checkout and returns candidates, each with the exact command it would run
and a confidence. Nothing executes until the user picks one.

| Signal | Candidate |
|---|---|
| `docker-compose.yml` / `compose.yml` | `docker compose up` |
| `package.json` with `scripts.dev` / `start` | `npm ci` + `npm run <script>` |
| `*.csproj` with `Aspire.AppHost.Sdk` or `<IsAspireHost>` | `dotnet run --project <path>` |
| `*.sln` / single runnable `*.csproj` | `dotnet run --project <path>` |
| `requirements.txt` + `main.py` / `app.py` | venv + `pip install -r` + `python <file>` |
| `pyproject.toml` | `uv run` / `poetry install && poetry run` |
| `Makefile` with a `run` or `dev` target | `make <target>` |
| `Cargo.toml` | `cargo run` |
| `go.mod` | `go run ./...` |
| `pom.xml` / `build.gradle` | `mvn spring-boot:run` / `./gradlew bootRun` |

Monorepos yield several candidates; all are listed, grouped by directory.

Every candidate list carries a **"Save as quickrun.yml"** action that writes
the generated config into the workspace and shows it for review, so a user can
commit it or open a PR against the repo. This turns QuickRun into its own
config generator and is the main adoption lever.

## 7. Git and authentication

Checkout is `git clone --depth 1 --branch <ref>` into the workspace, with a
`.git`-suffix retry on failure (mirroring the approach already proven in
AspireUI's `GitService`).

Credential resolution, first hit wins:

1. `--token` / `QUICKRUN_TOKEN`
2. token stored for that host in the OS credential store (DPAPI / libsecret /
   Keychain), if the user chose to save one
3. `git credential fill` — picks up Git Credential Manager, SSH agents, and
   whatever the user already has working
4. `gh auth token` — the user may be logged into the GitHub CLI only
5. plain `git clone` — SSH remotes and ambient credentials

`GIT_TERMINAL_PROMPT=0` throughout, so nothing ever blocks on an invisible
prompt. That variable only covers *terminal* prompts, though: Git Credential
Manager opens a GUI dialog and waits indefinitely, which for a background daemon
is a hang nobody can see. Every git invocation therefore also carries
`GCM_INTERACTIVE=never`, `GIT_ASKPASS=echo`, `SSH_ASKPASS=echo` and
`-c credential.interactive=false`. Stored credentials are still returned - only
prompting is disabled, so the "user is already logged in" fallback survives. Tokens are injected as `https://<token>@host/…` and **scrubbed from
every log line and error message** before display or storage.

PRs are fetched as `refs/pull/<n>/head`, which also covers PRs from forks.

## 8. Workspaces

```
Windows  %LOCALAPPDATA%\QuickRun\runs\<owner>__<repo>__<ref>\
Linux    ~/.local/share/QuickRun/runs/<owner>__<repo>__<ref>/
macOS    ~/Library/Application Support/QuickRun/runs/<owner>__<repo>__<ref>/
```

Deliberately **not** `%TEMP%`: system cleaners and reboots delete from there,
and a half-removed `node_modules` mid-run is a bug factory.

Reuse on a second run: `git fetch` + `git reset --hard origin/<ref>` +
`git clean -fdx`, excluding known dependency caches (`node_modules`, `.venv`,
`obj`, `bin`, `target`, `vendor`) so the second start takes seconds. `--fresh`
and a UI button delete and re-clone when a workspace is broken.

`quickrun ls` and the UI list each workspace with size, last use and last
result; cleanup is per-workspace, by age, or all.

Ref names are sanitised for the filesystem (`feature/x` → `feature__x`) with a
short hash suffix when sanitisation could collide.

## 9. Run lifecycle

```
resolve trigger → workspace (clone or fetch+reset) → load config or detect
  → check requires → collect inputs → TRUST DIALOG → setup steps → tasks
  → readiness → open browser → stream logs → stop → stop steps
```

The supervisor starts each task as a child process with redirected
stdout/stderr, streams output to the UI over the Blazor circuit (and to the
console for CLI runs), waits on `readyWhen` before starting dependants, and on
stop kills the whole process tree (`Process.Kill(entireProcessTree: true)`)
before running `stop` commands.

Failure handling: a failed `requires` check blocks with the `install` hint. A
failed `setup` step aborts the run unless `continueOnError`. A task that exits
non-zero is reported and, with `restart: onFailure`, retried with backoff up to
three times.

Run history (repo, ref, commit, outcome, timestamps, log tail) is kept per
workspace so the UI can show what happened last time.

## 10. Security and trust

`quickrun://run?repo=…` reachable from any web page means: arbitrary code from
an arbitrary repository, on the user's machine, with the user's privileges, no
container. The controls are therefore not optional.

**Confirmation dialog, always, before anything executes.** It shows the repo
URL, ref, resolved commit SHA, and the full list of commands that will run —
`setup`, `tasks`, `stop`, or the chosen detection candidate — plus the origin
that triggered the run. There is no "run immediately" mode and no way to
suppress the dialog globally.

**Per-repo trust with config-hash invalidation.** A "trust this repository"
checkbox stores the repo identity together with a hash of the effective command
set. A subsequent run whose commands hash differently shows the dialog again,
so a repo cannot be trusted once and weaponised later. Trust is per repo, never
per owner or per host, and revocable in the UI.

**Additional measures.**

- The extension injects only on `github.com` origins.
- The protocol handler validates that `repo` is an `https://` URL or
  `owner/repo` shorthand on a known host; anything else is rejected, not
  guessed.
- The listener requires a pairing token (§11) and sets CORS for GitHub origins
  only. Any localhost request without a valid token gets 403.
- Secrets from `password` inputs are held in memory for the run, passed to
  children as environment variables, and never written to logs or history.
  Persisting one is opt-in per input and lands in the OS credential store.
- Tokens are scrubbed from all output (§7).

Known ceiling, documented rather than hidden: a trusted repo's commands run
unsandboxed with full user privileges. Container isolation was considered and
deferred — most repos do not run in a container unmodified. If v1 shows demand,
the natural upgrade is an opt-in `isolate: docker` per repo.

## 11. Trigger transports

Both transports funnel into the same run request. `quickrun://` is the default
because it works when the daemon is not running and inherits the browser's own
"open this application?" prompt.

### Protocol

```
quickrun://run?repo=<https url | owner/repo>&ref=<branch|tag|sha>&pr=<n>
              &subdir=<path>&config=<path>
quickrun://open                     open the dashboard
```

Registration happens in `quickrun install`, run on first launch:

- Windows: `HKCU\Software\Classes\quickrun` with `shell\open\command`
- Linux: a `.desktop` file with `MimeType=x-scheme-handler/quickrun` plus
  `xdg-mime default`
- macOS: `CFBundleURLTypes` in an `Info.plist`. This requires an app bundle,
  which a bare single-file binary is not — so the macOS artifact is a minimal
  `QuickRun.app` wrapping the binary, and `install` registers it via
  `LSRegisterURL`. The bare binary remains available for CLI-only use, where
  the protocol handler is not needed.

### Listener

`POST http://127.0.0.1:9876/api/run` with the same fields as JSON and an
`X-QuickRun-Token` header. Off by default; enabling it is a setting in both the
app and the extension.

Pairing: the user clicks "Pair browser extension" in the UI, which opens a
60-second window during which `POST /api/pair` returns a token. The extension
stores it and sends it with every subsequent request. Outside that window,
`/api/pair` returns 403. This avoids asking the user to copy-paste a secret and
avoids any web page silently obtaining one.

`GET /api/ping` (no token) returns version and status so the extension can tell
whether QuickRun is installed and running.

Chromium requires a Private Network Access preflight for `https://github.com` →
`http://127.0.0.1`; the daemon answers the preflight with the required headers.

## 12. Browser extension

One MV3 source tree. Chrome, Edge and Opera consume the same build; Firefox
gets `browser_specific_settings` and `webextension-polyfill`. Plain JavaScript,
no framework — the entire UI is one button, a badge and an options page.

Injection points, all showing the same QuickRun-icon button:

- repo home and `tree/*` — next to the branch dropdown
- `pull/*` — in the PR header, carrying `pr=<n>`
- `branches` — one button per row, carrying that branch

The button is always shown; the engine decides what is runnable (config,
run script, or detection). No GitHub API call, no token in the extension, no
rate limits.

Options: transport (protocol / listener), port, pairing, and whether to prefer
the PR head or its merge ref.

GitHub is a Turbo-driven SPA, so injection hooks `turbo:load` and a
`MutationObserver` rather than running once on load. Selectors are anchored on
`data-testid` and ARIA labels where available and fail silently when GitHub
changes its DOM — a missing button is acceptable, a broken page is not. Known
ceiling: selector drift needs extension updates; a resilient-anchor test page
in the repo makes that a fast fix rather than an investigation.

## 13. Website and documentation

VitePress at `site/`, deployed to GitHub Pages.

- Landing page in `en` and `de` — what it is, a 20-second demo, install buttons
  per OS, extension buttons per browser, link to full docs.
- Full docs in `en` and `de` — getting started, config reference, every field,
  the detection table, CLI reference, security model, troubleshooting.
- A sample gallery rendered from `samples/`, so the docs and the CI-validated
  files cannot diverge: .NET check-then-run, npm dev server, Python + venv,
  multi-service (Postgres + .NET API + Vite front end), docker-compose,
  inputs-with-secrets, platform-specific scripts.
- One playground page hosting `QuickRun.Playground` (Blazor WASM): paste or
  edit a config, get validation from the **real** Core parser, see the expanded
  canonical form and the exact commands that would run.

`schema/quickrun.schema.json` is generated from the Core types in CI and
published alongside the site, so the `yaml-language-server` comment in every
sample gives repo owners autocomplete in VS Code.

README stays English and links to both language landing pages.

## 14. Distribution

GitHub Releases carry the six single-file binaries plus checksums, built by a
release matrix workflow. The two macOS artifacts additionally ship as a
`QuickRun.app` bundle (§11) so the URL scheme can be registered. Beyond that: winget and scoop manifests for Windows, a
Homebrew tap for macOS, and an `install.sh` for Linux (with AUR later if there
is demand).

Extension listings: Chrome Web Store, Edge Add-ons, Firefox AMO, Opera
add-ons. The site links all of them and the app's UI links the store matching
the browser it is being viewed in.

## 15. Build order

Each phase ends with something demonstrable.

1. **Core + CLI** — config parse, shorthand expansion, validation, detector,
   git and auth, workspaces, supervisor, `quickrun run/validate/detect/ls/clean`.
   Fully testable headless; the whole engine lives here.
2. **Daemon + UI** — Kestrel, Blazor Server dashboard, trust dialog, generated
   input form, log streaming, workspace manager, `quickrun install` with
   protocol registration and autostart.
3. **Extension** — MV3 build for both targets, injection points, options page,
   listener transport and pairing.
4. **Site, docs, samples, playground** — VitePress with `en`/`de`, sample
   gallery, generated schema, WASM playground.
5. **Distribution** — release matrix, installers, store submissions.
6. **Deferred** — tray icon, container isolation, richer scheduling.

## 16. Testing

- xUnit against `QuickRun.Core`: shorthand expansion (every row of the table in
  §4), validation errors, interpolation, secret redaction, semver range
  comparison, detector against fixture directory trees, ref-name sanitisation,
  trust-hash invalidation.
- Git and supervisor behaviour against local temporary repositories, not the
  network.
- CI validates every file in `samples/` against the generated schema, so a
  documented example can never drift from the engine.
- The extension gets a static fixture page reproducing GitHub's DOM at each
  injection point, so selector drift is caught by running one page rather than
  by manual clicking.

## 17. Open items

- Port conflicts between a repo's hardcoded ports and whatever is already
  listening are detected and surfaced in the dialog, but not remapped. Remapping
  requires rewriting the repo's own config and is out of scope for v1.
- Windows ARM64 and Linux ARM64 binaries are built but will be less tested than
  x64 until someone reports otherwise.
- `restart: onFailure` uses fixed three-attempt backoff; configurable retry is
  deferred until asked for.
