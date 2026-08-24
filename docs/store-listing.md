# Browser store listings

Everything a store submission asks for, ready to paste. The workflow uploads new versions
automatically once the credentials in [publishing.md](publishing.md) are configured; this file
covers the parts that live in each store's dashboard rather than in the repository.

## What still needs a person, once per store

| Store | Account | Cost |
|---|---|---|
| Chrome Web Store | [Developer dashboard](https://chrome.google.com/webstore/devconsole) | one-off 5 USD registration fee |
| Edge Add-ons | [Partner Center](https://partner.microsoft.com/dashboard/microsoftedge) | free |
| Firefox Add-ons | [addons.mozilla.org](https://addons.mozilla.org/developers/) | free |

Each requires accepting a developer agreement, which cannot be automated. Chrome additionally needs
one manual upload to allocate the extension id that `CHROME_EXTENSION_ID` then refers to.

## Name

```
QuickRun
```

## Summary (Chrome: 132 characters max)

```
Run any git repository with one click, straight from the GitHub page you found it on.
```

German:

```
Jedes Git-Repository mit einem Klick starten - direkt von der GitHub-Seite, auf der du es gefunden hast.
```

## Description

```
QuickRun puts a Run button on GitHub: next to the branch dropdown, in pull request headers, and on
every row of the branch list. Clicking it hands the repository to the QuickRun application on your
own machine, which checks it out, installs what it needs, and starts it.

A repository owner commits a quickrun.yml describing how their project starts. Repositories without
one still work: QuickRun recognises compose files, npm scripts, .NET projects, Python apps,
Makefiles, Cargo, Go, Maven and Gradle, and offers what it found.

Nothing runs unseen. Clicking the button does not start anything: QuickRun checks the repository
out, then this extension shows you the repository, the ref, the resolved commit and the exact
commands, and waits for your confirmation. That confirmation appears in an extension window rather
than in the page, so no web page can draw a convincing fake over it.

Progress comes back into the button while the repository starts.

Requires the QuickRun application: https://fgilde.github.io/QuickRun
Source code: https://github.com/fgilde/QuickRun
```

German:

```
QuickRun setzt einen Run-Button auf GitHub: neben das Branch-Dropdown, in PR-Header und in jede
Zeile der Branch-Liste. Ein Klick übergibt das Repository an die QuickRun-Anwendung auf deiner
eigenen Maschine, die es auscheckt, die Voraussetzungen einrichtet und es startet.

Repository-Owner committen eine quickrun.yml, die beschreibt, wie ihr Projekt startet. Repositories
ohne eine funktionieren trotzdem: QuickRun erkennt Compose-Dateien, npm-Skripte, .NET-Projekte,
Python-Apps, Makefiles, Cargo, Go, Maven und Gradle und schlägt vor, was es gefunden hat.

Nichts läuft unbesehen. Ein Klick auf den Button startet noch nichts: QuickRun checkt das
Repository aus, dann zeigt diese Erweiterung Repository, Ref, aufgelösten Commit und die exakten
Befehle und wartet auf deine Bestätigung. Diese Bestätigung erscheint in einem Extension-Fenster und
nicht in der Seite, damit keine Webseite eine überzeugende Fälschung darüberlegen kann.

Der Fortschritt kommt während des Starts in den Button zurück.

Benötigt die QuickRun-Anwendung: https://fgilde.github.io/QuickRun
Quellcode: https://github.com/fgilde/QuickRun
```

## Category

Developer Tools.

## Single purpose statement (Chrome requires one)

```
QuickRun adds a button to github.com that hands the repository you are viewing to the QuickRun
application on the same machine, so it can be checked out and started.
```

## Permission justifications

Chrome and Edge ask for one sentence per permission. Firefox asks for the same information in the
reviewer notes.

| Declared | Why |
|---|---|
| `storage` | Stores the pairing token for the local QuickRun application, the port, and two preferences. Nothing is sent anywhere. |
| `host_permissions: http://127.0.0.1/*`, `http://localhost/*` | The only way to reach the QuickRun application, which listens on loopback. This is also how the extension knows whether QuickRun is installed: a browser cannot be asked whether a `quickrun://` handler exists. |
| `content_scripts` on `https://github.com/*` | Where the Run button is injected, and the only site the extension touches. |

The extension deliberately does **not** request the `tabs` permission: `tabs.create` and
`tabs.sendMessage` do not need it, and it would grant read access to the URL and title of every open
tab.

## Data use declaration

- No data is collected, transmitted or sold.
- No analytics, no telemetry, no remote code.
- The pairing token stays in the browser's extension storage and is only ever sent to
  `127.0.0.1`.
- Repository names and refs are sent to the local application on `127.0.0.1` in order to run them,
  and nowhere else.

Privacy policy URL: <https://fgilde.github.io/QuickRun/privacy>

## Screenshots

Not in this repository — stores want real screenshots, and they go stale with every redesign.
Capture at 1280×800:

1. A GitHub repository page with the **Run this** button next to the branch dropdown.
2. The confirmation window showing the command list.
3. The button mid-run, showing a percentage.
4. The QuickRun dashboard with a run in progress.

## Reviewer notes

```
QuickRun requires a companion application on the user's machine, downloadable from
https://fgilde.github.io/QuickRun. Without it the button shows "Install QuickRun" and links to that
page; the extension does nothing else on its own.

The extension talks only to http://127.0.0.1 and only after the user has explicitly paired it: the
application hands out a token exclusively while a pairing window is open, and that window can only
be opened from the machine itself.

The extension never executes anything by itself. It asks the local application to prepare a run,
which returns the list of commands; the user then approves that list in an extension window before
anything executes.
```
