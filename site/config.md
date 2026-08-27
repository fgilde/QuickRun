# Config reference

`quickrun.yml` in the repository root. Every block is optional; the only requirement is that the
file describes *something* to execute.

Add the schema comment and your editor will complete the fields for you:

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
```

## Shorthand

The parser expands shorthand before anything runs, so the engine only ever sees one shape.

| Written | Means |
|---|---|
| `run: ./run.sh` | one task named `run` |
| `run: {linux: ./run.sh, windows: ./run.ps1}` | one task, the command picked per platform |
| `setup: [npm ci, dotnet restore]` | two sequential steps |
| `tasks: [npm start, python api.py]` | two tasks, named `task-1` and `task-2` |

A `run:` value that is a mapping is a **platform map** if all of its keys are `windows`, `linux` or
`macos`; anything else is an error rather than a guess.

If a repository has `run.sh`, `run.ps1`, `quickrun.sh` or `quickrun.ps1` in its root and no
`quickrun.yml` at all, QuickRun offers that script directly.

## Full shape

```yaml
version: 1                     # optional, default 1
name: My App                   # optional, default is the repository name
description: ...               # optional
icon: assets/logo.png          # optional, repository-relative path or URL
docs: https://...              # optional, shown in the UI

requires:                      # optional prerequisite checks
  - tool: dotnet               # any command; known tools get better version probes
    version: ">=9.0"           # optional
    install: https://dot.net   # optional, shown when the tool is missing
    optional: false            # default false; true warns instead of blocking

inputs:                        # optional, drives the generated form
  - id: apiKey                 # required, letters, digits, underscore
    label: OpenAI API key      # default is the id
    type: password             # text|password|number|bool|select|path|dir|file
    description: ...
    default: null
    required: true             # default false
    pattern: "^sk-"            # text-like types only
    min: 1                     # number only
    max: 65535                 # number only
    options: [dev, prod]       # select only; also [{value, label}]
    env: OPENAI_API_KEY        # exported to every command
    persist: false             # default false

env:                           # optional, for every command
  ASPNETCORE_ENVIRONMENT: Development

setup:                         # optional, sequential, before the tasks
  - run: npm ci
    cwd: web
    when: [linux, macos]       # optional platform filter; a scalar works too
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
    open: true                 # true opens the readyWhen URL; or give an explicit URL

stop:                          # optional, run when you stop the app
  - docker compose down
```

## `requires`

A requirement is checked before anything runs. What is missing and can be installed **is** installed
- into QuickRun's own folder, never into the system:

| Tool | Where it comes from |
|---|---|
| `dotnet` | Microsoft's `dotnet-install` script, at the channel your `version` implies (`>=10` means 10.0) |
| `node` | the newest build on nodejs.org that your `version` accepts |
| `pnpm`, `yarn` | npm - which arrives with the Node above when the machine has none |
| `pwsh` | the PowerShell package on NuGet, installed as a .NET tool |

A tool that needs another one brings it: `pnpm` on a machine without Node installs Node first, and
`pwsh` installs a .NET runtime to run on. Both stay inside `~/.quickrun/tools` with everything else.

Anything else is checked and reported, with your `install` line if you gave one. The confirmation
window lists what would be installed before you approve it, and the CLI prints it before it asks.

Three things it will not do: touch a version the machine already has (a satisfied requirement
installs nothing), install for an `optional: true` requirement, or write to your PATH - the
downloaded toolchain is put in front of the PATH of that run's own processes and nowhere else.
Everything lands under `~/.quickrun/tools`, so deleting that folder undoes it completely.

## `readyWhen`

Exactly one of these, or omit the block to mean "ready as soon as the process started":

| Form | Waits for |
|---|---|
| `{port: 5432}` | a TCP connect to `127.0.0.1:5432` to succeed |
| `{http: "http://localhost:5000/health"}` | an HTTP response below 500 — a dev server answering 404 is up |
| `{log: 'Now listening on: (?<url>\S+)'}` | the pattern to appear in the task's output |
| `{delay: 5s}` | a fixed wait — `500ms`, `5s`, `2m`, or a bare number of seconds |
| `{window: true}` | a window of the task's process — for desktop applications |

A desktop application has no port and no URL, so `{window: true}` waits for the thing a user
actually waits for: its window. QuickRun watches the process and everything it starts - `dotnet run`
launches the app as a child - and counts the task as ready when one of them has a window. It then
brings that window to the front, and the log and the status show the process id. Detection puts this
on desktop projects by itself (WPF, WinForms, Avalonia, `WinExe`), so most repositories need no
config for it at all. Windows only for now; elsewhere the task counts as started.

Readiness describes the *service*, not the process. `docker compose up -d db` exits long before its
port opens, so a task that exits cleanly keeps its readiness watcher running.

A task that exits with a non-zero code fails the run, and the run says which task and which code.
An application that could not start - a port already taken, a missing connection string - is not a
finished run, however far the log got before it died.

Before a task starts, the address its `readyWhen` names is probed once. If something is already
answering there, the log says so: readiness cannot tell two servers apart, so it would have passed
on a stranger while the task itself failed to bind. The usual reason is an earlier run of the same
repository that is still going.

A readiness check that never fires is not fatal. QuickRun waits three minutes, then says so in the
log and counts the task as started - the process keeps running, because "it never answered on that
port" and "it is broken" are not the same thing. The progress bar moves when a task *starts* as well
as when it becomes ready, so a slow application no longer looks stuck.

`open: true` needs an address. With `port` or `http` that is the one you declared. With `log` there
is none, so QuickRun takes the last loopback address the task printed - which is the line the
pattern was waiting for. Only `localhost`, `127.0.0.1`, `0.0.0.0` and `[::1]` count: a build log is
full of links to documentation and advisories, and none of those are where the app is running.

::: warning
Use single quotes for a `log` pattern. In double-quoted YAML, `\S` is an invalid escape and the file
will not parse.
:::

## `dependsOn`

A task waits for every task it names to become ready. Cycles are rejected when the config is
validated, not at run time. A dependency with no `readyWhen` produces a warning: its dependants
start as soon as it launches, which is rarely what was meant.

## Interpolation

| Placeholder | Expands to |
|---|---|
| `${inputs.apiKey}` | the value of that input |
| `${env.HOME}` | an environment variable, empty when unset |
| `${workspace}` | the absolute path of the checkout |
| `${repo.name}` | the repository name |
| `${repo.ref}` | the branch, tag or commit being run |

Available in `run`, `cwd`, `env` values, `open` and `readyWhen` - so a task that starts on
`${inputs.port}` can wait for that port too. A reference to an input that does not exist is a
validation error, not an empty string. Secret inputs are substituted but never written to logs, run
history or progress text.

## The environment a command gets

From general to specific, later winning:

1. what QuickRun sets - currently `MSBUILDDISABLENODEREUSE=1`, because MSBuild's reusable worker
   nodes outlive the build that started them and hold its output pipe open, which made a finished
   `dotnet restore` look like a run frozen at that step. A config that wants them back can just set
   the variable itself
2. the config's own `env` block
3. the value of every input that names an `env`
4. the task's own `env`

All four are interpolated, so `ADDRESS: http://localhost:${inputs.port}` is a normal thing to write.

## Shell

Commands run through the platform shell: `cmd /c` on Windows, `/bin/sh -c` elsewhere. A command
starting with a `.sh` script on Windows is retried through Git for Windows' `bash.exe` when it is
present, so a repository shipping only `run.sh` behaves sensibly there.

## Checking your config

```bash
quickrun validate            # in the repository
quickrun validate ./my-repo
```

Validation reports errors and warnings separately; warnings do not fail. Repository owners can wire
it into a pre-commit hook.
