# Repositories ohne Config

Die meisten Repositories haben nie von QuickRun gehört. Fehlt eine `quickrun.yml`, ermittelt
QuickRun selbst, wie das Repository startet — in dieser Reihenfolge:

1. ein Run-Skript im Wurzelverzeichnis — `quickrun.ps1`, `quickrun.sh`, `run.ps1`, `run.sh`
2. **QuickRuns eigene Sammlung** — eine Config, die für dieses Repository bereitliegt, [siehe unten](#die-sammlung)
3. **die Config eines anderen Launchers**, derzeit [Pinokio](#pinokio)
4. **Erkennung** anhand der vorhandenen Dateien

Vor allen vier steht alles, was spezifischer ist: zuerst die `quickrun.yml` des Repositories selbst,
dann eine Config, die du für dieses Repository im Config-Builder gespeichert hast. Weder die Sammlung
noch die Erkennung kommen jemals davor.

## Die Sammlung

Manche Repositories werden nie eine `quickrun.yml` committen, und QuickRuns Lesen ihrer Dateien ist
eine Vermutung — eine gute, aber eine Vermutung. Deshalb hält QuickRun Configs für bekannte
Repositories bereit und nutzt eine davon, wenn das Repository selbst keine mitbringt.
[Alle ansehen](/de/collection), oder direkt im
[configs-Verzeichnis](https://github.com/fgilde/QuickRun/tree/main/configs) des Repositories.

Die Suche ist eine einzige Anfrage für das gestartete Repository:

```
acme/app  →  https://quickrun.org/configs/acme/app.yml
```

Die Antwort wird einen Tag lang in QuickRuns eigenem Verzeichnis zwischengespeichert, und eine
zwischengespeicherte Config wird auch genutzt, wenn kein Netz erreichbar ist — sie wurde ja für
dieses Repository geschrieben.

Der Plan nennt die Herkunft, im Fenster und im Bestätigungsfenster: *QuickRuns gesammelte Config für
dieses Repository*. Eine fehlerhafte gesammelte Config wird mit einem Hinweis übersprungen statt den
Lauf scheitern zu lassen, und die Erkennung übernimmt.

**Was das kostet, und wie man es abschaltet.** Die Anfrage verrät quickrun.org, welches Repository
du startest. Sie passiert nur für ein Repository, das nichts eigenes mitbringt, und niemals für einen
Ordner auf deinem Rechner. Um nie zu fragen:

```bash
QUICKRUN_NO_COLLECTION=1
```

Damit verhält sich QuickRun genau so, wie es sich vor der Sammlung verhalten hat.

Zum Beitragen: eine `configs/<owner>/<repo>.yml` anlegen, mit einem `repository:`-Feld, das genau
dieses Repository nennt. Die Testsuite parst und validiert jede Config in diesem Verzeichnis, eine
fehlerhafte kann also nicht gemergt werden.

Was dabei herauskommt, ist ein Plan wie jeder andere: die Befehlsliste wird angezeigt, es läuft
nichts ohne Bestätigung, und das Log-Fenster nennt in der ersten Zeile, woher der Plan stammt.

Das Ergebnis ansehen, ohne etwas zu starten:

```bash
quickrun detect            # im Repository
quickrun detect ./mein-repo --save   # in eine quickrun.yml schreiben
```

`--save` überschreibt niemals eine vorhandene Datei. Es ist der schnellste Weg, aus einer erkannten
oder fremden Config eine zu machen, die man bearbeiten und committen kann.

### Eine gesammelte Config ausdrücklich anfordern

Automatisch gewinnt die `quickrun.yml` des Repositories — genau darum geht es in der Reihenfolge
oben, und daran ändert sich nichts. Eine Config lässt sich aber ausdrücklich anfordern, und dann ist
sie es, die läuft:

```bash
quickrun run acme/app --from-collection
```

Genau das macht der Knopf **Mit dieser Config** auf [der Sammlungsseite](/de/collection). Eine Karte
für ein Repository, das eine eigene Config mitbringt, bietet beides an — denn die Karte zeigt eine
davon, und ein Klick auf Starten muss das starten, was man gerade angesehen hat.

Der Link nennt die *Quelle*, niemals die Befehle: `#run?repo=acme/app&config=collection` sagt
QuickRun, es solle die Config selbst holen. Ein Link, der Befehle tragen könnte, wäre ein Link, der
jemandem Befehle vorlegen kann — und das bleibt unmöglich.

Ausdrücklich angefordert, obwohl für dieses Repository nichts bereitliegt, ist ein Fehler und kein
stilles Zurückfallen auf die Config des Repositories — sonst würde ein Knopf mit der Aufschrift „Mit
dieser Config" eine andere starten.

## Pinokio

Eine [Pinokio](https://pinokio.co)-App liefert eine `pinokio.js` neben `install.js` und `start.js`,
deren exportiertes `run`-Array eine Liste von `{ method, params }`-Schritten ist. QuickRun liest
diese Skripte und übersetzt sie — ein Pinokio-Repository läuft damit von der GitHub-Seite aus wie
jedes andere, ohne installiertes Pinokio.

::: v-pre
| Pinokio | Wird zu |
|---|---|
| `install.js` / `install.json` | `setup`-Schritte |
| `start.js` / `start.json` | die Tasks |
| `shell.run` `message` | der Befehl, eine Shell pro Schritt |
| `shell.run` `path` | `cwd` |
| `shell.run` `venv` | ein `python -m venv`-Schritt, danach die Aktivierung vor jedem Befehl |
| `shell.run` `env` | das `env` des Tasks |
| `on: [{ event, done: true }]` | `readyWhen.log`, ein JavaScript-Flag `/…/i` bleibt erhalten |
| `local.set` `url` | die Adresse, die QuickRun öffnet, sobald der Task bereit ist |
| `when` | wird ausgewertet; ein Schritt mit falscher Bedingung entfällt |
| `script.start` | das genannte Skript wird eingelesen, seine `params` werden `{{args}}` |
| `fs.download` | ein `curl -L -o`-Schritt |
| `git`, `python`, `uv`, `node` in einem Befehl | eine Tool-Voraussetzung, vor dem Start geprüft |

`{{ … }}`-Templates werden ausgewertet, inklusive der Ternaries, mit denen diese Skripte ihren
Befehl wählen:
`{{platform === 'win32' && gpu === 'amd' ? 'python main.py --directml' : 'python main.py'}}`.
Verfügbar sind `platform`, `arch`, `cwd`, `gpu`, `args` und `local`.
:::

### Was QuickRun nicht tut

Pinokio-Skripte können JavaScript-Funktionen statt Literale sein — `module.exports = async (kernel)
=> { … }`. Solche Skripte fragen Pinokios eigene Laufzeit nach einem freien Port oder der GPU-Liste
der Maschine, und QuickRun kann sie nicht lesen. Es sagt das, statt zu raten: ist das
**Start**-Skript eine Funktion, gibt es nichts zu starten und die Erkennung übernimmt; ist nur das
**Install**-Skript eine, startet die App trotzdem, und das Log nennt die übersprungene Installation.

Dasselbe gilt für eine `when`-Bedingung, die QuickRun nicht auswerten kann, für das gemeinsame
Modell-Laufwerk von `fs.link` und für Schritte, die `sudo` verlangen — jeder davon entfällt und wird
im Log gezählt. `conda` ist Pinokios eigene mitgelieferte Umgebung; ein Skript, das sie braucht,
bekommt einen Hinweis statt eines kaputten Laufs.

### Welcher Beschleuniger

Skripte verzweigen über `gpu`. QuickRun entscheidet, ohne etwas auszuführen: `nvidia`, wenn
`nvidia-smi` im `PATH` liegt, `apple` unter macOS, sonst unbekannt — das ist die CPU-Variante und
funktioniert immer. Wer es besser weiß, überschreibt es:

```bash
QUICKRUN_GPU=amd quickrun run https://github.com/pinokiofactory/comfy
```

## Erkennung

Sagt nichts anderes, wie das Repository startet, sieht QuickRun sich an, was darin liegt. Jeder
Kandidat wird mit dem Befehl angezeigt, den er ausführen würde; der höchstbewertete wird verwendet,
die übrigen werden aufgelistet, damit man mit `--config` oder einer committeten `quickrun.yml` einen
anderen wählen kann.

| Merkmal | Befehl | Adresse |
|---|---|---|
| `quickrun.sh`, `run.sh`, `run.ps1` | das Skript | — |
| `docker-compose.yml` | `docker compose up` | der erste veröffentlichte Port |
| `package.json` mit `dev`, `start` oder `serve` | `npm run …` (`pnpm`, `yarn`, `bun`, wenn ein Lockfile das sagt) | `--port` im Skript, sonst der Standard des Frameworks |
| `.csproj` (`Microsoft.NET.Sdk.Web`) | `dotnet run --project …` | `launchSettings.json`, sonst fest auf 5000 |
| `.csproj` (Aspire, `OutputType Exe`) | `dotnet run --project …` | — |
| `.csproj` (WPF, WinForms, Avalonia, `WinExe`) | `dotnet run --project …` | keine — es wartet auf das Fenster |
| `manage.py` | `python manage.py runserver` | 8000 |
| `app.py` mit `streamlit` | `python -m streamlit run app.py` | 8501 |
| `app.py`/`main.py` mit `gradio` | `python app.py` | 7860 |
| `requirements.txt`, `pyproject.toml`, `uv.lock` | ein `.venv`, `uv run` oder `poetry run` | fastapi 8000, flask 5000 |
| `Procfile` | alle Prozesse, der `web`-Prozess zuerst, `$PORT` fest auf 8080 | 8080 |
| `.replit` | die `run =`-Zeile | ein dort genannter `--port` |
| `Makefile`, `Taskfile.yml`, `justfile` | `make run`, `task dev`, `just run` | — |
| `Cargo.toml`, `go.mod`, `pom.xml`, `build.gradle` | `cargo run`, `go run ./...`, `mvn spring-boot:run`, `./gradlew bootRun` | Spring 8080 |

Ein Test- oder Benchmark-Projekt wird nie als etwas zum Starten angeboten.

Eine Desktop-Anwendung hat keine Adresse, auf die man warten kann, deshalb bekommt eine erkannte
[`readyWhen: {window: true}`](/de/config#readywhen): der Lauf gilt als bereit, wenn das Fenster da
ist, holt es in den Vordergrund und zeigt die Prozess-ID. Ohne das meldet `dotnet run` Erfolg,
während die Anwendung noch baut und vom Fenster nichts zu sehen ist.

Die Adresse ist es, die einen erkannten Lauf brauchbar macht: sie wird zu einer
`readyWhen`-Prüfung und einem `open`. QuickRun wartet damit auf die Anwendung und liefert den Link,
statt einen im Log suchen zu lassen. Ein geratener Port, der falsch ist, kostet einen
Readiness-Timeout, nicht den Lauf — und gibt die Anwendung ihre Adresse selbst aus, greift das
Log-Fenster auch die auf.
