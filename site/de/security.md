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
- Jeder Endpoint, der etwas starten kann, verlangt, dass die Anfrage von einer Browser-Erweiterung
  kommt — geprüft am `Origin`-Header, den der Browser selbst setzt und eine Seite nicht fälschen kann.
- `/api/ping` braucht absichtlich keinen Token: einer Seite mitzuteilen, dass QuickRun existiert, ist
  seine ganze Aufgabe. Mehr gibt er nicht heraus — keine Repository-Namen, keine Pfade, keine
  Lauf-Inhalte.
- `https://github.com` ist bewusst **kein** akzeptierter Origin. Ein Skript, das auf GitHub selbst
  läuft, kann keinen Lauf starten.
- Ein Aufrufer ohne `Origin` ist kein Browser — `curl` oder QuickRuns eigene CLI — und ist erlaubt.
  Ein solches Programm läuft ohnehin mit deinen Rechten und gewinnt über den Daemon nichts dazu.
- Es gibt keinen Pairing-Token mehr. Er schützte vor genau dem, wovor die Origin-Prüfung schützt,
  kostete aber jeden Nutzer einen Einrichtungsschritt.

## Seiten, die das Fenster öffnen dürfen

Eine Webseite kann weiterhin nichts starten. Was eine Seite auf einer vertrauten Domain darf: die
lokale QuickRun bitten, **ihr eigenes Fenster** mit einem Plan zu öffnen — statt dich über einen
`quickrun://`-Link oder einen neuen Tab an dieselbe Stelle zu schicken.

- `POST /api/show` ist der einzige Endpunkt, den eine Webseite überhaupt erreicht, und er tut genau
  eines: das Fenster öffnen. Alles, was einen Run starten, stoppen oder lesen könnte, bleibt hinter
  der Extension-Prüfung oben, die eine Seite nicht passiert. Das Schlimmste, was eine vertraute
  Seite anrichten kann, ist ein Fenster, das aufgeht.
- Der Plan darin wartet auf dich — genauso wie wenn die Extension ihn dorthin gelegt hat.
- `*.quickrun.org` steht von Anfang an drin, weil QuickRun von dort kommt: wer es dort
  heruntergeladen hat, hat dieser Seite schon deutlich mehr vertraut als ein Fenster.
- Nur `https` zählt. Bei einfachem `http` kann jede Zwischenstation die Seite umschreiben, während
  der `Origin`-Header weiter den vertrauten Namen nennt — der Name wäre also kein Beleg.
  `http://localhost` ist die Ausnahme, dort ist nichts dazwischen.
- Die Subdomain-Form vergleicht ganze Labels. `*.example.com` deckt `example.com` und
  `app.example.com` ab, niemals `notexample.com` oder `example.com.angreifer.net`.
- Eine vertraute Seite darf ein Repository und die gewünschte Config nennen. Sie darf **keine Datei
  auf deinem Rechner** nennen: das wird hier unabhängig von der Seite abgelehnt und bleibt etwas,
  auf das du selbst zeigst.
- Eine Seite kann sich nicht selbst eintragen. Die Liste wird in QuickRuns Fenster unter
  **Settings** gepflegt — hinter dessen eigenem Token — oder in der Datei, die das Fenster nennt.
- Eine leere Liste schaltet das Ganze ab, auch die Voreinstellung. Jede Seite nimmt dann wieder den
  Link, so wie vorher.

## Was ein Link mitbringen darf

Ein `quickrun://`-Link — dem ein [README-Badge](/de/badge) am Ende folgt — ist eine Zeichenkette,
die geschrieben hat, wer die Seite geschrieben hat. Genau so wird er behandelt:

- Nur `repo`, `ref` und `pr` überleben. Ein Befehl, eine Config, ein Token oder ein lokaler Pfad im
  Link wird kommentarlos verworfen — nichts davon war je unsere Sache.
- `repo` muss `owner/name` oder eine `https://`-URL sein. `ssh://`, `file://` und
  `git@host:owner/name` werden aus einem Link abgelehnt: sie selbst auf der CLI zu tippen ist eine
  Entscheidung, ein Link, der es tut, ist keine.
- Der Link startet nie etwas. Er öffnet QuickRuns eigenes Fenster beim Repository, wo der Plan
  gebaut wird und der Bestätigungsdialog genauso gilt wie überall sonst.

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
