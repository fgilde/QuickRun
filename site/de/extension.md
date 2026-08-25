# Browser-Erweiterung

Die Erweiterung setzt einen Run-Button dorthin, wo du schon bist: neben das Branch-Dropdown auf der
Repository-Seite, in den Header eines Pull Requests und in jede Zeile der Branch-Liste.

## Warum sie den lokalen Listener braucht

Ein Browser lässt sich nicht fragen, ob für ein URL-Schema ein Handler registriert ist. Das ist
Absicht — es wäre ein Fingerprinting-Vektor — und es gibt keine API dafür. Eine Erweiterung, deren
einziger Kanal `quickrun://` ist, kann also nie wissen, ob QuickRun installiert ist, und nie
Fortschritt aus einem Lauf zurückbekommen, den sie ausgelöst hat.

Deshalb ist der localhost-Listener der Hauptkanal, und `quickrun://` hat genau eine Aufgabe übrig:
einen installierten, aber nicht laufenden Daemon zu starten.

| Ping-Ergebnis | Der Button zeigt |
|---|---|
| QuickRun antwortet | **Run this** — Klick bereitet einen Lauf vor |
| keine Antwort, dann greift `quickrun://` | **Starting QuickRun…**, danach Run this |
| überhaupt keine Antwort | **Install QuickRun** — verlinkt die Download-Seite |

## Button-Zustände

- **ready** — QuickRun hat geantwortet. Klick bereitet einen Lauf vor.
- **running** — aktuelle Phase und, wo es eine echte Zahl gibt, ein Prozentwert.
- **done** — der Lauf ist fertig.
- **error** — der Tooltip zeigt, warum.

## Das Bestätigungsfenster

Ein Klick auf Run startet nichts. QuickRun checkt das Repository aus, baut den Plan, und die
Erweiterung öffnet ein Fenster mit Repository, Ref, aufgelöstem Commit und den **exakten Befehlen**,
die laufen werden. Erst der Button in diesem Fenster startet sie.

Das Fenster zeigt außerdem die `description` aus der Config, wenn es eine gibt, das Verzeichnis, in
das ausgecheckt wurde, und — sobald ein Task eine meldet — die Adresse, auf der es läuft, als Link.

Nach der Bestätigung bleibt das Fenster offen und wird zum Log des Laufs: der Checkout mit den
echten Fortschrittszählern, jeder Setup-Schritt und alles, was die Befehle des Repositories
ausgeben. Der Button auf der Seite zeigt nur Prozent und eine grobe Phase — ein Toolbar-Button ist
kein Ort für hundert Zeilen Build-Ausgabe.

**Stop** stoppt den Lauf und sagt es: das Banner wechselt auf *Stopped*, und das Fenster bietet
Close an. Klickbar ist es nur, solange wirklich etwas läuft — ein Lauf, dessen Prozesse alle beendet
sind, hat nichts mehr zu stoppen.

Dieses Fenster ist eine Extension-Seite und nicht Teil der GitHub-Seite — und das mit Absicht: eine
Webseite kann über ihren eigenen Inhalt ein überzeugendes gefälschtes Panel zeichnen, und niemand
soll je eine Befehlsliste bestätigen, während eine andere ausgeführt wird.

## Warum keine Webseite es fernsteuern kann

Es gibt nichts zu koppeln. Der Browser hängt an jede seitenübergreifende Anfrage einen
`Origin`-Header, den eine Seite nicht ändern kann — QuickRun lehnt daher alles ab, was nicht von
einer Browser-Erweiterung kommt. `https://github.com` steht nicht auf dieser Liste: ein Skript auf
GitHub selbst kann keinen Lauf starten.

Ein Programm auf deinem eigenen Rechner — `curl`, QuickRuns eigene CLI — sendet keinen `Origin`
und ist erlaubt. Es läuft ohnehin mit deinen Rechten, der Daemon gibt ihm also nichts dazu.
## Optionen

| Einstellung | Standard |
|---|---|
| Port | 9876 |
| `quickrun://` versuchen, wenn QuickRun nicht antwortet | an |

Einen Pull Request zu starten heißt, den Branch zu starten, aus dem er kommt — geholt als
`refs/pull/<n>/head`. Das funktioniert auch bei Pull Requests aus Forks, und genau das macht der
Button auf einer PR-Seite.

## Selbst bauen

```bash
cd extension
sh build.sh
```

Ein Quellbaum, zwei Builds: `dist/chromium` für Chrome, Edge und Opera, und `dist/firefox`, das sich
nur im Manifest unterscheidet.

## Wenn GitHub sein DOM ändert

Der Button hängt an `data-testid`-Attributen und ARIA-Labels, wo GitHub sie anbietet, und jede
Suche scheitert still: ein fehlender Button ist akzeptabel, eine kaputte GitHub-Seite nicht. GitHub
baut diese Seiten um — ein fehlender Button bedeutet also meist, dass die Erweiterung ein Update
braucht, und nicht, dass an deinem Setup etwas falsch ist.
