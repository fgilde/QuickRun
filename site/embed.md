# Embed a button

A Run button on your own page, as a real HTML element:

```html
<script type="module" src="https://quickrun.org/components.js"></script>

<quickrun-btn repo="acme/app">Run this</quickrun-btn>
```

<quickrun-btn repo="fgilde/QuickRun">Run this</quickrun-btn>

That is the whole integration. No build step, no bundler, no dependency, and nothing to keep up to
date: `components.js` is served from quickrun.org, so a page embedded once behaves the way QuickRun
behaves today rather than the way it behaved when the line was pasted.

For a README, where scripts do not run, use the [badge](/badge) instead — same destination, one
click more.

## Build it here

Everything below is the real component, not a picture of one. Press it and QuickRun's window opens
if QuickRun is on this machine.

<EmbedPlayground />

## What a click does

1. The page asks `http://127.0.0.1:9876/api/ping` whether QuickRun is there. That endpoint answers
   any page on purpose; it says the version and nothing else.
2. **Running, and this site is trusted** — QuickRun opens its own window on the plan. The reader
   stays on your page. `*.quickrun.org` is trusted out of the box, and anyone can add their own site
   in QuickRun's settings.
3. **Running, any other site** — the button follows `quickrun://run?…`, which lands in the same
   window after the browser's one "open QuickRun?" prompt.
4. **Not there** — the button goes to [quickrun.org/run](/run) with the repository, which offers the
   download for that machine. The press is not lost on the way.

Between 1 and 2 the browser may ask something of its own. Current Chrome treats a public page
reaching `127.0.0.1` as local network access and asks the reader for permission the first time; a
reader who declines - or a browser that declines for them - leaves the page unable to tell whether
QuickRun is there, and the click takes route 4, where the run page hands over through `quickrun://`
anyway. So the worst that permission costs is the seamless window, never the run. All the page ever
asks for is `/api/ping`, which answers with a version and nothing else.

In every one of those, what arrives on the other side is a plan waiting for a person: the commands,
the ref, the resolved commit, the config it came from. **Nothing runs until it is confirmed in
QuickRun's own window** — and that is why there is no component here that draws a plan or a log. One
that did would be the convincing fake the window exists to prevent.

## Attributes

| Attribute | Means |
|---|---|
| `repo` | `owner/name`, or an `https://` URL to the repository. Required. |
| `ref` | A branch or tag, when the default is not what people should run. |
| `pr` | A pull request number, fetched as `refs/pull/<n>/head`. |
| `run-cfg` | Which config to use — see below. |
| `icon` | `left` (default), `right`, or `none`. |
| `label` | The text. Without it, the element's own text content is used. |
| `mode` | `window` (default) hands over to QuickRun; `link` goes to quickrun.org/run instead. |
| `port` | Only for a QuickRun told to listen somewhere other than 9876. |
| `unstyled` | Drop the bundled CSS and style the parts yourself. |

`config` is accepted as a synonym of `run-cfg`.

A `run-cfg` naming a path or an address needs QuickRun **0.9.12 or newer**. An older one ignores it
and prepares the repository's own config instead - and says so in the window, which is where the
person reading it decides.

## Which config a button may name

Three kinds, and QuickRun reads the file itself in all three:

```html
<quickrun-btn repo="acme/app" run-cfg="collection">Run this</quickrun-btn>
<quickrun-btn repo="acme/app" run-cfg="ci/demo.quickrun.yml">Run the demo</quickrun-btn>
<quickrun-btn repo="acme/app" run-cfg="https://acme.com/quickrun/demo.yml">Run the demo</quickrun-btn>
```

| Value | Means |
|---|---|
| `collection` | The config [QuickRun keeps](/collection) for that repository. |
| `some/path.yml` | A file in the repository being run, relative to its root. |
| `https://…/x.yml` | A config published at an address. QuickRun fetches it over https only. |

What a button can never name is a command. A config is a **file**, fetched by QuickRun and shown in
the confirmation window before anything happens — so the worst a page can do is put a plan in front
of somebody, which is a plan they read and refuse. Refused outright, before any fetch: `http://`,
credentials in the address, an address on the reader's own machine or private network, a path
leaving the repository, and a path on the reader's disk. Details in [Security](/security).

## Styling

Nine custom properties cover the usual cases:

```html
<quickrun-btn repo="acme/app"
  style="--quickrun-bg: #1f883d; --quickrun-radius: 999px; --quickrun-size: 16px">
  Run this
</quickrun-btn>
```

`--quickrun-bg`, `--quickrun-fg`, `--quickrun-border`, `--quickrun-radius`, `--quickrun-padding`,
`--quickrun-gap`, `--quickrun-size`, `--quickrun-weight`, `--quickrun-icon-size`.

For anything else, the inside is reachable by name:

```css
quickrun-btn::part(button) { box-shadow: 0 2px 8px #0003; }
quickrun-btn::part(icon)   { opacity: .8; }
quickrun-btn::part(label)  { letter-spacing: .02em; }
```

And `unstyled` removes the bundled stylesheet entirely, leaving the same three parts with no opinion
about how they look:

```html
<quickrun-btn repo="acme/app" unstyled class="my-button">Run this</quickrun-btn>
```

The element also carries `data-running` once the ping has answered, so a page can say something
different for a reader who has QuickRun:

```css
quickrun-btn:not([data-running])::part(label)::after { content: ' (get QuickRun)'; }
```

## Events

Everything bubbles, so one listener on a container covers a list of buttons.

| Event | When | `detail` |
|---|---|---|
| `quickrun-status` | The ping answered, once per port per page. | `{ running, version, busy }` |
| `quickrun-run` | A press, **before** anything is handed over. Cancelable. | `{ target }` |
| `quickrun-handover` | QuickRun has it. | `{ target, how: 'window' \| 'scheme' }` |

Cancelable means a page can put its own step in front of the hand-over — a licence to accept, a
warning, a dialog of its own:

```js
document.addEventListener('quickrun-run', (event) => {
  if (!confirm('Run this on your machine?')) event.preventDefault();
});
```

And `run()` is a method, so a page's own control can do what the button does:

```js
document.querySelector('quickrun-btn').run();
```

## The status line

The same ping as a sentence, for a page that wants to say what a press will do before it happens:

```html
<quickrun-status running="QuickRun is ready" missing="QuickRun is not running here"></quickrun-status>
```

<quickrun-status />

It has the same `port` attribute and reveals exactly what `/api/ping` reveals: whether something
answers, and its version. Never a repository, a path, or a run.

## Button, badge, or link

Three ways to send somebody to a run, for three different places:

| Where | What | Why |
|---|---|---|
| A README on GitHub | the [badge](/badge) linking to `quickrun.org/run?repo=…` | GitHub runs no scripts and strips unknown link schemes. An image inside a link is all there is. |
| Your own page | `<quickrun-btn>` | A click opens QuickRun's window directly, and the page can react to the events. |
| A link in text, a chat message | `https://quickrun.org/run?repo=…` | Works anywhere a link works. |

The badge can also carry on by itself, which is the "direct run" variant — the run page hands over as
soon as it loads instead of waiting for a second click:

```markdown
[![QuickRun](https://quickrun.org/badge.svg)](https://quickrun.org/run?repo=owner/repo&executeQuickRun=true)
```

The plain link stays the recommendation for a README. Somebody clicking a badge in a stranger's
project has not asked for a hand-over yet, and the page in the middle is where they find out what
QuickRun even is. On your own page, where the visitor came for your project, the button is the
better answer.

A `run-cfg` works in all three: the run page carries it on to QuickRun, and the window says the
config came from somewhere other than the repository.

## Where it works

Every current browser: custom elements, shadow DOM and `::part()` are the platform, not a library.
Nothing renders differently on a page without JavaScript than any other button would — it is simply
not there, which is the case the [badge](/badge) covers.

Until the script arrives - or if it never does - the element is an unknown tag with your text
inside it, and `:not(:defined)` is how you decide what that looks like:

```css
quickrun-btn:not(:defined) { visibility: hidden; }
```

With scripting off entirely, nothing upgrades and nothing runs, so give it the link:

```html
<quickrun-btn repo="acme/app">Run this</quickrun-btn>
<noscript><a href="https://quickrun.org/run?repo=acme/app">Run this in QuickRun</a></noscript>
```
