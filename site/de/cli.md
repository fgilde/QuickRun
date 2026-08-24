# CLI

QuickRun ist ohne Browser-Erweiterung vollständig benutzbar.

## `quickrun` ohne Argumente

Startet den Listener, legt ein Icon ins Tray und öffnet das Dashboard. Das macht ein Doppelklick,
und es ist der Einstiegspunkt für alles andere:

- **Runs** — was läuft, mit Fortschritt und Log-Ausgabe in Echtzeit
- **Workspaces** — was ausgecheckt ist, wieviel Platz es braucht, und wie man es entfernt
- **Browser extension** — der Pairing-Button und wie die Erweiterung funktioniert
- **About** — Version, Installationsquelle, Update-Prüfung

```bash
quickrun                 # Tray-Icon + Desktop-Fenster
quickrun --browser       # Dashboard im Browser statt im Fenster öffnen
quickrun --no-window     # nur Tray-Icon; das Icon öffnet ein Fenster, wenn du eins willst
quickrun --no-tray       # kein Tray-Icon, damit ist der Browser die UI
quickrun daemon          # nur Listener, kein Tray und kein Fenster
```

Das Fenster wird nativ gezeichnet, nicht in einem eingebetteten Browser. Damit muss keine
Browser-Engine mitgeliefert werden, um ein paar Listen darzustellen — und der Browser fragt nicht,
ob er die lokale Werkzeugseite übersetzen soll.

Die Binary ist fürs GUI-Subsystem gebaut, ein Doppelklick öffnet also kein Konsolenfenster. Aus
einem Terminal gestartet hängt sie sich an dieses Terminal — nur so bleiben die Befehle unten
benutzbar. Die eine sichtbare Folge: die Shell druckt ihren nächsten Prompt, bevor die Ausgabe
kommt, ein Prompt kann also darüber stehen.

## `quickrun run`

```bash
quickrun run acme/app
quickrun run acme/app --ref feature/login
quickrun run https://github.com/acme/app --pr 42
quickrun run acme/app --input apiKey=sk-1 --input port=3000
```

| Option | Bedeutung |
|---|---|
| `-r, --ref` | Branch, Tag oder Commit; Standard ist der Default-Branch des Repositories |
| `-p, --pr` | Pull-Request-Nummer, als `refs/pull/<n>/head` geholt — funktioniert auch bei Forks |
| `-d, --subdir` | ein Unterverzeichnis als Projekt-Root behandeln |
| `-i, --input` | eine deklarierte Eingabe füllen, wiederholbar |
| `-t, --token` | Zugriffstoken für ein privates Repository |
| `-c, --config` | andere Config-Datei verwenden, relativ zum Projekt-Root |
| `--fresh` | Workspace löschen und neu klonen |
| `-y, --yes` | Bestätigung überspringen; fehlende erforderliche Eingaben schlagen dann fehl statt zu fragen |
| `--no-open` | keine Browser-URL öffnen, die die Config anfordert |

Ohne `--yes` wird nichts ausgeführt, bevor der Plan gedruckt und bestätigt wurde.

## `quickrun validate`

```bash
quickrun validate
quickrun validate ./my-repo
```

Exit-Codes: `0` gültig, `1` ungültig, `2` keine Config gefunden.

## `quickrun detect`

```bash
quickrun detect
quickrun detect . --save
```

Zeigt, wie QuickRun ein Repository ohne Config starten würde. `--save` schreibt den
höchstbewerteten Kandidaten in `quickrun.yml` und überschreibt eine vorhandene Datei nicht.

## `quickrun ls` und `quickrun clean`

```bash
quickrun ls
quickrun clean --all
quickrun clean --older-than 30d
quickrun clean acme__app__main-1a2b3c
```

`clean` verlangt genau einen Selektor. Standardmäßig alles zu löschen wäre die schlechtestmögliche
Vermutung — deshalb ist „kein Selektor" ein Benutzungsfehler.

## `quickrun daemon` und `quickrun pair`

```bash
quickrun daemon              # lauscht auf 127.0.0.1:9876
quickrun daemon --pair       # und öffnet beim Start ein Pairing-Fenster
quickrun pair                # Pairing-Fenster für einen laufenden Daemon öffnen
quickrun pair --revoke       # Token ungültig machen
```

## `quickrun update`

```bash
quickrun update
quickrun update --check
```

## Workspaces

```
Windows  %LOCALAPPDATA%\QuickRun\runs\
Linux    ~/.local/share/QuickRun/runs/
macOS    ~/Library/Application Support/QuickRun/runs/
```

Bewusst nicht `%TEMP%`: dort löschen System-Cleaner hinein, und ein halb entferntes `node_modules`
mitten im Lauf ist eine Bug-Fabrik. `QUICKRUN_HOME` überschreibt das Wurzelverzeichnis.

Ein zweiter Lauf desselben Repositories und Refs verwendet den Workspace weiter — `git fetch` plus
`git reset --hard`, wobei `node_modules`, `.venv`, `obj`, `bin`, `target` und Verwandte bleiben — der
zweite Start dauert damit Sekunden. `--fresh` ist der Notausgang, wenn ein Workspace kaputt ist.

## Authentifizierung

Für ein privates Repository, erster Treffer gewinnt:

1. `--token`
2. `QUICKRUN_TOKEN`
3. `gh auth token`, wenn du in der GitHub CLI angemeldet bist
4. einfaches `git clone`, das SSH-Keys und den Git Credential Manager mitnimmt

Tokens werden aus jeder Log-Zeile und jeder Fehlermeldung entfernt. Credential-Prompts sind
durchgehend abgeschaltet, inklusive des GUI-Dialogs, den der Git Credential Manager sonst öffnet —
für einen Hintergrund-Daemon wäre das ein unsichtbarer Deadlock.
