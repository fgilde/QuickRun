---
layout: home

hero:
  name: QuickRun
  text: Run any git repository with one click
  tagline: From the GitHub page you found it on, without reading a line of setup documentation.
  image:
    src: /logo.png
    alt: QuickRun
  actions:
    - theme: brand
      text: Download
      link: /download
    - theme: alt
      text: Config reference
      link: /config
    - theme: alt
      text: GitHub
      link: https://github.com/fgilde/QuickRun

features:
  - title: One command
    details: >
      quickrun run acme/app checks the repository out into a managed workspace, verifies the
      prerequisites, asks for whatever inputs the config declares, and starts it.
  - title: A tray icon and a dashboard
    details: >
      Double-click the binary and QuickRun sits in the tray. The dashboard shows what is running
      with live progress, the workspaces on disk, and how the browser extension works.
  - title: A button on GitHub
    details: >
      The browser extension puts a Run button next to the branch dropdown, in pull request headers
      and on every row of the branch list. Progress comes back into the button.
  - title: Works without a config
    details: >
      No quickrun.yml? QuickRun recognises compose files, npm scripts, .NET projects, Python apps,
      Makefiles, Cargo, Go, Maven and Gradle, and offers what it found.
  - title: Nothing runs unseen
    details: >
      Every run shows the repository, ref, resolved commit and the exact commands, and waits for
      your confirmation. There is no way to disable that prompt.
---

## The shortest config that works

```yaml
run: ./run.sh
```

## A more useful one

<<< @/../samples/npm-dev.yml{yaml}

Every block is optional. See the [config reference](/config) for the whole shape, or the
[samples](/samples) for eight worked examples — including a multi-service stack, a generated input
form with a validated secret, and a repository that installs its own SDK.

## How it fits together

```
GitHub page (extension button)
   │
   ├── http://127.0.0.1:9876/api/run   ← the main channel: also how the button knows QuickRun exists
   └── quickrun://open                 ← starts the daemon when it is installed but not running
   │
   ▼
QuickRun on your machine
   git checkout → prerequisites → inputs → confirmation → setup → tasks → progress back to the button
```

A browser cannot be asked whether a URL scheme has a handler, so the localhost listener is what
tells the extension that QuickRun is installed — and what carries progress back while a repository
starts. See [browser extension](/extension).
