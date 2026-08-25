# Browser extension

The extension puts a Run button where you already are: next to the branch dropdown on a repository
page, in a pull request header, and on every row of the branch list.

## Why it needs the local listener

A browser cannot be asked whether a URL scheme has a handler. That is deliberate — it would be a
fingerprinting vector — and there is no API for it. So an extension whose only channel is
`quickrun://` can never know whether QuickRun is installed, and can never receive progress back from
a run it triggered.

The localhost listener is therefore the main channel, and `quickrun://` has exactly one job left:
starting a daemon that is installed but not running.

| Ping result | The button shows |
|---|---|
| QuickRun answers | **Run this** — clicking prepares a run |
| no answer, then `quickrun://` works | **Starting QuickRun…**, then Run this |
| no answer at all | **Install QuickRun** — links to the download page |

## Button states

- **ready** — QuickRun answered. Click to prepare a run.
- **running** — the current phase and, where a real number exists, a percentage.
- **done** — the run finished.
- **error** — hovering shows why.

## The confirmation window

Clicking Run does not start anything. QuickRun checks the repository out, builds the plan, and the
extension opens a window listing the repository, ref, resolved commit and the **exact commands**
that will run. Only the button in that window starts them.

The window also shows the `description` from the config when it has one, the folder the repository
was checked out into, and — once a task reports one — the address it is listening on, as a link.

After you approve, that window stays open and becomes the run's log: the checkout with its real
progress counters, every setup step, and everything the repository's own commands print. The button
on the page shows only a percentage and a coarse phase — a toolbar button is no place for a hundred
lines of build output.

**Stop** stops the run and says so: the banner turns to *Stopped* and the window offers Close. It is
only clickable while something is actually running - a run whose processes have all exited has
nothing left to stop.

That window is an extension page, not part of the GitHub page, and that is on purpose: a web page
can draw a convincing fake panel over its own content, and nobody should ever approve one set of
commands while a different set runs.

## Why no web page can drive it

There is nothing to pair. A browser attaches an `Origin` header to every cross-origin request and
a page cannot change it, so QuickRun simply refuses anything that does not come from a browser
extension. `https://github.com` is not on that list: a script running on GitHub itself cannot start
a run.

A program on your own machine — `curl`, QuickRun's own CLI — sends no `Origin` and is allowed. It
already runs with your privileges, so the daemon grants it nothing it did not have.
## Options

| Setting | Default |
|---|---|
| Port | 9876 |
| Try `quickrun://` when QuickRun does not answer | on |

Running a pull request means running the branch it comes from, fetched as `refs/pull/<n>/head`.
That works for pull requests from forks too, and it is what the button on a pull request page does.

## Building it yourself

```bash
cd extension
sh build.sh
```

One source tree, two builds: `dist/chromium` for Chrome, Edge and Opera, and `dist/firefox`, which
differs only in its manifest.

## When GitHub changes its DOM

The button is anchored on `data-testid` attributes and ARIA labels where GitHub provides them, and
every lookup fails silently: a missing button is acceptable, a broken GitHub page is not. GitHub
does redesign these pages, so a missing button usually means the extension needs an update rather
than that something is wrong with your setup.
