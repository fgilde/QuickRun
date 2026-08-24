# Datenschutz

QuickRun sammelt nichts.

Keine Analytics, keine Telemetrie, kein Crash-Reporting, kein Konto. Nichts über dich oder die
Repositories, die du startest, wird irgendwohin gesendet.

## Was deine Maschine verlässt

Drei Dinge, alle von dir ausgelöst:

| Anfrage | An | Wofür |
|---|---|---|
| `git clone` / `git fetch` | den Host des Repositories, meist github.com | um den Code auszuchecken, den du starten willst |
| Release-API und ein Release-Asset | api.github.com, github.com | um nach einem Update zu sehen und es zu laden; mit `--no-update` abschaltbar |
| was die Befehle des Repositories tun | wohin diese Befehle zeigen | QuickRun startet sie; was sie kontaktieren, liegt bei ihnen |

Die letzte Zeile gehört klar gesagt: QuickRun führt Befehle aus dem Repository aus, auf das du es
zeigst. Diese Befehle können ins Netz gehen, und QuickRun schränkt das weder ein noch prüft es das.
Die Befehle werden dir vor der Ausführung gezeigt.

## Was auf deiner Maschine bleibt

- **Workspaces** — die ausgecheckten Repositories, im Anwendungsdaten-Verzeichnis des Systems.
- **Lauf-Historie** — Repository, Ref, Commit, Ergebnis und ein Log-Ende, in jedem Workspace.
- **Eingaben** — Werte, die eine `quickrun.yml` deklariert. Als `password` markierte Werte bleiben
  für den Lauf im Speicher und gehen als Umgebungsvariablen an die Befehle. Sie werden nie in Logs,
  Lauf-Historie oder Fortschrittstexte geschrieben und nur gespeichert, wenn du es ausdrücklich
  verlangst.

`quickrun clean --all` entfernt jeden Workspace und alles darin.

## Die Browser-Erweiterung

Die Erweiterung speichert den Port und zwei Einstellungen und sendet sie
ausschließlich an `127.0.0.1`. Sie liest nichts aus den Seiten, die du besuchst: sie setzt einen
Button auf `github.com` und nimmt Repository und Ref aus der Adresszeile. Zugriff auf deine anderen
Tabs verlangt sie nicht.

## Die Webseite

Diese Seiten liefert GitHub Pages als statische Dateien aus. Keine Cookies, keine Analytics, keine
Skripte von Dritten. GitHub protokolliert Anfragen an seine Server unter
[der eigenen Datenschutzerklärung](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).

## Kontakt

Fragen oder Korrekturen: [github.com/fgilde/QuickRun/issues](https://github.com/fgilde/QuickRun/issues)
