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
- Every endpoint that can start something requires the request to come from a browser extension,
  checked against the `Origin` header the browser attaches and a page cannot forge.
- `/api/ping` answers anyone on purpose: telling a page that QuickRun exists is its entire job. It
  reveals nothing else — no repository names, no paths, no run contents.
- `https://github.com` is deliberately **not** an accepted origin. A script running on GitHub
  itself cannot start a run.
- A caller with no `Origin` is not a browser — `curl`, or QuickRun's own CLI — and is allowed. Such
  a program already runs with your privileges and gains nothing by going through the daemon.
- There is no pairing token any more. It guarded against exactly what the origin check guards
  against, while costing every user a setup step.

## Sites allowed to open the window

A web page still cannot start anything. What a page on a trusted site may do is ask the local
QuickRun to open **its own window** on a plan, instead of sending you through a `quickrun://` link
or a new tab to get to the same place.

- `POST /api/show` is the only endpoint a web page ever reaches, and it does one thing: open the
  window. Everything that could start, stop or read a run stays behind the extension check above,
  which a page still cannot pass. The worst a trusted site can do is make a window appear.
- The plan that appears there waits for you, exactly as it does when the extension puts it there.
- `*.quickrun.org` is on the list to begin with, because that is where QuickRun is downloaded from:
  whoever installed it from that page has already trusted it with rather more than a window.
- Only `https` counts. On plain `http` anything in between can rewrite the page while the `Origin`
  header still reads as the trusted name, so the name would be no evidence at all. `http://localhost`
  is the exception - there is nothing in between.
- The subdomain form matches whole labels. `*.example.com` covers `example.com` and
  `app.example.com`, and never `notexample.com` or `example.com.attacker.net`.
- A trusted site may name a repository and which config to use. It may **not** name a file on your
  machine: that is refused here whatever the site, and remains something you point at yourself.
- A page cannot add itself to the list. The list is edited in **Settings** in QuickRun's window,
  which is behind its own token, or in the file that window names.
- Emptying the list turns the whole thing off, including the default. Every page then falls back to
  the link, which is what it did before this existed.

## What a link may carry

A `quickrun://` link - what a [README badge](/badge) ends up following - is a string written by
whoever wrote the page. So it is treated as one:

- Only `repo`, `ref` and `pr` survive. A command, a config, a token or a local path in the link is
  dropped without comment, because none of it was ever ours to accept.
- `repo` must be `owner/name` or an `https://` URL. `ssh://`, `file://` and `git@host:owner/name`
  are refused from a link - typing them on the CLI yourself is a choice, a link doing it is not.
- The link never starts anything. It opens QuickRun's own window at the repository, where the plan
  is prepared and the confirmation gate applies exactly as it does everywhere else.

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
