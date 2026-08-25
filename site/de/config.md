# Config-Referenz

`quickrun.yml` im Repository-Root. Jeder Block ist optional; die einzige Anforderung ist, dass die
Datei *irgendetwas* Ausführbares beschreibt.

Mit dieser Kommentarzeile vervollständigt dein Editor die Felder:

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
```

## Kurzformen

Der Parser expandiert Kurzformen, bevor irgendetwas läuft — die Engine sieht immer nur eine Form.

| Geschrieben | Bedeutet |
|---|---|
| `run: ./run.sh` | ein Task namens `run` |
| `run: {linux: ./run.sh, windows: ./run.ps1}` | ein Task, Befehl pro Plattform gewählt |
| `setup: [npm ci, dotnet restore]` | zwei sequenzielle Schritte |
| `tasks: [npm start, python api.py]` | zwei Tasks, `task-1` und `task-2` |

Ein `run:`-Wert, der ein Mapping ist, gilt als **Platform-Map**, wenn alle Schlüssel `windows`,
`linux` oder `macos` sind. Alles andere ist ein Fehler und keine Vermutung.

Liegt `run.sh`, `run.ps1`, `quickrun.sh` oder `quickrun.ps1` im Repository-Root und gibt es keine
`quickrun.yml`, schlägt QuickRun dieses Skript direkt vor.

## Vollständige Form

```yaml
version: 1                     # optional, Standard 1
name: My App                   # optional, Standard ist der Repository-Name
description: ...               # optional
icon: assets/logo.png          # optional, Pfad im Repository oder URL
docs: https://...              # optional, wird in der UI verlinkt

requires:                      # optionale Voraussetzungsprüfungen
  - tool: dotnet               # beliebiger Befehl; bekannte Tools werden gezielter abgefragt
    version: ">=9.0"           # optional
    install: https://dot.net   # optional, wird gezeigt wenn das Tool fehlt
    optional: false            # Standard false; true warnt statt zu blockieren

inputs:                        # optional, steuert das generierte Formular
  - id: apiKey                 # erforderlich, Buchstaben, Ziffern, Unterstrich
    label: OpenAI API Key      # Standard ist die id
    type: password             # text|password|number|bool|select|path|dir|file
    description: ...
    default: null
    required: true             # Standard false
    pattern: "^sk-"            # nur bei textartigen Typen
    min: 1                     # nur number
    max: 65535                 # nur number
    options: [dev, prod]       # nur select; auch [{value, label}]
    env: OPENAI_API_KEY        # wird an jeden Befehl exportiert
    persist: false             # Standard false

env:                           # optional, für jeden Befehl
  ASPNETCORE_ENVIRONMENT: Development

setup:                         # optional, sequenziell, vor den Tasks
  - run: npm ci
    cwd: web
    when: [linux, macos]       # optionaler Plattformfilter; ein Skalar geht auch
    continueOnError: false

tasks:                         # optional, parallel gestartet, sofern dependsOn nichts anderes sagt
  - name: db
    run: docker compose up -d db
    readyWhen: {port: 5432}
  - name: api
    run: dotnet run --project src/Api
    dependsOn: [db]
    env: {PORT: "5000"}
    readyWhen: {port: 5000}
    restart: onFailure         # never|onFailure, Standard never
  - name: web
    run: npm run dev
    cwd: web
    readyWhen: {http: "http://localhost:5173"}
    open: true                 # true öffnet die readyWhen-URL; oder explizite URL angeben

stop:                          # optional, läuft beim Stoppen
  - docker compose down
```

## `readyWhen`

Genau eine dieser Formen, oder den Block weglassen für „fertig, sobald der Prozess gestartet ist":

| Form | Wartet auf |
|---|---|
| `{port: 5432}` | erfolgreichen TCP-Connect auf `127.0.0.1:5432` |
| `{http: "http://localhost:5000/health"}` | HTTP-Antwort unter 500 — ein Dev-Server mit 404 läuft |
| `{log: 'Now listening on: (?<url>\S+)'}` | Auftreten des Musters in der Ausgabe des Tasks |
| `{delay: 5s}` | feste Wartezeit — `500ms`, `5s`, `2m`, oder eine Zahl in Sekunden |

Readiness beschreibt den *Dienst*, nicht den Prozess. `docker compose up -d db` beendet sich lange
bevor der Port offen ist, deshalb läuft der Readiness-Watcher weiter, wenn ein Task sauber beendet.

Eine Readiness-Prüfung, die nie zutrifft, ist kein Fehler. QuickRun wartet drei Minuten, sagt es
dann im Log und zählt den Task als gestartet — der Prozess läuft weiter, denn „hat auf dem Port
nicht geantwortet" und „ist kaputt" sind nicht dasselbe. Der Fortschrittsbalken bewegt sich beim
*Start* eines Tasks und nicht erst, wenn er bereit ist; eine langsame Anwendung sieht damit nicht
mehr hängengeblieben aus.

`open: true` braucht eine Adresse. Bei `port` oder `http` ist es die deklarierte. Bei `log` gibt es
keine, deshalb nimmt QuickRun die letzte Loopback-Adresse, die der Task ausgegeben hat — also die
Zeile, auf die das Muster gewartet hat. Es zählen nur `localhost`, `127.0.0.1`, `0.0.0.0` und
`[::1]`: ein Build-Log ist voll von Links auf Dokumentation und Advisories, und keiner davon ist der
Ort, an dem die Anwendung läuft.

::: warning
Für ein `log`-Muster einfache Anführungszeichen verwenden. In YAML-Doppelquotes ist `\S` ein
ungültiges Escape und die Datei parst nicht.
:::

## `dependsOn`

Ein Task wartet, bis jeder genannte Task ready ist. Zyklen werden bei der Validierung abgewiesen,
nicht erst zur Laufzeit. Eine Abhängigkeit ohne `readyWhen` erzeugt eine Warnung: ihre Abhängigen
starten dann, sobald sie anläuft — was selten gemeint ist.

## Interpolation

| Platzhalter | Wird ersetzt durch |
|---|---|
| `${inputs.apiKey}` | den Wert dieser Eingabe |
| `${env.HOME}` | eine Umgebungsvariable, leer wenn nicht gesetzt |
| `${workspace}` | den absoluten Pfad des Checkouts |
| `${repo.name}` | den Repository-Namen |
| `${repo.ref}` | Branch, Tag oder Commit, der läuft |

Verfügbar in `run`, `cwd`, `env`-Werten und `open`. Ein Verweis auf eine nicht existierende Eingabe
ist ein Validierungsfehler, kein leerer String. Secret-Eingaben werden ersetzt, aber nie in Logs,
Lauf-Historie oder Fortschrittstexte geschrieben.

## Shell

Befehle laufen über die Plattform-Shell: `cmd /c` unter Windows, sonst `/bin/sh -c`. Ein Befehl, der
unter Windows mit einem `.sh`-Skript beginnt, wird über die `bash.exe` von Git für Windows erneut
versucht, sofern vorhanden — damit verhält sich ein Repository, das nur `run.sh` mitbringt, dort
sinnvoll.

## Config prüfen

```bash
quickrun validate            # im Repository
quickrun validate ./my-repo
```

Die Validierung trennt Fehler und Warnungen; Warnungen führen nicht zum Fehlschlag. Repository-Owner
können das in einen Pre-Commit-Hook hängen.
