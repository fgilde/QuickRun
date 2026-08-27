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

Deklariert die Config [Inputs](/de/config#inputs), fragt das Fenster sie ab, bevor es überhaupt
etwas zu bestätigen gibt: Label, Beschreibung, Default, ein Dropdown für `select`, eine Checkbox für
`bool`, ein maskiertes Feld für `password`. Ein geänderter Wert wird einen Moment später von selbst
übernommen und die Befehlsliste daraus neu gebaut — auf dem Schirm steht also immer die Liste, die
diese Werte ergeben: du bestätigst genau das, was läuft, und es bleibt bei einem Klick auf **Run**.
*Continue* steht nur auf dem Button, solange nötige Werte fehlen, denn bis dahin gibt es keine
Befehlsliste zu bestätigen. Werte mit `env` gehen als diese Umgebungsvariable in den Lauf, und ein
Secret wird nie ans Fenster zurückgeschickt.

Solange ein Lauf dieses Branches läuft, ist der Button der Weg dorthin und nicht der Weg in einen
zweiten Lauf. Ein Klick öffnet die Aktionen:

- **Show log** — holt das Log-Fenster zurück, oder öffnet ein neues, das sich an den Lauf hängt. Das
  Fenster zu schließen hat nie etwas gestoppt, also muss man auch zurückkommen können
- **Open …** — die Adresse, die der Lauf gemeldet hat, sobald er eine gemeldet hat
- **Stop** — stoppt den Lauf, und beendet auch das, was er laufen ließ, wenn ein Task sich beendet
  und einen Server hinterlassen hat

Nach einem Reload findet der Button seinen Lauf wieder: der Tab vergisst ihn, der Daemon nicht. Ein
Branch, der schon läuft, lässt sich damit nicht versehentlich zweimal starten.

Nach der Bestätigung bleibt das Fenster offen und wird zum Log des Laufs: der Checkout mit den
echten Fortschrittszählern, jeder Setup-Schritt und alles, was die Befehle des Repositories
ausgeben. Der Button auf der Seite zeigt nur Prozent und eine grobe Phase — ein Toolbar-Button ist
kein Ort für hundert Zeilen Build-Ausgabe.

Sobald der Lauf läuft, bekommt jeder Task eine eigene Zeile: was er tut — *starting*, *ready*,
*exited* — die Adresse, die er gemeldet hat, als Link, und die Prozess-ID dessen, was er gestartet
hat. Bei einer Desktop-Anwendung ist diese PID der einzige Griff, den man an ihr hat — und genau
darauf wartet ein Task mit `readyWhen: {window: true}`. „Running" für den ganzen Lauf sagt nichts
darüber, welcher von fünf Diensten oben ist.

**Stop** stoppt den Lauf und sagt es: der Button wechselt beim Klick sofort auf *Stopping…* mit
Spinner, das Banner auf *Stopped*, sobald die Prozesse weg sind, und das Fenster schließt sich kurz
danach selbst. „Sobald die Prozesse weg sind" ist wörtlich gemeint: hat der Lauf etwas im Hintergrund
laufen lassen, sagt das Fenster wie viele und stoppt erneut, statt es stopped zu nennen — du wolltest stoppen, nicht ein Fenster aufräumen. Klickbar ist es nur, solange
wirklich etwas läuft: ein Lauf, dessen Prozesse alle beendet sind, hat nichts mehr zu stoppen.

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
| Wo der Button erscheint | bei jedem Repository |

### Aus QuickRun heraus installieren

QuickRuns eigenes Fenster hat einen Reiter **Browser-Erweiterung**, der die auf diesem Rechner
gefundenen Browser auflistet und pro Browser sagt, ob die Erweiterung dort liegt. Ein entpackter
Build zählt mit: Er wird an dem Ordner erkannt, aus dem er geladen wurde — und den notiert der
Browser selbst.

Der Installieren-Knopf geht so weit, wie ein Browser es zulässt, und keinen Schritt weiter. Wo es
eine Store-Listung gibt, öffnet er sie in genau diesem Browser, und der letzte Klick ist der
Hinzufügen-Knopf des Browsers. Wo es noch keine gibt, lädt er die gepackte Erweiterung aus dem
neuesten Release, entpackt sie, öffnet die Erweiterungsseite des Browsers und daneben den Ordner —
übrig bleibt „Entpackte Erweiterung laden, diesen Ordner wählen".

Dass er einen Klick vorher aufhört, ist Absicht. Chrome hat die Installation aus einer Seite heraus
2018 abgeschafft, und der einzige verbliebene Weg, ein Programm eine Erweiterung in Chrome oder Edge
setzen zu lassen, ist eine Unternehmensrichtlinie, die sie erzwingt und dir das Entfernen nimmt. Das
tut Schadsoftware, und QuickRun tut es nicht. Firefox ist die Ausnahme in die andere Richtung: Über
`about:debugging` lässt sich ein temporäres Add-on laden, das Firefox beim Schließen wieder
vergisst.

### Wo der Button erscheint

QuickRun startet fast jedes Repository: Gibt es keine Konfiguration, liest es die Dateien und baut
sich selbst einen Plan. Dieser Plan ist eine begründete Vermutung — eine gute, und die genauen
Befehle siehst du vor jedem Lauf — aber vielleicht willst du den Button nur dort, wo das Repository
selbst gesagt hat, wie es gestartet werden möchte.

- **Bei jedem Repository.** Auch bei denen, die QuickRun sich selbst erschließen müsste.
- **Wo es Anweisungen gibt.** Eine `quickrun.yml` oder Skripte, die für Pinokio geschrieben wurden.
- **Nur mit quickrun.yml.** Das Repository hat eine QuickRun-Konfiguration eingecheckt.

Die letzten beiden fragen QuickRun auf deinem Rechner, ob es diese Dateien gibt — einmal pro
Repository, eine halbe Stunde lang gemerkt. Die Erweiterung fragt GitHub nicht selbst: dafür bräuchte
sie eine Berechtigung für `raw.githubusercontent.com`, und der Daemon kommt ohne aus. Läuft QuickRun
nicht, lässt sich die Prüfung nicht durchführen, und der Button erscheint trotzdem — zu verschwinden,
weil eine Prüfung fehlschlug, wäre von einer kaputten Erweiterung nicht zu unterscheiden.

Das Bestätigungsfenster nennt bei jedem Lauf die Quelle, damit klar ist, welcher der vier Fälle
vorliegt: die `quickrun.yml` des Repositories, eine Konfiguration, die du selbst dafür gespeichert
hast, die Skripte eines anderen Launchers, oder QuickRuns eigene Lesart der Dateien.

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
