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

## `readyWhen`

Exactly one of these, or omit the block to mean "ready as soon as the process started":

| Form | Waits for |
|---|---|
| `{port: 5432}` | a TCP connect to `127.0.0.1:5432` to succeed |
| `{http: "http://localhost:5000/health"}` | an HTTP response below 500 — a dev server answering 404 is up |
| `{log: 'Now listening on: (?<url>\S+)'}` | the pattern to appear in the task's output |
| `{delay: 5s}` | a fixed wait — `500ms`, `5s`, `2m`, or a bare number of seconds |

Readiness describes the *service*, not the process. `docker compose up -d db` exits long before its
port opens, so a task that exits cleanly keeps its readiness watcher running.

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

Available in `run`, `cwd`, `env` values and `open`. A reference to an input that does not exist is a
validation error, not an empty string. Secret inputs are substituted but never written to logs, run
history or progress text.

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
