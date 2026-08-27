# README-Badge

Ein Badge im README, das dein Projekt startet. Auf der anderen Seite ist keine Erweiterung nötig —
wer klickt, landet entweder in QuickRun oder auf der Download-Seite.

[![QuickRun](https://fgilde.github.io/QuickRun/badge.svg)](/de/run?repo=fgilde/QuickRun)

## Die Zeile

`owner/repo` durch dein Repository ersetzen:

```markdown
[![QuickRun](https://fgilde.github.io/QuickRun/badge.svg)](https://fgilde.github.io/QuickRun/de/run?repo=owner/repo)
```

Die [Run-Seite](/de/run) hat ein Feld, das die Zeile schreibt, und einen Kopieren-Button.

Ein bestimmter Branch oder ein Pull Request, wenn der Standard nicht das ist, was laufen soll:

```markdown
[![QuickRun](https://fgilde.github.io/QuickRun/badge.svg)](https://fgilde.github.io/QuickRun/de/run?repo=owner/repo&ref=develop)
```

Für englischsprachige Leser führt `…/run?repo=…` ohne `de/` auf dieselbe Seite auf Englisch.

## Was beim Klick passiert

1. Das Badge ist ein normaler Link. GitHub rendert es, und niemand muss etwas installiert haben, um
   es zu sehen.
2. Die Seite fragt `http://127.0.0.1:9876/api/ping`, ob QuickRun auf diesem Rechner läuft.
3. Antwortet es, führt **In QuickRun öffnen** direkt auf QuickRuns eigene Seite auf `127.0.0.1` —
   eine gewöhnliche http-Adresse, also kein URL-Schema, keine Handler-Registrierung und kein
   Berechtigungsdialog dazwischen.
4. QuickRun zeigt den Plan: die Befehle, der Ref, der aufgelöste Commit. Gestartet wird erst, wenn
   dort bestätigt wird.
5. Antwortet nichts, folgt der Button stattdessen `quickrun://run?repo=…` — das startet QuickRun,
   wenn es installiert ist — und daneben steht der Download für den Fall, dass es das nicht ist.

## Warum das Badge nicht direkt auf `quickrun://` zeigt

Es kann nicht. GitHub entfernt beim Rendern eines README alle Link-Schemata, die es nicht kennt —
ein `quickrun://`-Link im README ist also gar kein Link. Und ein Browser verrät einer Seite nicht,
ob ein Schema einen Handler hat; das wäre ein Fingerprinting-Vektor. Genau deshalb ist die
https-Seite dazwischen auch das, was Schritt 5 überhaupt möglich macht.

## Was der Link mitbringen darf

`repo`, `ref` und `pr`. Sonst überlebt nichts: kein Befehl, keine Config, kein Token, kein lokaler
Pfad. Der Link sagt, was angeschaut wird, nie was ausgeführt wird — siehe
[Sicherheit](/de/security).

`repo` darf `owner/name` oder eine `https://`-URL sein. `ssh://`, `file://` und
`git@host:owner/name` werden aus einem Link abgelehnt; selbst tippen darfst du sie weiterhin auf der
[CLI](/de/cli).

## Das Badge-Bild

`https://fgilde.github.io/QuickRun/badge.svg` — 150×20, die übliche Badge-Form, damit es neben denen
sitzt, die dein README schon hat. Direkt verlinken ist in Ordnung; es liegt auf derselben
GitHub-Pages-Seite wie diese hier.
