# Repositories without a config

Most repositories have never heard of QuickRun. When there is no `quickrun.yml`, QuickRun works out
how to start the repository itself, in this order:

1. a root run script — `quickrun.ps1`, `quickrun.sh`, `run.ps1`, `run.sh`
2. **QuickRun's own collection** — a config kept for this repository, [see below](#the-collection)
3. **another launcher's config**, currently [Pinokio](#pinokio)
4. **detection** from the files that are there

Ahead of all four sits anything you have that is more specific: the repository's own `quickrun.yml`
first, then a config you saved for that repository in the config builder. Neither the collection nor
detection can ever get in front of those.

## The collection

Some repositories will never commit a `quickrun.yml`, and QuickRun reading their files is a guess —
a decent one, but a guess. So QuickRun keeps configs for known repositories and uses one when the
repository ships nothing itself. [Browse them](/collection), or look at the
[configs directory](https://github.com/fgilde/QuickRun/tree/main/configs) in the repository.

The lookup is a single request for the repository being started:

```
acme/app  →  https://quickrun.org/configs/acme/app.yml
```

The answer is cached for a day under QuickRun's own directory, and a cached config is used when the
network is unreachable — it was still written for this repository.

The plan says where it came from, in the window and in the confirmation window: *QuickRun's collected
config for this repository*. A broken collected config is skipped with a note rather than failing the
run, and detection takes over.

**What this costs, and how to switch it off.** Asking tells quickrun.org which repository you are
starting. It only happens for a repository that has nothing of its own, and never for a folder on
your machine. To never ask:

```bash
QUICKRUN_NO_COLLECTION=1
```

With that set, QuickRun behaves exactly as it did before the collection existed.

To contribute one, add `configs/<owner>/<repo>.yml` with a `repository:` field naming that same
repository. The test suite parses and validates every config in that tree, so a broken one cannot be
merged.

Whatever comes out of that is a plan like any other: the command list is shown, nothing runs until
you approve it, and the log window says where the plan came from as its first line.

To see the result without running anything:

```bash
quickrun detect            # in the repository
quickrun detect ./my-repo --save   # write it to quickrun.yml
```

`--save` never overwrites an existing file. It is the quickest way to turn a detected or foreign
config into one you can edit and commit.

### Asking for a collected config by name

Automatically, the repository's own `quickrun.yml` wins - that is the whole point of the order above,
and it does not change. But a config can be asked for by name, and then it is what runs:

```bash
quickrun run acme/app --from-collection
```

That is what the **With this config** button on [the collection page](/collection) does. A card for a
repository that ships its own config offers both, because the card shows one of them and pressing Run
has to start the thing you were looking at.

The link carries which *source* to use, never the commands: `#run?repo=acme/app&config=collection`
tells QuickRun to fetch the config itself. A link that could carry commands would be a link that can
put commands in front of somebody, and that stays impossible.

Asked for by name with nothing kept for that repository is an error rather than a quiet fall back to
the repository's own - otherwise a button that says "run this config" would run a different one.

## Pinokio

A [Pinokio](https://pinokio.co) app ships a `pinokio.js` next to `install.js` and `start.js`, whose
exported `run` array is a list of `{ method, params }` steps. QuickRun reads those scripts and
translates them, so a Pinokio repository runs from the GitHub page like anything else — without
Pinokio installed.

::: v-pre
| Pinokio | Becomes |
|---|---|
| `install.js` / `install.json` | `setup` steps |
| `start.js` / `start.json` | the tasks |
| `shell.run` `message` | the command, one shell per step |
| `shell.run` `path` | `cwd` |
| `shell.run` `venv` | a `python -m venv` step, then activation in front of each command |
| `shell.run` `env` | the task's `env` |
| `on: [{ event, done: true }]` | `readyWhen.log`, with a JavaScript `/…/i` flag preserved |
| `local.set` `url` | the address QuickRun opens when the task is ready |
| `when` | evaluated; a step whose condition is false is left out |
| `script.start` | the named script is read inline, its `params` become `{{args}}` |
| `fs.download` | a `curl -L -o` step |
| `git`, `python`, `uv`, `node` in a command | a tool requirement, checked before the run |

`{{ … }}` templates are evaluated, including the ternaries those scripts pick their command with:
`{{platform === 'win32' && gpu === 'amd' ? 'python main.py --directml' : 'python main.py'}}`.
`platform`, `arch`, `cwd`, `gpu`, `args` and `local` are provided.
:::

### What QuickRun does not do

Pinokio scripts may be JavaScript functions rather than literals — `module.exports = async (kernel)
=> { … }`. Those ask Pinokio's own runtime for a free port or the machine's GPU list, and QuickRun
cannot read them. It says so instead of guessing: if the **start** script is a function there is
nothing to run and detection takes over; if only the **install** script is, the app still starts but
the log says the install was skipped.

The same applies to a `when` condition QuickRun cannot evaluate, to `fs.link`'s shared model drive,
and to steps asking for `sudo` — each is left out and counted in the log. `conda` is Pinokio's own
bundled environment; a script that needs it gets a note rather than a broken run.

### Which accelerator

Scripts branch on `gpu`. QuickRun decides without executing anything: `nvidia` when `nvidia-smi` is
on the `PATH`, `apple` on macOS, otherwise unknown — which is the CPU variant, and always works.
Override it when you know better:

```bash
QUICKRUN_GPU=amd quickrun run https://github.com/pinokiofactory/comfy
```

## Detection

When nothing else says how to start the repository, QuickRun looks at what is in it. Every
candidate is shown with the command it would run; the highest-ranked one is used, and the others are
listed so you can pick another with `--config` or a committed `quickrun.yml`.

| Marker | Command | Address |
|---|---|---|
| `quickrun.sh`, `run.sh`, `run.ps1` | the script | — |
| `docker-compose.yml` | `docker compose up` | the first published port |
| `package.json` with `dev`, `start` or `serve` | `npm run …` (`pnpm`, `yarn`, `bun` when a lockfile says so) | `--port` in the script, else the framework's default |
| `.csproj` (`Microsoft.NET.Sdk.Web`) | `dotnet run --project …` | `launchSettings.json`, else pinned to 5000 |
| `.csproj` (Aspire, `OutputType Exe`) | `dotnet run --project …` | — |
| `.csproj` (WPF, WinForms, Avalonia, `WinExe`) | `dotnet run --project …` | none - it waits for the window |
| `manage.py` | `python manage.py runserver` | 8000 |
| `app.py` with `streamlit` | `python -m streamlit run app.py` | 8501 |
| `app.py`/`main.py` with `gradio` | `python app.py` | 7860 |
| `requirements.txt`, `pyproject.toml`, `uv.lock` | a `.venv`, `uv run` or `poetry run` | fastapi 8000, flask 5000 |
| `Procfile` | every process, the `web` one first, `$PORT` pinned to 8080 | 8080 |
| `.replit` | its `run =` line | a `--port` it names |
| `Makefile`, `Taskfile.yml`, `justfile` | `make run`, `task dev`, `just run` | — |
| `Cargo.toml`, `go.mod`, `pom.xml`, `build.gradle` | `cargo run`, `go run ./...`, `mvn spring-boot:run`, `./gradlew bootRun` | Spring 8080 |

A test or benchmark project is never offered as something to start.

A desktop application has no address to wait for, so a detected one gets
[`readyWhen: {window: true}`](/config#readywhen) instead: the run counts as ready when the window is
there, brings it to the front, and shows the process id. Without that, `dotnet run` reports success
while the app is still building and the window is nowhere to be seen.

The address is what makes a detected run useful: it becomes a `readyWhen` check and an `open`, so
QuickRun waits for the app and hands you the link instead of leaving you to find it in the log. A
guessed port that turns out wrong costs a readiness timeout, not the run — and if the app prints its
address, the log window picks that up as well.
