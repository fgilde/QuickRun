# Der Config-Builder

In QuickRuns lokaler UI den Reiter **Config builder** öffnen, oder direkt
[`http://127.0.0.1:9876/#builder`](http://127.0.0.1:9876/#builder).

Er ist für zwei Aufgaben: die `quickrun.yml` für ein eigenes Repository schreiben, und für ein
fremdes Repository eine eigene Config behalten.

## Der Ablauf

1. **Load** — Repository eintippen, Load drücken. QuickRun checkt es aus und legt die Config in den
   Editor, die es benutzen würde, samt Herkunft:

   | Badge | Bedeutet |
   |---|---|
   | your config | die Override-Config, die du für dieses Repository gespeichert hast |
   | the repository's own config | die committete `quickrun.yml` |
   | derived from Pinokio scripts | aus `install.js` / `start.js` übersetzt |
   | detected, not committed | QuickRuns Vermutung aus den vorhandenen Dateien |
   | a starting point | nichts Erkennbares — eine leere Vorlage |

2. **Bearbeiten** — der Editor vervollständigt die Schlüssel des Blocks, in dem der Cursor steht,
   aus demselben [Schema](https://quickrun.org/quickrun.schema.json), das QuickRun
   veröffentlicht. Dazu Snippets für die Blöcke, die niemand auswendig kennt: ein Task mit
   Readiness-Prüfung und `open`, ein `requires`-Eintrag für .NET, Node oder Docker, ein
   Secret-Input.

3. **Check** — vom Daemon geparst und validiert, mit demselben Parser und Validator, den ein Lauf
   benutzt. Fehler und Warnungen erscheinen unter dem Editor und in der Gutter-Spalte. Eine
   Schema-Näherung im Browser würde irgendwann anderer Meinung sein als das echte Ding; das hier
   kann es nicht.

4. **Test against the repository** — bereitet einen echten Lauf aus dem Editor-Text vor. Die Config
   in Arbeit gewinnt gegen die des Repositories und gegen jede Override-Config, du testest also
   genau das, was du siehst. Die Befehlsliste erscheint, bei deklarierten Inputs auch das Formular,
   und es läuft nichts ohne Bestätigung.

   Nach der Bestätigung bleibt der Lauf genau dort: Fortschritt, die Task-Zustände mit Adresse und
   Prozess-ID, das Log im Zulauf und **Stop**. Eine Config zu schreiben heißt, dasselbe zehnmal
   hintereinander zu starten — und dafür soll man den Tab nicht verlassen müssen. **Remove** nimmt
   einen beendeten Versuch aus der Liste; der Checkout bleibt, der nächste Versuch dauert Sekunden.

   Ein weiterer Klick auf **Test** beendet zuerst den vorigen Testlauf — samt allem, was er im
   Hintergrund laufen ließ. Ein Versuch, über den du hinweg bist, soll nicht den Port halten, den
   der nächste braucht.

5. **Save as my config** — behält sie für dieses Repository. Oder, beim eigenen Repository: den Text
   als `quickrun.yml` in die Repository-Wurzel legen und committen.

## Wo die eigene Config liegt

`Save as my config` schreibt in QuickRuns eigenes Verzeichnis, nicht in den Checkout:

```
<Workspace-Wurzel>/configs/<owner>_<repo>-<hash>/__auto_quickrun.config.yml
```

Das ist Absicht. Im Checkout würde `--fresh` die Datei löschen, sie würde im `git status` eines
fremden Repositories auftauchen und ließe sich versehentlich in ein Projekt committen, das nicht
deins ist. Sie gilt für **jeden Branch** dieses Repositories, und die Liste **Configs you saved**
unten im Reiter öffnet oder entfernt sie wieder.

## Welche Config ein Lauf benutzt

Von spezifisch nach allgemein:

1. eine Config von der Kommandozeile — `quickrun run … --config pfad/zu/andere.yml`
2. der Text im Builder, solange du ihn testest
3. deine gespeicherte Config für dieses Repository
4. die `quickrun.yml` des Repositories
5. die Skripte eines anderen Launchers — siehe [Repositories ohne Config](/de/no-config)
6. Erkennung

Wird deine Config anstelle einer mitgelieferten `quickrun.yml` benutzt, sagt der Lauf das in seiner
ersten Log-Zeile. Ein Lauf, der eine committete Config stillschweigend ignoriert, kostet sonst
Stunden.

## Wenn sie fertig ist

Beim eigenen Repository gehört die Datei als `quickrun.yml` in die Repository-Wurzel, committet. Die
Schema-Zeile oben gibt dieselbe Vervollständigung in VS Code, JetBrains und allem anderen, das den
YAML-Language-Server spricht, und `quickrun validate` prüft sie in einem Pre-Commit-Hook:

```bash
quickrun validate
```

## Der Editor

Monaco, heruntergebrochen auf den Editor und YAML-Hervorhebung, vom Daemon aus der Binary
ausgeliefert — kein CDN. Damit funktioniert er offline, und niemand außerhalb deiner Maschine
erfährt, welche Config du bearbeitest.

Lädt er aus irgendeinem Grund nicht, fällt der Reiter auf ein einfaches Textfeld zurück. Prüfen,
Testen und Speichern sind davon nicht betroffen: alle drei passieren im Daemon.
