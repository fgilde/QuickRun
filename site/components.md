# Web components

QuickRun's elements for any page: a Run button, a badge, a status line, a gate, a download button
and a config viewer. One file, no build step, no dependency.

```html
<script type="module" src="https://quickrun.org/components.js"></script>

<quickrun-btn repo="acme/app">Run this</quickrun-btn>
```

The [embed page](/embed) has all six of them live, with a builder for the button and the code to
copy. This page is the reference.

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

## The other elements

Five more, for the parts of a page around the button. Every one of them is built on the same one
endpoint any page may ask, `/api/ping`, and none of them can start anything.

### `<quickrun-badge>`

The README badge, as an element. The image is the same one a README carries, so the two look
identical side by side.

```html
<quickrun-badge repo="owner/repo"></quickrun-badge>
<quickrun-badge repo="owner/repo" mode="run"></quickrun-badge>
```

The default is a plain link to the [run page](/run) — what a README has to use. `mode="run"` hands
over the way the button does, and the link underneath stays what it is, so a middle click, a copied
address and a reader without QuickRun all still land somewhere sensible. `repo`, `ref`, `pr`,
`run-cfg`, `port` and `src` (your own badge image) are read, and `--quickrun-badge-height` sizes it.

### `<quickrun-status>`

Whether QuickRun is on this machine, as a line of text.

```html
<quickrun-status running="QuickRun is ready" missing="QuickRun is not running here"></quickrun-status>
```

It reveals exactly what `/api/ping` reveals: whether something answers, and its version. Never a
repository, a path, or a run.

### `<quickrun-gate>`

Two slots, and the ping decides which one a reader sees — so the rest of the page can be written for
one case instead of hedged for both.

```html
<quickrun-gate>
  <p slot="asking">Looking for QuickRun…</p>
  <p slot="running">Press Run and it starts here.</p>
  <p slot="missing">Get QuickRun first — it is one download.</p>
</quickrun-gate>
```

The `asking` slot is optional and shows only while the answer is out; without it the element is
simply empty for those 200ms, which beats flashing "get QuickRun" at somebody who has it. The state
is on the host as `state="asking|running|missing"`, so CSS can do more than swap the two blocks.

### `<quickrun-get>`

The download, named for the machine reading the page.

```html
<quickrun-get></quickrun-get>
<quickrun-get only-when-missing>Get QuickRun</quickrun-get>
```

Always [quickrun.org/download](/download), which is the page that knows which file is right for
which machine — never a release asset directly. It takes the same custom properties as the button.
With `only-when-missing` it removes itself once QuickRun answers; without it, the host carries
`data-running` and the page decides.

### `<quickrun-config>`

What would run, in plain text.

```html
<quickrun-config repo="owner/repo"></quickrun-config>
<quickrun-config repo="owner/repo" run-cfg="collection"></quickrun-config>
```

Both places a config can come from are public and readable from any page — the repository's own
`quickrun.yml` on raw.githubusercontent.com, and the one QuickRun keeps in its
[collection](/collection) — so a page offering to run something can show what that means instead of
asking for trust. Without `run-cfg` it asks all three in QuickRun's own order and shows the first
one that exists; with one, only that source. It says which one it found, and when there is none it
says that too: QuickRun would then read the files itself and say in its window that it guessed.

The file is somebody else's, so it is written into the page as **text** and never as markup.
`--quickrun-config-height`, `--quickrun-config-size` and `--quickrun-config-bg` size it, and
`::part(from)` and `::part(config)` are the two halves. It fires `quickrun-config` with
`{ repo, from, url, text }` when it has one.

## The mark

The logo is the default, embedded in `components.js` as data rather than fetched: a page with a
strict `img-src` would otherwise show a broken image, and a page read offline would show nothing.

```html
<quickrun-btn repo="owner/repo" glyph="play">Run this</quickrun-btn>
```

`glyph="play"` swaps it for the flat triangle the badge carries, which takes its colour from the
text — for a design a full-colour logo does not sit well in.

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
