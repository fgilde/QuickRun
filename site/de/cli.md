# CLI

QuickRun ist ohne Browser-Erweiterung vollständig benutzbar.

## `quickrun` ohne Argumente

Startet den Listener, legt ein Icon ins Tray und öffnet das Dashboard. Das macht ein Doppelklick,
und es ist der Einstiegspunkt für alles andere:

- **Run a repository** — ein Repository ohne Browser-Erweiterung starten
- **Runs** — was läuft, mit Fortschritt und Log-Ausgabe in Echtzeit
- **Workspaces** — was ausgecheckt ist, wieviel Platz es braucht, und wie man es entfernt
- **Browser extension** — wie die Erweiterung funktioniert
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
| `-f, --file` | eine einzelne Config-Datei starten, egal wo sie liegt — siehe unten |
| `--path` | einen Ordner dieser Maschine ausführen, statt ein Repository auszuchecken |
| `--copy` | mit `--path`: eine Kopie unter `runs/` ausführen, das Original bleibt unberührt |
| `--fresh` | Workspace löschen und neu klonen |
| `-y, --yes` | Bestätigung überspringen; fehlende erforderliche Eingaben schlagen dann fehl statt zu fragen |
| `--no-open` | keine Browser-URL öffnen, die die Config anfordert |

Ohne `--yes` wird nichts ausgeführt, bevor der Plan gedruckt und bestätigt wurde.

## `quickrun run --file`

Eine einzelne Config-Datei, allein gestartet:

```bash
quickrun run --file ./demo.yml
quickrun run --file ~/configs/acme.yml
```

Was läuft, entscheidet die Datei:

- sie nennt ein `repository:` — dieses Repository wird ausgecheckt, und die Datei bestimmt den Ablauf
- sie nennt keines — der Ordner, in dem die Datei liegt, läuft an seinem Platz

Die zweite Regel gilt nur, weil du die Datei selbst benannt hast. Eine Config, die von woanders
kommt, darf das nicht entscheiden: `quickrun://runfile?path=…` und der Knopf **Datei starten** im
Fenster übergeben die Datei zwar, aber bei einer Datei ohne `repository:` aus einem Link wird
nachgefragt statt angenommen. Ein Repository auf der Kommandozeile schlägt das in der Datei.

Ein Lauf mit einer Config von außen bekommt einen eigenen Checkout und kann deshalb nicht mit der
`quickrun.yml` des Repositories kollidieren.

## `quickrun run` auf einem Ordner

```bash
quickrun run .                       # dieser Ordner, dort wo er liegt
quickrun run --path ~/dev/planner    # dasselbe, ausdrücklich
quickrun run --path . --copy         # eine Kopie unter runs/, das Original bleibt unberührt
```

Kein Checkout, kein Klon: QuickRun liest die `quickrun.yml` des Ordners und führt deren Befehle in
diesem Ordner aus. Eine Repository-Kurzform ist nie ein existierendes Verzeichnis, die Argumentform
kollidiert also nicht mit `quickrun run acme/app`. Kontextmenü-Einträge nutzen `--path`, wo nichts
geraten werden darf.

In der Workspace-Liste steht ein **Verweis** darauf, wo der Ordner liegt — keine Kopie. Die Größe
zeigt `in place`, und diesen Workspace zu entfernen entfernt den Verweis: `Remove`, `Remove all` und
`clean` kommen nicht aus `runs/` heraus und können damit niemals eine Arbeitskopie von dir löschen.

`--copy` ist für den Fall, dass der Run das Original nicht anfassen darf: der Ordner wird unter
`runs/` kopiert und alles passiert dort. Die Kopie lässt weg, was ein Build wieder herstellt —
`.git`, `node_modules`, `.venv`, `obj`, `bin`, `target`, `.next` und Verwandte — und sagt das vorher.
Eine Kopie ist ein Workspace, den QuickRun besitzt; sie zu entfernen entfernt die Kopie.

Ist der Ordner eine git-Arbeitskopie, werden Branch und Commit gemeldet, damit ein lokaler Lauf
hinterher wiedererkennbar ist. Sonst steht als Ref `local`.

Bewusst nur von hier: ein Ordner auf dieser Maschine ist von der Kommandozeile und aus einem
Kontextmenü startbar, nie über die Browser-Erweiterung. Die Erweiterung fragt nach Repositories, und
eine Anfrage mit einem Pfad oder einer `file:`-URL wird dort abgelehnt.

### Aus dem Fenster

Das Formular hat ein Feld, und das nimmt beides: `owner/repo`, eine Git-URL oder einen Ordner auf
dieser Maschine. Was dort steht, entscheidet, was sonst erscheint — eine Branch-Auswahl beim
Repository, der Kopie-Schalter beim Ordner — und eine Zeile darunter sagt, was gelesen wurde. Der
Durchsuchen-Knopf ist immer da: einer Seite kann in keinem Browser ein Pfad übergeben werden, also
öffnet das Fenster den System-Dialog für sie. Ohne Fenster — bei headless gestartetem QuickRun — wird
der Pfad getippt oder eingefügt.

Der Daemon entscheidet zusätzlich selbst: ein Pfad, den es wirklich gibt, ist ein Ordner — ganz
gleich, was das Formular beim Tippen geraten hat.

Das native Fenster — das, was ohne System-WebView erscheint — hat dasselbe eine Feld, denselben
Durchsuchen-Knopf und denselben Kopie-Schalter. Eines kann es besser als die Seite: es fragt das
Dateisystem, statt zu raten. Ein Pfad, den es nicht gibt, wird deshalb benannt, statt als Repository
ausgecheckt zu werden.

Ein Ordner ohne `quickrun.yml` ist keine Sackgasse: QuickRun liest das Projekt und schlägt Befehle
vor, genau wie bei einem Repository, und sagt dazu, dass es geraten hat.

## `quickrun open`

```bash
quickrun open .                      # diesen Ordner an QuickRun geben und den Plan zeigen
quickrun open ~/dev/planner --copy
```

Das, was der Kontextmenü-Eintrag aufruft. Er führt selbst nichts aus: er bittet das laufende QuickRun
— und startet vorher eines, wenn keines läuft —, den Ordner vorzubereiten und den Plan in seinem
Fenster zu zeigen, wo die Entscheidung hingehört. Bekommt er eine `quickrun.yml`, nimmt er den Ordner
darum herum: eine Config ist nichts, was für sich läuft.

Dieser Umweg ist der Sinn der Sache. Ein Prozess, den die Shell startet, nähme den Run mit sich, wenn
sein Fenster zugeht — und die Binary ist fürs GUI-Subsystem gebaut, eine Bestätigungsabfrage in einer
Konsole, die niemand sieht, ist keine Bestätigung.

### „Run with QuickRun" im Dateimanager

QuickRun legt den Eintrag beim Start an und hält ihn auf die laufende Kopie gerichtet — ein Update
verschiebt die Binary, und ein Menüeintrag, der auf den alten Ort zeigt, ist schlimmer als keiner.
`quickrun uninstall` entfernt ihn. Auf einem Ordner, auf der leeren Fläche in einem Ordner und auf
einer `quickrun.yml`.

Nichts davon braucht Administratorrechte: unter Windows steht alles in `HKCU`, sonst im eigenen
Home-Verzeichnis. Den Autostart schaltet QuickRun als Einziges nie von selbst ein — das ist eine
Entscheidung, und sie steht in den Einstellungen.

**Windows** schreibt vier Schlüssel unter `HKCU\Software\Classes` — ohne Administratorrechte, und die
`.yml`-Einträge liegen unter `SystemFileAssociations`: das stellt den Eintrag neben das, was YAML
ohnehin öffnet, statt den Dateityp zu übernehmen. Unter **Windows 11** steht der Eintrag in *Weitere
Optionen anzeigen*; das kurze Menü nimmt nur einen paketierten Handler an, und das ist eigene Arbeit.

**Linux** bekommt eine Datei pro Dateimanager, denn einen gemeinsamen Standard gibt es nicht: ein
KIO-Servicemenü für Dolphin, eine Action-Datei für Nemo und ein Skript für Nautilus, das
Menü-Erweiterungen dieser Art abgeschafft hat und nur noch sein Scripts-Untermenü anbietet. Thunar
bleibt bewusst außen vor: seine Aktionen stehen in einer einzigen `uca.xml`, die auch von Hand
gepflegt wird, und daran anzuhängen ist das Risiko nicht wert.

**macOS** hat davon noch nichts. Eine Finder-Erweiterung braucht eine Developer-ID-Signatur, die
QuickRun nicht hat, und die Alternativen sind es wert, richtig gemacht statt geraten zu werden.
`quickrun open .` funktioniert dort schon heute.

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

Zeigt, wie QuickRun ein Repository ohne Config starten würde: zuerst die Skripte eines fremden
Launchers (siehe [Repositories ohne Config](/de/no-config)), dann das Ergebnis der Erkennung.
`--save` schreibt den höchstbewerteten Kandidaten in `quickrun.yml` und überschreibt eine vorhandene
Datei nicht.

## `quickrun ls` und `quickrun clean`

```bash
quickrun ls
quickrun clean --all
quickrun clean --older-than 30d
quickrun clean acme__app__main-1a2b3c
```

`clean` verlangt genau einen Selektor. Standardmäßig alles zu löschen wäre die schlechtestmögliche
Vermutung — deshalb ist „kein Selektor" ein Benutzungsfehler.

## `quickrun daemon`

```bash
quickrun daemon              # lauscht auf 127.0.0.1:9876
```

## `quickrun doctor`

```bash
quickrun doctor              # prüft, ob diese Installation hier funktioniert
quickrun doctor --no-ui      # ohne Fenster- und Tray-Prüfung, für Maschinen ohne Bildschirm
```

Jede Prüfung steht für etwas, das einmal wirklich kaputt war. Der Befehl startet einen eigenen
Listener auf einem freien Port und stellt ihm die Fragen, die die Browser-Erweiterung stellt —
darunter die zwei, die eine Sicherheitsgrenze sind: eine Run-Anfrage von einer normalen Seite muss
abgelehnt werden, eine aus einer Erweiterung nicht. Danach werden ein Fenster und ein Tray-Icon
tatsächlich erzeugt, denn das Anzeigen eines Fensters lädt das Icon der Programmdatei — und ein
kaputtes Icon ist dort im UI-Framework tödlich und nicht abfangbar.

Er nennt auch, was er selbst nicht richten kann: kein `git` im `PATH`, ein Workspace-Verzeichnis,
in das nicht geschrieben werden kann, eine `quickrun://`-Registrierung, die auf eine Programmdatei
zeigt, die es nicht mehr gibt, kein Daemon dort, wo die Erweiterung ihn sucht. Eine fehlgeschlagene
Prüfung beendet den Befehl mit einem Fehlercode; eine Warnung — Autostart, das URL-Schema — nicht,
denn das sind Annehmlichkeiten und keine Voraussetzungen.

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

Ein Workspace, von dem QuickRun keine Aufzeichnung hat, wird trotzdem gelistet — als
`unknown - no QuickRun metadata`. Das ist ein Checkout, der starb, bevor er notiert wurde, oder
einer, dessen Löschung auf halbem Weg abbrach. Was nicht gelistet ist, kann auch nicht gelöscht
werden, und genau darum ist es sichtbar. Einen, in dem keine einzige Datei mehr liegt, räumt QuickRun
ungefragt weg — darin ist nichts zu verlieren.

Eine Löschung, die nicht durchgeht, sagt das, statt Erfolg zu melden. Unter Windows ist der Grund
meist eine Datei, die noch offen ist — ein laufender Run dieses Repositories, ein Virenscanner, der
mitliest, ein Explorer-Fenster im Verzeichnis. Dann hilft: schließen und nochmal. `Remove all`
versucht jeden Workspace und nennt die, die sich verweigert haben, statt beim ersten aufzuhören.

## Authentifizierung

Für ein privates Repository, erster Treffer gewinnt:

1. `--token`
2. `QUICKRUN_TOKEN`
3. `gh auth token`, wenn du in der GitHub CLI angemeldet bist
4. einfaches `git clone`, das SSH-Keys und den Git Credential Manager mitnimmt

Tokens werden aus jeder Log-Zeile und jeder Fehlermeldung entfernt. Credential-Prompts sind
durchgehend abgeschaltet, inklusive des GUI-Dialogs, den der Git Credential Manager sonst öffnet —
für einen Hintergrund-Daemon wäre das ein unsichtbarer Deadlock.
