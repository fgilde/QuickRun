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
  den du sowieso genommen hättest. PR-Nummer und Token für ein privates Repository liegen hinter
  *More*. Nach *Prepare* steht der Plan da — und, wenn die Config Inputs deklariert, zuerst ein
  Formular dafür; es läuft nichts ohne Bestätigung
- **Runs** — was läuft, mit Fortschritt und Log-Ausgabe in Echtzeit. Jeder Task zeigt seinen
  Zustand, seine Adresse sobald es eine gibt, und die Prozess-ID dessen, was er gestartet hat.
  **Stop** bittet den Run zu stoppen: er zeigt *stopping*, solange die Stop-Kommandos der Config
  laufen, gibt ihnen 30 Sekunden und beendet danach, was übrig ist — und zwar alles, was das Kommando
  gestartet hat, nicht nur das, was noch eine ununterbrochene Linie zurück hat: `dotnet run` startet
  die Anwendung, der Prozess dazwischen ist oft schon weg, und so eine Anwendung hat ein Stop früher
  überlebt und weiter auf ihrem Port geantwortet. Der Run verlässt diesen Zustand
  immer.

  Ein beendeter Run kann weiterhin Prozesse besitzen: ein Task, der einen Server im Hintergrund
  startet und sich beendet, ist als Task fertig — der Server läuft weiter. So ein Run zeigt *still
  running* und behält ein **Stop**, das diese Prozesse beendet; genau dieser Fall war es, in dem
  „stopped" gelogen hat. **Remove** nimmt einen beendeten Run aus der Liste und löscht nichts, der
  Checkout bleibt unter Workspaces; solange noch Prozesse leben, wird es abgelehnt, weil der Eintrag
  der letzte Griff an ihnen ist
- **Config builder** — eine `quickrun.yml` schreiben, prüfen und testen, siehe [Config-Builder](/de/builder)
- **Workspaces** — was ausgecheckt ist, wieviel Platz es braucht, und wie man es entfernt
- **Browser extension** — wie die Erweiterung funktioniert
- **Settings** — ob QuickRun beim Anmelden startet, ob `quickrun` im Terminal funktioniert, und
  was die Kommandozeile kann
- **About** — Version, Installationsquelle, Update-Prüfung

Das Fenster öffnet dort, wo du es verlassen hast, in der Größe, in der du es verlassen hast, und
maximiert, wenn es maximiert war — festgehalten in `window.json` neben den Workspaces.

Dieselbe Ansicht gibt es im Browser unter `http://127.0.0.1:9876`, wenn du sie dort lieber hast.
`quickrun --browser` öffnet sie so; `quickrun --no-tray` lässt das Tray-Icon ganz weg.

## Einstellungen

Zwei Schalter, beide benutzerbezogen, keiner braucht Administratorrechte:

- **Start QuickRun when I sign in** — der Browser-Button braucht ein laufendes QuickRun. Unter
  Windows ist das ein Wert unter `HKCU\...\CurrentVersion\Run`, unter Linux eine `.desktop`-Datei
  in `~/.config/autostart`, unter macOS ein Launch Agent in `~/Library/LaunchAgents`. Der
  Settings-Tab zeigt welcher, damit man es auch von Hand zurücknehmen kann — und sagt es, wenn der
  Eintrag auf ein Programm zeigt, das inzwischen woanders liegt.
- **Make `quickrun` work in a terminal** — unter Windows kommt das Programmverzeichnis in den
  eigenen PATH, und laufende Shells werden benachrichtigt; ein *neues* Terminal hat den Befehl dann.
  Unter Linux und macOS wird `quickrun` in ein bin-Verzeichnis verlinkt, das ohnehin im PATH liegt
  (`~/.local/bin`, unter macOS das Homebrew-Verzeichnis, wenn es beschreibbar ist), statt in irgendein
  Shell-Profil zu schreiben.

`quickrun install` macht beides plus den `quickrun://`-Handler in einem Schritt, `quickrun uninstall`
nimmt es zurück.

## Protokoll registrieren und beim Anmelden starten

```bash
quickrun install
```

Registriert das `quickrun://`-Schema und legt einen Autostart-Eintrag an. Das Schema hat eine
Aufgabe: die Browser-Erweiterung kann QuickRun damit starten, wenn es installiert ist aber nicht
läuft. Ohne funktioniert alles weiter, solange QuickRun läuft, wenn du den Button klickst.

Unter Linux landet dabei auch das Icon in `~/.local/share/icons`, und neben dem Handler wird ein
normaler Programmeintrag geschrieben: QuickRun steht dann mit Icon im Menü wie jedes andere
Programm. Unter Windows steckt das Icon in der Binary, unter macOS im App-Bundle.

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

**Über → Auf x.y.z aktualisieren** im Fenster erledigt es: QuickRun lädt das Release für deine
Plattform, prüft es gegen die mitveröffentlichten Prüfsummen, ersetzt das Binary und startet in die
neue Version neu. Windows, macOS und Linux gleichermaßen. Die Seite wartet, bis der neue Build
antwortet, und lädt sich dort hinein — „hat es geklappt" muss niemand fragen.

Dasselbe im Terminal:

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
