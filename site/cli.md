# CLI

QuickRun is fully usable without the browser extension or any UI.

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
| `--fresh` | delete the workspace and clone again |
| `-y, --yes` | skip the confirmation prompt; missing required inputs then fail rather than prompt |
| `--no-open` | do not open any browser URL the config asks for |

Nothing executes before the plan is printed and confirmed, unless `--yes` is given.

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

Shows how QuickRun would start a repository that has no config. `--save` writes the highest-ranked
candidate to `quickrun.yml` and refuses to overwrite an existing file.

## `quickrun ls` and `quickrun clean`

```bash
quickrun ls
quickrun clean --all
quickrun clean --older-than 30d
quickrun clean acme__app__main-1a2b3c
```

`clean` requires exactly one selector. Deleting everything by default would be the worst possible
guess, so no selector is a usage error.

## `quickrun daemon` and `quickrun pair`

```bash
quickrun daemon              # listen on 127.0.0.1:9876
quickrun daemon --pair       # and open a pairing window at startup
quickrun pair                # open a pairing window for a running daemon
quickrun pair --revoke       # invalidate the token
```

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

## Authentication

For a private repository, first hit wins:

1. `--token`
2. `QUICKRUN_TOKEN`
3. `gh auth token`, if you are logged into the GitHub CLI
4. plain `git clone`, which picks up SSH keys and Git Credential Manager

Tokens are removed from every log line and error message. Credential prompts are disabled
throughout, including the GUI dialog Git Credential Manager would otherwise open — for a background
daemon that would be an invisible hang.
