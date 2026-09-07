# Web-Komponenten

QuickRuns Elemente für jede Seite: ein Run-Button, das Badge, eine Statuszeile, ein Gate, ein
Download-Button und eine Config-Ansicht. Eine Datei, kein Build-Schritt, keine Abhängigkeit.

```html
<script type="module" src="https://quickrun.org/components.js"></script>

<quickrun-btn repo="acme/app">Starten</quickrun-btn>
```

Auf der [Einbinden-Seite](/de/embed) sind alle sechs live zu sehen, mit einem Baukasten für den
Button und dem Code zum Kopieren. Diese Seite hier ist die Referenz.

## Was ein Klick macht

1. Die Seite fragt `http://127.0.0.1:9876/api/ping`, ob QuickRun da ist. Dieser Endpunkt antwortet
   absichtlich jeder Seite; er nennt die Version und sonst nichts.
2. **Läuft, und diese Seite ist vertraut** — QuickRun öffnet sein eigenes Fenster mit dem Plan. Der
   Besucher bleibt auf deiner Seite. `*.quickrun.org` ist von Anfang an vertraut, und jeder kann in
   den Einstellungen von QuickRun seine eigene Seite eintragen.
3. **Läuft, andere Seite** — der Button folgt `quickrun://run?…` und landet nach der einen Rückfrage
   des Browsers („QuickRun öffnen?“) im selben Fenster.
4. **Nicht da** — der Button geht mit dem Repository auf [quickrun.org/run](/de/run), wo der Download
   für diesen Rechner steht. Der Klick geht unterwegs nicht verloren.

Zwischen 1 und 2 fragt womöglich der Browser selbst nach. Aktuelle Chrome-Versionen behandeln es
als Zugriff aufs lokale Netz, wenn eine öffentliche Seite `127.0.0.1` erreichen will, und fragen den
Leser beim ersten Mal um Erlaubnis. Wer sie verweigert — oder wessen Browser das für ihn tut —, gibt
der Seite keine Möglichkeit mehr zu erkennen, ob QuickRun da ist; der Klick nimmt dann Weg 4, und
die Run-Seite übergibt trotzdem per `quickrun://`. Diese Erlaubnis kostet also höchstens das
nahtlose Fenster, nie den Lauf. Gefragt wird ausschließlich `/api/ping`, und das antwortet mit einer
Version und sonst nichts.

In allen vier Fällen kommt auf der anderen Seite dasselbe an: ein Plan, der auf einen Menschen
wartet — die Befehle, der Ref, der aufgelöste Commit, die Herkunft der Config. **Es läuft nichts,
bevor es in QuickRuns eigenem Fenster bestätigt wurde** — und genau deshalb gibt es hier keine
Komponente, die einen Plan oder ein Log zeichnet. Sie wäre die überzeugende Fälschung, gegen die
dieses Fenster existiert.

## Attribute

| Attribut | Bedeutung |
|---|---|
| `repo` | `owner/name` oder eine `https://`-Adresse zum Repository. Pflicht. |
| `ref` | Branch oder Tag, wenn nicht der Standard gestartet werden soll. |
| `pr` | Nummer eines Pull Requests, geholt als `refs/pull/<n>/head`. |
| `run-cfg` | Welche Config gelten soll — siehe unten. |
| `icon` | `left` (Standard), `right` oder `none`. |
| `label` | Der Text. Ohne das Attribut zählt der Textinhalt des Elements. |
| `mode` | `window` (Standard) übergibt an QuickRun; `link` geht auf quickrun.org/run. |
| `port` | Nur für ein QuickRun, das nicht auf 9876 lauscht. |
| `unstyled` | Das mitgelieferte CSS weglassen und die Parts selbst stylen. |

`config` wird als Synonym zu `run-cfg` akzeptiert.

Ein `run-cfg`, das einen Pfad oder eine Adresse nennt, braucht QuickRun **0.9.12 oder neuer**. Ältere
Versionen ignorieren es und nehmen die Config des Repositories — und sagen das im Fenster, wo
derjenige, der es liest, entscheidet.

## Welche Config ein Button nennen darf

Drei Arten, und in allen drei liest QuickRun die Datei selbst:

```html
<quickrun-btn repo="acme/app" run-cfg="collection">Starten</quickrun-btn>
<quickrun-btn repo="acme/app" run-cfg="ci/demo.quickrun.yml">Demo starten</quickrun-btn>
<quickrun-btn repo="acme/app" run-cfg="https://acme.com/quickrun/demo.yml">Demo starten</quickrun-btn>
```

| Wert | Bedeutung |
|---|---|
| `collection` | Die Config, die [QuickRun selbst](/de/collection) für dieses Repository hält. |
| `pfad/datei.yml` | Eine Datei im gestarteten Repository, relativ zu seiner Wurzel. |
| `https://…/x.yml` | Eine irgendwo veröffentlichte Config. QuickRun holt sie ausschließlich über https. |

Was ein Button nie nennen kann, ist ein Befehl. Eine Config ist eine **Datei**, die QuickRun selbst
holt und im Bestätigungsfenster zeigt, bevor irgendetwas passiert — das Schlimmste, was eine Seite
also anrichten kann, ist, jemandem einen Plan vorzulegen, den dieser liest und ablehnt. Direkt
abgelehnt, noch vor jedem Abruf: `http://`, Zugangsdaten in der Adresse, eine Adresse auf dem
eigenen Rechner oder im privaten Netz, ein Pfad, der aus dem Repository hinausführt, und ein Pfad
auf der Platte des Lesers. Einzelheiten in [Sicherheit](/de/security).

## Aussehen

Neun Custom Properties decken den Normalfall ab:

```html
<quickrun-btn repo="acme/app"
  style="--quickrun-bg: #1f883d; --quickrun-radius: 999px; --quickrun-size: 16px">
  Starten
</quickrun-btn>
```

`--quickrun-bg`, `--quickrun-fg`, `--quickrun-border`, `--quickrun-radius`, `--quickrun-padding`,
`--quickrun-gap`, `--quickrun-size`, `--quickrun-weight`, `--quickrun-icon-size`.

Für alles andere ist das Innere über seine Namen erreichbar:

```css
quickrun-btn::part(button) { box-shadow: 0 2px 8px #0003; }
quickrun-btn::part(icon)   { opacity: .8; }
quickrun-btn::part(label)  { letter-spacing: .02em; }
```

Und `unstyled` wirft das mitgelieferte Stylesheet ganz weg — dieselben drei Parts, nur ohne jede
Meinung dazu, wie sie aussehen sollen:

```html
<quickrun-btn repo="acme/app" unstyled class="mein-button">Starten</quickrun-btn>
```

Sobald der Ping geantwortet hat, trägt das Element außerdem `data-running`. Damit kann eine Seite
jemandem, der QuickRun schon hat, etwas anderes sagen:

```css
quickrun-btn:not([data-running])::part(label)::after { content: ' (QuickRun holen)'; }
```

## Events

Alles blubbert nach oben, ein Listener am Container reicht also für eine ganze Liste von Buttons.

| Event | Wann | `detail` |
|---|---|---|
| `quickrun-status` | Der Ping hat geantwortet, einmal pro Port und Seite. | `{ running, version, busy }` |
| `quickrun-run` | Ein Klick, **bevor** irgendetwas übergeben wird. Abbrechbar. | `{ target }` |
| `quickrun-handover` | QuickRun hat es. | `{ target, how: 'window' \| 'scheme' }` |

Abbrechbar heißt: eine Seite kann einen eigenen Schritt vor die Übergabe setzen — eine Lizenz zum
Bestätigen, eine Warnung, ein eigener Dialog:

```js
document.addEventListener('quickrun-run', (event) => {
  if (!confirm('Das hier auf deinem Rechner starten?')) event.preventDefault();
});
```

Und `run()` ist eine Methode, ein eigenes Bedienelement der Seite kann also dasselbe tun wie der
Button:

```js
document.querySelector('quickrun-btn').run();
```

## Die anderen Elemente

Fünf weitere, für das, was um den Button herum auf einer Seite steht. Alle bauen auf demselben einen
Endpunkt auf, den jede Seite fragen darf — `/api/ping` —, und keines von ihnen kann etwas starten.

### `<quickrun-badge>`

Das README-Badge als Element. Das Bild ist dasselbe, das ein README trägt, beide sehen also
nebeneinander gleich aus.

```html
<quickrun-badge repo="owner/repo"></quickrun-badge>
<quickrun-badge repo="owner/repo" mode="run"></quickrun-badge>
```

Standard ist ein einfacher Link auf die [Run-Seite](/de/run) — das, was ein README nehmen muss.
`mode="run"` übergibt wie der Button, und der Link darunter bleibt, was er ist: Mittelklick,
kopierte Adresse und ein Leser ohne QuickRun landen weiterhin sinnvoll. Gelesen werden `repo`,
`ref`, `pr`, `run-cfg`, `port` und `src` (dein eigenes Badge-Bild); `--quickrun-badge-height` setzt
die Höhe.

### `<quickrun-status>`

Ob QuickRun auf diesem Rechner läuft, als Textzeile.

```html
<quickrun-status running="QuickRun ist bereit" missing="QuickRun läuft hier nicht"></quickrun-status>
```

Verrät genau das, was `/api/ping` verrät: ob etwas antwortet und welche Version. Nie ein Repository,
einen Pfad oder einen Lauf.

### `<quickrun-gate>`

Zwei Slots, und der Ping entscheidet, welchen der Leser sieht — damit lässt sich der Rest der Seite
für einen Fall schreiben statt für beide gleichzeitig.

```html
<quickrun-gate>
  <p slot="asking">Suche QuickRun…</p>
  <p slot="running">Auf Start drücken, und es läuft hier.</p>
  <p slot="missing">Erst QuickRun holen — ein Download.</p>
</quickrun-gate>
```

Der `asking`-Slot ist optional und erscheint nur, solange die Antwort aussteht; ohne ihn ist das
Element diese 200 ms einfach leer — besser, als jemandem „QuickRun holen“ hinzuwerfen, der es schon
hat. Der Zustand steht als `state="asking|running|missing"` am Element, CSS kann also mehr tun als
die zwei Blöcke zu tauschen.

### `<quickrun-get>`

Der Download, benannt nach dem Rechner, der die Seite liest.

```html
<quickrun-get></quickrun-get>
<quickrun-get only-when-missing>QuickRun holen</quickrun-get>
```

Immer [quickrun.org/download](/de/download) — die Seite, die weiß, welche Datei zu welchem Rechner
gehört —, nie direkt eine Release-Datei. Nimmt dieselben Custom Properties wie der Button. Mit
`only-when-missing` nimmt es sich selbst weg, sobald QuickRun antwortet; ohne trägt das Element
`data-running`, und die Seite entscheidet.

### `<quickrun-config>`

Was laufen würde, im Klartext.

```html
<quickrun-config repo="owner/repo"></quickrun-config>
<quickrun-config repo="owner/repo" run-cfg="collection"></quickrun-config>
```

Beide Orte, aus denen eine Config kommen kann, sind öffentlich und von jeder Seite lesbar — die
eigene `quickrun.yml` des Repositories auf raw.githubusercontent.com und die, die QuickRun in seiner
[Sammlung](/de/collection) hält. Eine Seite, die anbietet etwas zu starten, kann also zeigen, was
das bedeutet, statt um Vertrauen zu bitten. Ohne `run-cfg` fragt es alle drei in QuickRuns eigener
Reihenfolge und zeigt die erste, die existiert; mit `run-cfg` nur diese Quelle. Es sagt, welche es
gefunden hat — und wenn es keine gibt, sagt es auch das: QuickRun würde dann selbst die Dateien
lesen und im Fenster sagen, dass es geraten hat.

Die Datei gehört jemand anderem, sie wird deshalb als **Text** in die Seite geschrieben und nie als
Markup. `--quickrun-config-height`, `--quickrun-config-size` und `--quickrun-config-bg` bestimmen
die Größe, `::part(from)` und `::part(config)` sind die zwei Hälften. Es feuert `quickrun-config`
mit `{ repo, from, url, text }`, sobald es eine hat.

## Die Marke

Standard ist das Logo, eingebettet in `components.js` als Daten statt nachgeladen: eine Seite mit
strengem `img-src` würde sonst ein kaputtes Bild zeigen, eine offline gelesene Seite gar nichts.

```html
<quickrun-btn repo="owner/repo" glyph="play">Starten</quickrun-btn>
```

`glyph="play"` tauscht es gegen das flache Dreieck des Badges, das seine Farbe vom Text nimmt — für
ein Design, in dem ein farbiges Logo nicht sitzt.

## Button, Badge oder Link

Drei Wege zu einem Lauf, für drei verschiedene Orte:

| Wo | Was | Warum |
|---|---|---|
| README auf GitHub | das [Badge](/de/badge), verlinkt auf `quickrun.org/run?repo=…` | GitHub führt keine Skripte aus und entfernt unbekannte Link-Schemata. Ein Bild in einem Link ist alles, was bleibt. |
| Eigene Seite | `<quickrun-btn>` | Ein Klick öffnet direkt QuickRuns Fenster, und die Seite kann auf die Events reagieren. |
| Link im Text, Chat-Nachricht | `https://quickrun.org/run?repo=…` | Funktioniert überall, wo ein Link funktioniert. |

Das Badge kann auch von selbst weitergehen — die Variante „direkt starten“: die Run-Seite übergibt
schon beim Laden, statt auf einen zweiten Klick zu warten.

```markdown
[![QuickRun](https://quickrun.org/badge.svg)](https://quickrun.org/de/run?repo=owner/repo&executeQuickRun=true)
```

Für ein README bleibt der einfache Link die Empfehlung. Wer in einem fremden Projekt auf ein Badge
klickt, hat noch keine Übergabe verlangt, und die Seite dazwischen ist der Ort, an dem er überhaupt
erfährt, was QuickRun ist. Auf der eigenen Seite, wo der Besucher wegen genau dieses Projekts ist,
ist der Button die bessere Antwort.

Ein `run-cfg` funktioniert in allen drei Fällen: die Run-Seite gibt es an QuickRun weiter, und das
Fenster sagt, dass die Config nicht aus dem Repository selbst kommt.

## Wo das läuft

In jedem aktuellen Browser: Custom Elements, Shadow DOM und `::part()` sind die Plattform, keine
Bibliothek. Auf einer Seite ohne JavaScript erscheint der Button nicht — genau wie jeder andere
Button auch — und das ist der Fall, den das [Badge](/de/badge) abdeckt.

Bis das Skript da ist — oder falls es nie ankommt — ist das Element ein unbekanntes Tag mit deinem
Text darin, und `:not(:defined)` entscheidet, wie das aussieht:

```css
quickrun-btn:not(:defined) { visibility: hidden; }
```

Ganz ohne Skripte wird nichts hochgerüstet und nichts gestartet — dann gehört der Link daneben:

```html
<quickrun-btn repo="acme/app">Starten</quickrun-btn>
<noscript><a href="https://quickrun.org/de/run?repo=acme/app">In QuickRun starten</a></noscript>
```
