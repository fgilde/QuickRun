# Beispiele

Jede Datei hier liegt in [`samples/`](https://github.com/fgilde/QuickRun/tree/main/samples) und wird
in CI gegen alle drei Plattformen validiert — nichts auf dieser Seite kann von der Engine
abweichen.

## Kleinste nützliche Config

<<< @/../samples/npm-dev.yml{yaml}

## .NET-Webanwendung

<<< @/../samples/dotnet-web.yml{yaml}

## Python mit virtueller Umgebung

Zeigt, wie Platform-Maps mit dem Layout-Unterschied der venv zwischen Windows und dem Rest umgehen.

<<< @/../samples/python-venv.yml{yaml}

## Mehrere Dienste gleichzeitig

Postgres, eine .NET-API und ein Vite-Frontend, in Abhängigkeitsreihenfolge gestartet und beim
Stoppen aufgeräumt.

<<< @/../samples/multi-service.yml{yaml}

## Eine bestehende Compose-Datei einpacken

<<< @/../samples/docker-compose.yml{yaml}

## Ein generiertes Eingabeformular

Erforderliches Secret mit validiertem Muster, eine Zahl mit Bereich, ein Dropdown und ein Schalter.

<<< @/../samples/inputs-and-secrets.yml{yaml}

## Ein Skript pro Plattform

<<< @/../samples/platform-scripts.yml{yaml}

## Ein Repository, das sein SDK mitbringt

Nichts zu `require`, weil das Setup installiert, was es braucht.

<<< @/../samples/install-dotnet-then-run.yml{yaml}

## QuickRuns eigene Config

QuickRun startet sich selbst; das macht der Extension-Button auf seinem Repository.

<<< @/../quickrun.yml{yaml}
