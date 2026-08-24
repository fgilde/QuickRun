# Privacy

QuickRun collects nothing.

There is no analytics, no telemetry, no crash reporting and no account. Nothing about you or the
repositories you run is sent anywhere.

## What leaves your machine

Three things, all of them requests you triggered:

| Request | To | Why |
|---|---|---|
| `git clone` / `git fetch` | the repository's host, usually github.com | to check out the code you asked to run |
| the release API and a release asset | api.github.com, github.com | to check for and download an update; disable with `--no-update` |
| whatever the repository's own commands do | wherever those commands point | QuickRun starts them; what they contact is up to them |

That last row is worth being plain about: QuickRun runs commands from the repository you point it
at. Those commands can reach the network, and QuickRun neither restricts nor inspects that. The
commands are shown to you before anything runs.

## What stays on your machine

- **Workspaces** — the checked-out repositories, under the OS application-data directory.
- **Run history** — repository, ref, commit, outcome and a log tail, inside each workspace.
- **The pairing token** — a random value in the QuickRun data directory, and a copy in the
  browser extension's own storage.
- **Values you enter** — inputs a `quickrun.yml` declares. Values marked `password` are held in
  memory for the run and passed to the commands as environment variables. They are never written to
  logs, run history or progress text, and are only stored if you explicitly ask for that.

`quickrun clean --all` removes every workspace and everything in it.

## The browser extension

The extension stores the pairing token, the port and two preferences, and sends them only to
`127.0.0.1`. It reads nothing from the pages you visit: it adds a button on `github.com` and takes
the repository and ref from the address bar. It requests no access to your other tabs.

## The website

These pages are served by GitHub Pages as static files. There are no cookies, no analytics and no
third-party scripts. GitHub logs requests to its servers under
[its own privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).

## Contact

Questions or corrections: [github.com/fgilde/QuickRun/issues](https://github.com/fgilde/QuickRun/issues)
