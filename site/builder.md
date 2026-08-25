# The config builder

Open QuickRun's local UI and pick **Config builder**, or go straight to
[`http://127.0.0.1:9876/#builder`](http://127.0.0.1:9876/#builder).

It is for two jobs: writing the `quickrun.yml` for a repository you own, and keeping your own config
for a repository you do not.

## The loop

1. **Load** — type a repository and press Load. QuickRun checks it out and puts the config it would
   use into the editor, saying where that came from:

   | Badge | Means |
   |---|---|
   | your config | the override you saved for this repository |
   | the repository's own config | its committed `quickrun.yml` |
   | derived from Pinokio scripts | translated from `install.js` / `start.js` |
   | detected, not committed | QuickRun's guess from the files that are there |
   | a starting point | nothing recognisable - an empty template |

2. **Edit** — the editor completes the keys of whatever block the cursor is in, from the same
   [schema](https://fgilde.github.io/QuickRun/quickrun.schema.json) QuickRun publishes, and has
   snippets for the blocks nobody remembers by heart: a task with a readiness check and an `open`, a
   `requires` entry for .NET, Node or Docker, a secret input.

3. **Check** — parsed and validated by the daemon, with the same parser and validator a run uses.
   Errors and warnings appear under the editor and in the gutter. A browser-side approximation of
   the schema would eventually disagree with the real thing; this cannot.

4. **Test against the repository** — prepares a real run from the text in the editor. The config
   being written wins over the repository's own and over any override, so what you see is what you
   are testing. The command list appears, and nothing runs until you confirm it.

5. **Save as my config** — keeps it for that repository. Or, for your own repository, copy the text
   into `quickrun.yml` in the repository root and commit it.

## Where your own config lives

`Save as my config` writes to QuickRun's own directory, not into the checkout:

```
<workspace root>/configs/<owner>_<repo>-<hash>/__auto_quickrun.config.yml
```

That is deliberate. In the checkout the file would be deleted by `--fresh`, would show up in
`git status` of a repository that is not yours, and could be committed to a stranger's project by
accident. It applies to **every branch** of that repository, and the **Configs you saved** list at
the bottom of the tab reopens or removes it.

## Which config a run uses

Most specific first:

1. a config passed on the command line — `quickrun run … --config path/to/other.yml`
2. the text in the builder, while you are testing it
3. your saved config for that repository
4. the repository's own `quickrun.yml`
5. another launcher's scripts — see [repositories without a config](/no-config)
6. detection

When your config is used instead of a `quickrun.yml` the repository ships, the run says so in its
first log line. A run that silently ignored a committed config would be a mystery worth hours.

## When it is finished

For your own repository the file belongs in the repository root as `quickrun.yml`, committed. The
schema line at the top gives the same completion in VS Code, JetBrains and anything else that speaks
YAML language server, and `quickrun validate` checks it in a pre-commit hook:

```bash
quickrun validate
```

## The editor

Monaco, bundled down to the editor and YAML highlighting, served by the daemon from the binary - no
CDN, so it works offline and nobody outside your machine learns which config you are editing.

If it fails to load for any reason the tab falls back to a plain text area. Checking, testing and
saving are unaffected: all three happen in the daemon.
