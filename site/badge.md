# README badge

A badge in your README that runs your project. No extension needed on the other side — whoever
clicks it either lands in QuickRun or lands on the download page.

[![QuickRun](https://quickrun.org/badge.svg)](/run?repo=fgilde/QuickRun)

## The snippet

Replace `owner/repo` with yours:

```markdown
[![QuickRun](https://quickrun.org/badge.svg)](https://quickrun.org/run?repo=owner/repo)
```

The [run page](/run) has a field that writes the line for you, and a Copy button.

A specific branch or a pull request, when the default is not what you want people to run:

```markdown
[![QuickRun](https://quickrun.org/badge.svg)](https://quickrun.org/run?repo=owner/repo&ref=develop)
```

German readers can be sent to `/de/run?repo=…`, which is the same page in German.

## What happens on a click

1. The badge is a normal link, so GitHub renders it and no one has to install anything to see it.
2. The page it opens asks `http://127.0.0.1:9876/api/ping` whether QuickRun is on that machine.
3. If it answers, **Open in QuickRun** goes straight to QuickRun's own page on `127.0.0.1` - an
   ordinary http address, so there is no URL scheme, no handler registration and no permission
   dialog in the way.
4. QuickRun shows the plan for your repository — the commands, the ref, the resolved commit.
   Nothing runs until the person confirms it there.
5. If nothing answers the ping, the button follows `quickrun://run?repo=…` instead, which starts
   QuickRun when it is installed, and the download is offered next to it for when it is not.

## Why the badge does not link `quickrun://` directly

It cannot. GitHub strips link schemes it does not know from a rendered README, so a
`quickrun://` link in a README is not a link at all. And a browser will not tell a page whether a
scheme has a handler — that would be a fingerprinting vector — so the https page in the middle is
also what makes step 5 possible.

## What the link may carry

`repo`, `ref` and `pr`. Nothing else survives: no command, no config, no token, no local path. The
link says what to look at, never what to execute — see [Security](/security).

A `repo` may be `owner/name` or an `https://` URL. `ssh://`, `file://` and `git@host:owner/name` are
refused from a link, although you can still type them yourself on the [CLI](/cli).

## The badge image

`https://quickrun.org/badge.svg` — 150×20, the usual badge shape, so it sits next to
the ones your README already has. Hotlink it; it is served from the same GitHub Pages site as this
page.
