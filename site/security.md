# Security

**QuickRun executes code from the repository you point it at, with your privileges, outside any
sandbox.** Everything below follows from that.

## The confirmation gate

Every run shows the repository, the ref, the resolved commit and the exact list of commands —
`setup`, `tasks`, `stop`, or the detected fallback — and waits for you to approve it. There is no
setting that disables this, and a run triggered from the browser cannot skip it: the listener
prepares the run and returns the plan, and a second, explicit confirmation starts it.

`--yes` on the command line skips the prompt, because you typed the command yourself. Nothing a web
page can do reaches that flag.

## Trusting a repository

Approving a repository stores a hash of *those commands*. If the config changes, the hash changes
and you are asked again — so a repository cannot be trusted once and quietly turned into something
else later. Trust is per repository, never per owner or per host, and revocable.

The hash deliberately ignores the repository, ref and commit: new commits to a trusted repository
keep working, changed commands do not.

## The listener

- Bound to `127.0.0.1` only, never to an address reachable from the network.
- Every endpoint except `GET /api/ping` requires the pairing token.
- `/api/ping` needs no token on purpose: telling a page that QuickRun exists is its entire job. It
  reveals nothing else — no repository names, no paths, no run contents.
- CORS is granted to `https://github.com` alone.
- A token is issued only while a pairing window is open, and that window can only be opened from
  your machine.

## Secrets

Values from `password` inputs are held in memory for the run, passed to child processes as
environment variables, and never written to logs, run history or progress text. Access tokens are
removed from every log line and error message before they are shown or stored.

Very short values are not redacted: blanket-replacing a one-character secret would mangle every log
line it happened to appear in.

## Auto-update

Updating is a code-execution channel and is treated as one:

- the asset is fetched only from the release's own `github.com` download URL over HTTPS
- its SHA-256 must match the `SHA256SUMS` published with the release; a mismatch aborts
- QuickRun only replaces its own binary when nothing else manages it — a package-manager install is
  told the upgrade command instead
- the update is applied on restart, never in the middle of a run
- `--no-update` disables checking entirely

## What is not protected

- **No sandbox.** A trusted repository's commands run with your full privileges. Container
  isolation was considered and deferred: most repositories do not run in a container unmodified.
- **Unsigned binaries.** See [install](/install) for what that means on each platform.
- **The commands themselves.** QuickRun shows you what will run. It cannot tell you whether it is
  safe. Only run repositories you would be willing to `git clone && ./run.sh` by hand.

## Reporting a problem

Open an issue at [github.com/fgilde/QuickRun](https://github.com/fgilde/QuickRun/issues). For
anything you believe is exploitable, use GitHub's private vulnerability reporting on that repository
rather than a public issue.
