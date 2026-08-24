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
extension opens a small window listing the repository, ref, resolved commit and the **exact
commands** that will run. Only the button in that window starts them.

That window is an extension page, not part of the GitHub page, and that is on purpose: a web page
can draw a convincing fake panel over its own content, and nobody should ever approve one set of
commands while a different set runs.

## Pairing

Every endpoint except the ping requires a token, and a token is handed out only while a pairing
window is open on your machine:

```bash
quickrun pair
```

Then click **Pair** in the extension options within 60 seconds. The token stays in the browser's
extension storage; it is never given to a web page, and the content script never sees it.

`quickrun pair --revoke` invalidates it.

## Options

| Setting | Default |
|---|---|
| Port | 9876 |
| Try `quickrun://` when QuickRun does not answer | on |
| For pull requests, run the merge result instead of the head | off |

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
