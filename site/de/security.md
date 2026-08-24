# Sicherheit

**QuickRun führt Code aus dem Repository aus, auf das du es zeigst — mit deinen Rechten und ohne
Sandbox.** Alles Folgende ergibt sich daraus.

## Der Bestätigungsdialog

Jeder Lauf zeigt Repository, Ref, aufgelösten Commit und die exakte Liste der Befehle — `setup`,
`tasks`, `stop` oder den erkannten Fallback — und wartet auf deine Zustimmung. Es gibt keine
Einstellung, die das abschaltet, und ein aus dem Browser ausgelöster Lauf kann es nicht überspringen:
der Listener bereitet den Lauf vor und liefert den Plan zurück, und erst eine zweite, ausdrückliche
Bestätigung startet ihn.

`--yes` auf der Kommandozeile überspringt die Rückfrage, weil du den Befehl selbst getippt hast.
Nichts, was eine Webseite tun kann, erreicht dieses Flag.

## Einem Repository vertrauen

Wenn du ein Repository freigibst, wird ein Hash *dieser Befehle* gespeichert. Ändert sich die Config,
ändert sich der Hash und du wirst wieder gefragt — ein Repository kann also nicht einmal
freigegeben und später still in etwas anderes verwandelt werden. Vertrauen gilt pro Repository, nie
pro Owner oder pro Host, und ist widerrufbar.

Der Hash ignoriert Repository, Ref und Commit bewusst: neue Commits in einem vertrauten Repository
funktionieren weiter, geänderte Befehle nicht.

## Der Listener

- Bindet nur an `127.0.0.1`, nie an eine aus dem Netz erreichbare Adresse.
- Jeder Endpoint außer `GET /api/ping` verlangt den Pairing-Token.
- `/api/ping` braucht absichtlich keinen Token: einer Seite mitzuteilen, dass QuickRun existiert, ist
  seine ganze Aufgabe. Mehr gibt er nicht heraus — keine Repository-Namen, keine Pfade, keine
  Lauf-Inhalte.
- CORS wird ausschließlich für `https://github.com` gewährt.
- Ein Token wird nur ausgegeben, während ein Pairing-Fenster offen ist, und dieses Fenster kann nur
  von deiner Maschine geöffnet werden.

## Secrets

Werte aus `password`-Eingaben bleiben für den Lauf im Speicher, gehen als Umgebungsvariablen an die
Kindprozesse und werden nie in Logs, Lauf-Historie oder Fortschrittstexte geschrieben. Zugriffstokens
werden aus jeder Log-Zeile und jeder Fehlermeldung entfernt, bevor sie angezeigt oder gespeichert
werden.

Sehr kurze Werte werden nicht ersetzt: ein einzelnes Zeichen pauschal zu ersetzen würde jede
Log-Zeile verstümmeln, in der es zufällig vorkommt.

## Auto-Update

Ein Update ist ein Code-Ausführungskanal und wird als solcher behandelt:

- das Asset wird ausschließlich von der `github.com`-Download-URL des Releases über HTTPS geholt
- sein SHA-256 muss zur mit dem Release veröffentlichten `SHA256SUMS` passen; eine Abweichung bricht ab
- QuickRun ersetzt sein eigenes Binary nur, wenn nichts anderes es verwaltet — einer
  Paketmanager-Installation wird stattdessen der Upgrade-Befehl gemeldet
- das Update wird beim Neustart angewendet, nie mitten in einem Lauf
- `--no-update` schaltet die Prüfung vollständig ab

## Was nicht geschützt ist

- **Keine Sandbox.** Die Befehle eines vertrauten Repositories laufen mit deinen vollen Rechten.
  Container-Isolation wurde erwogen und verschoben: die meisten Repositories laufen unverändert
  nicht im Container.
- **Unsignierte Binaries.** Siehe [Installation](/de/install) für die Folgen pro Plattform.
- **Die Befehle selbst.** QuickRun zeigt dir, was laufen wird. Es kann dir nicht sagen, ob das
  sicher ist. Starte nur Repositories, bei denen du auch `git clone && ./run.sh` von Hand machen
  würdest.

## Probleme melden

Ein Issue auf [github.com/fgilde/QuickRun](https://github.com/fgilde/QuickRun/issues). Für alles, was
du für ausnutzbar hältst, bitte GitHubs private Vulnerability-Reporting auf diesem Repository nutzen
statt eines öffentlichen Issues.
