---
layout: home

hero:
  name: QuickRun
  text: Jedes Git-Repository mit einem Klick starten
  tagline: Von der GitHub-Seite aus, auf der du es gefunden hast — ohne eine Zeile Setup-Doku zu lesen.
  image:
    src: /logo.png
    alt: QuickRun
  actions:
    - theme: brand
      text: Installation
      link: /de/install
    - theme: alt
      text: Config-Referenz
      link: /de/config
    - theme: alt
      text: GitHub
      link: https://github.com/fgilde/QuickRun

features:
  - title: Ein Befehl
    details: >
      quickrun run acme/app checkt das Repository in einen verwalteten Workspace aus, prüft die
      Voraussetzungen, fragt die in der Config deklarierten Eingaben ab und startet es.
  - title: Tray-Icon und Dashboard
    details: >
      Doppelklick auf die Binary, und QuickRun sitzt im Tray. Das Dashboard zeigt, was läuft —
      mit Fortschritt in Echtzeit —, die Workspaces auf der Platte und wie das Pairing der
      Browser-Erweiterung geht.
  - title: Ein Button auf GitHub
    details: >
      Die Browser-Erweiterung setzt einen Run-Button neben das Branch-Dropdown, in PR-Header und in
      jede Zeile der Branch-Liste. Der Fortschritt kommt in den Button zurück.
  - title: Funktioniert ohne Config
    details: >
      Keine quickrun.yml? QuickRun erkennt Compose-Dateien, npm-Skripte, .NET-Projekte,
      Python-Apps, Makefiles, Cargo, Go, Maven und Gradle und schlägt vor, was es gefunden hat.
  - title: Nichts läuft unbesehen
    details: >
      Jeder Lauf zeigt Repository, Ref, aufgelösten Commit und die exakten Befehle und wartet auf
      deine Bestätigung. Dieser Dialog lässt sich nicht abschalten.
---

## Die kürzeste Config, die funktioniert

```yaml
run: ./run.sh
```

## Eine nützlichere

<<< @/../samples/npm-dev.yml{yaml}

Jeder Block ist optional. Die [Config-Referenz](/de/config) beschreibt die vollständige Form, die
[Beispiele](/de/samples) zeigen acht ausgearbeitete Fälle — darunter ein Multi-Service-Stack, ein
generiertes Eingabeformular mit validiertem Secret und ein Repository, das sich sein SDK selbst
installiert.

## Wie das zusammenspielt

```
GitHub-Seite (Extension-Button)
   │
   ├── http://127.0.0.1:9876/api/run   ← Hauptkanal; hierüber weiß der Button auch, ob QuickRun da ist
   └── quickrun://open                 ← startet den Daemon, wenn installiert aber nicht laufend
   │
   ▼
QuickRun auf deiner Maschine
   Checkout → Voraussetzungen → Eingaben → Bestätigung → Setup → Tasks → Fortschritt zurück zum Button
```

Ein Browser lässt sich nicht fragen, ob für ein URL-Schema ein Handler registriert ist. Deshalb ist
der localhost-Listener der Kanal, über den die Erweiterung erfährt, dass QuickRun installiert ist —
und über den der Fortschritt während des Starts zurückkommt. Siehe
[Browser-Erweiterung](/de/extension).
