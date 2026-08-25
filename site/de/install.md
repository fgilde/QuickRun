# Erster Start

Die Dateien holst du von der [Download-Seite](/de/download). Diese Seite beschreibt, was du damit
machst.

## Starten

Binary ohne Argumente starten, oder doppelklicken:

```bash
quickrun
```

QuickRun startet seinen Listener auf `127.0.0.1:9876`, legt ein Icon ins Tray und öffnet sein
Fenster.

Unter Windows zeigt das Fenster dieselbe Seite, die der Listener ausliefert, im System-WebView —
eine Oberfläche statt zwei, und die Browser-Engine ist die, die Windows sowieso mitbringt. Die Seite
bringt ihren eigenen Header mit, deshalb fügt das Fenster keinen zweiten hinzu, und der Link **Open
in browser** darin ist der Ausweg, den ein Fenster ohne Adressleiste sonst nicht hat. Sonst, und
immer wenn kein WebView verfügbar ist oder `QUICKRUN_NO_WEBVIEW` gesetzt ist, zeichnet das Fenster
seine eigene native Ansicht derselben Daten.

Das Fenster hat diese Bereiche:

- **Start a run** — ein Repository und ein Branch, ohne Browser-Erweiterung. Repository eintippen,
  QuickRun listet die Branches, stellt die schon einmal gelaufenen Refs nach oben und wählt den vor,
  den du sowieso genommen hättest. PR-Nummer, Token für ein privates Repository und die Inputs der
  Config liegen hinter *More*. Nach *Prepare* steht der Plan da; es läuft nichts ohne Bestätigung
- **Runs** — was läuft, mit Fortschritt und Log-Ausgabe in Echtzeit
- **Config builder** — eine `quickrun.yml` schreiben, prüfen und testen, siehe [Config-Builder](/de/builder)
- **Workspaces** — was ausgecheckt ist, wieviel Platz es braucht, und wie man es entfernt
- **Browser extension** — wie die Erweiterung funktioniert
- **About** — Version, Installationsquelle, Update-Prüfung

Dieselbe Ansicht gibt es im Browser unter `http://127.0.0.1:9876`, wenn du sie dort lieber hast.
`quickrun --browser` öffnet sie so; `quickrun --no-tray` lässt das Tray-Icon ganz weg.

## Protokoll registrieren und beim Anmelden starten

```bash
quickrun install
```

Registriert das `quickrun://`-Schema und legt einen Autostart-Eintrag an. Das Schema hat eine
Aufgabe: die Browser-Erweiterung kann QuickRun damit starten, wenn es installiert ist aber nicht
läuft. Ohne funktioniert alles weiter, solange QuickRun läuft, wenn du den Button klickst.

Der Reiter **Browser extension** in der lokalen UI macht dasselbe nur für das Schema und sagt, wie
der Stand ist: *registered*, *not registered* oder *registered to another build* — letzteres nach
dem Verschieben oder Neuinstallieren der Binary, und der einzige Fehler, der wie Erfolg aussieht.
Administratorrechte braucht das nicht: unter Windows ist es ein Schlüssel unter
`HKCU\Software\Classes\quickrun`, unter Linux eine `.desktop`-Datei in
`~/.local/share/applications`.

Unter macOS braucht das Schema das [App-Bundle](/de/download#macos-app-bundle) — eine nackte Binary
kann kein URL-Schema für sich beanspruchen.

## Prüfen, ob es funktioniert

Ein beliebiges Repository auf GitHub öffnen. Neben dem Branch-Dropdown erscheint ein
**Run this**-Button. Ein Klick startet noch nichts: QuickRun checkt das Repository aus, dann zeigt
die Erweiterung die exakten Befehle und wartet auf deine Bestätigung.

## Aktualisieren

```bash
quickrun update          # installiert, wenn QuickRun das Binary besitzt
quickrun update --check  # meldet nur
```

QuickRun leitet aus dem Installationspfad ab, wer das Binary verwaltet. Hat ein Paketmanager es dort
abgelegt, meldet `update` die Version und den passenden Befehl statt die Datei zu überschreiben —
zwei Updater, die um dieselbe Datei kämpfen, sind der Anfang von Versionschaos.

Der Download wird gegen die mit dem Release veröffentlichten Checksummen geprüft, bevor etwas
ersetzt wird, und das Update greift beim Neustart, nie mitten in einem Lauf. `--no-update` schaltet
die Prüfung ab.

## Deinstallieren

```bash
quickrun clean --all   # zuerst die ausgecheckten Workspaces entfernen
quickrun uninstall     # quickrun:// und den Autostart-Eintrag abmelden
```

Danach das Binary entfernen, oder `scoop uninstall quickrun` / `brew uninstall quickrun` /
`winget uninstall fgilde.QuickRun`.
