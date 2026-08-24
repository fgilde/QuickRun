# Erster Start

Die Dateien holst du von der [Download-Seite](/de/download). Diese Seite beschreibt, was du damit
machst.

## Starten

Binary ohne Argumente starten, oder doppelklicken:

```bash
quickrun
```

QuickRun startet seinen Listener auf `127.0.0.1:9876`, legt ein Icon ins Tray und öffnet sein
Fenster. Das Fenster hat vier Bereiche:

- **Runs** — was läuft, mit Fortschritt und Log-Ausgabe in Echtzeit
- **Workspaces** — was ausgecheckt ist, wieviel Platz es braucht, und wie man es entfernt
- **Browser extension** — der Pairing-Button und wie die Erweiterung funktioniert
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

Unter macOS braucht das Schema das [App-Bundle](/de/download#macos-app-bundle) — eine nackte Binary
kann kein URL-Schema für sich beanspruchen.

## Browser-Erweiterung pairen

Im QuickRun-Fenster **Browser extension** öffnen und **Open pairing window** klicken. Dann innerhalb
von 60 Sekunden in der Erweiterung auf **Pair**. Aus dem Terminal:

```bash
quickrun pair
```

Der Token bleibt im Extension-Storage des Browsers. Er wird keiner Webseite gegeben, und das
Content-Script sieht ihn nie. `quickrun pair --revoke` macht ihn ungültig.

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
