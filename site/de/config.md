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

## `requires`

Eine Voraussetzung wird geprüft, bevor irgendetwas läuft. Was fehlt und installiert werden kann,
**wird** installiert — in QuickRuns eigenen Ordner, nie ins System:

| Tool | Woher |
|---|---|
| `dotnet` | Microsofts `dotnet-install`-Script, im Channel, den deine `version` bedeutet (`>=10` heißt 10.0) |
| `node` | der neueste Build auf nodejs.org, den deine `version` akzeptiert |
| `pnpm`, `yarn` | npm — das mit dem Node oben mitkommt, wenn die Maschine keinen hat |
| `pwsh` | das PowerShell-Paket auf NuGet, installiert als .NET-Tool |

Ein Tool, das ein anderes braucht, bringt es mit: `pnpm` installiert auf einer Maschine ohne Node
erst Node, `pwsh` installiert eine .NET-Runtime zum Laufen. Beides landet mit allem anderen unter
`~/.quickrun/tools`.

Alles andere wird geprüft und gemeldet, mit deiner `install`-Zeile, falls du eine angegeben hast.
Das Bestätigungsfenster listet vor der Zustimmung auf, was installiert würde; die CLI schreibt es
hin, bevor sie fragt.

Drei Dinge passieren nicht: eine Version anfassen, die die Maschine schon hat (eine erfüllte
Voraussetzung installiert nichts), für `optional: true` installieren, oder in deinen PATH schreiben
— die geladene Toolchain steht nur im PATH der Prozesse dieses Laufs. Alles liegt unter
`~/.quickrun/tools`; diesen Ordner zu löschen macht es vollständig rückgängig.

## `readyWhen`

Genau eine dieser Formen, oder den Block weglassen für „fertig, sobald der Prozess gestartet ist":

| Form | Wartet auf |
|---|---|
| `{port: 5432}` | erfolgreichen TCP-Connect auf `127.0.0.1:5432` |
| `{http: "http://localhost:5000/health"}` | HTTP-Antwort unter 500 — ein Dev-Server mit 404 läuft |
| `{log: 'Now listening on: (?<url>\S+)'}` | Auftreten des Musters in der Ausgabe des Tasks |
| `{delay: 5s}` | feste Wartezeit — `500ms`, `5s`, `2m`, oder eine Zahl in Sekunden |
| `{window: true}` | ein Fenster des Task-Prozesses — für Desktop-Anwendungen |

Eine Desktop-Anwendung hat keinen Port und keine URL, deshalb wartet `{window: true}` auf das,
worauf ein Mensch wirklich wartet: ihr Fenster. QuickRun beobachtet den Prozess und alles, was er
startet — `dotnet run` startet die Anwendung als Kindprozess — und zählt den Task als bereit, sobald
einer davon ein Fenster hat. Danach holt QuickRun dieses Fenster in den Vordergrund, und Log und
Status zeigen die Prozess-ID. Die Erkennung setzt das bei Desktop-Projekten von selbst (WPF,
WinForms, Avalonia, `WinExe`), die meisten Repositories brauchen dafür also gar keine Config.
Bisher nur unter Windows; anderswo zählt der Task als gestartet.

Readiness beschreibt den *Dienst*, nicht den Prozess. `docker compose up -d db` beendet sich lange
bevor der Port offen ist, deshalb läuft der Readiness-Watcher weiter, wenn ein Task sauber beendet.

Ein Task, der mit einem Exit-Code ungleich 0 endet, lässt den Lauf fehlschlagen — und der Lauf sagt,
welcher Task mit welchem Code. Eine Anwendung, die nicht starten konnte, weil der Port belegt war
oder ein Connection String fehlte, ist kein fertiger Lauf, so weit das Log vorher auch kam.

Bevor ein Task startet, wird die Adresse aus seinem `readyWhen` einmal geprüft. Antwortet dort schon
etwas, sagt das Log es: Readiness kann zwei Server nicht unterscheiden, sie wäre also bei einem
Fremden zugeschlagen, während der Task selbst am Binden scheitert. Der übliche Grund ist ein
früherer Lauf desselben Repositories, der noch läuft.

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

Verfügbar in `run`, `cwd`, `env`-Werten, `open` und `readyWhen` — ein Task, der auf
`${inputs.port}` startet, kann also auch auf diesen Port warten. Ein Verweis auf eine nicht existierende Eingabe
ist ein Validierungsfehler, kein leerer String. Secret-Eingaben werden ersetzt, aber nie in Logs,
Lauf-Historie oder Fortschrittstexte geschrieben.

## Die Umgebung eines Befehls

Von allgemein nach spezifisch, das Spätere gewinnt:

1. was QuickRun setzt — derzeit `MSBUILDDISABLENODEREUSE=1`, weil MSBuilds wiederverwendbare
   Worker-Knoten den Build überleben, der sie gestartet hat, und dessen Ausgabe-Pipe offen halten.
   Ein fertiges `dotnet restore` sah dadurch aus wie ein Lauf, der an diesem Schritt hängt. Eine
   Config, die sie zurückhaben will, setzt die Variable einfach selbst
2. der `env`-Block der Config
3. der Wert jedes Inputs, der ein `env` nennt
4. das `env` des Tasks

Alle vier werden interpoliert, `ADDRESS: http://localhost:${inputs.port}` ist also normal.

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
