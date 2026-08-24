# Installation

## Paketmanager

Der empfohlene Weg auf allen Plattformen. Paketmanager halten QuickRun aktuell und vermeiden unter
macOS das unten beschriebene Gatekeeper-Problem.

::: code-group

```powershell [Windows]
winget install fgilde.QuickRun
# oder
scoop install https://fgilde.github.io/QuickRun/quickrun.json
```

```bash [macOS]
brew install fgilde/tap/quickrun
```

```bash [Linux]
curl -fsSL https://fgilde.github.io/QuickRun/install.sh | sh
```

:::

Der Linux-Installer lädt das Release-Asset für deine Architektur, prüft es gegen die veröffentlichte
`SHA256SUMS` und installiert nach `~/.local/bin`. `PREFIX` setzen, um woanders zu installieren.

## Direkter Download

<DownloadButtons lang="de" />

Archiv entpacken und `quickrun` an eine Stelle im `PATH` legen.

### Unsignierte Binaries

QuickRun ist noch nicht code-signiert. Was das pro Plattform bedeutet:

- **macOS** verweigert die Ausführung eines heruntergeladenen unsignierten Binaries komplett.
  Entweder Homebrew benutzen, das das Quarantäne-Attribut entfernt, oder es selbst entfernen:
  ```bash
  xattr -d com.apple.quarantine ./quickrun
  ```
- **Windows** zeigt beim ersten Start eine SmartScreen-Warnung. Installationen über `winget` oder
  `scoop` umgehen das.
- **Linux** interessiert es nicht.

Signatur-Zertifikate kosten jährlich Geld und bringen nichts, solange es keine Nutzer zu schützen
gibt — deshalb warten sie. Die Release-Pipeline ist so gebaut, dass ein Signing-Schritt später
genau einen Job berührt.

## Erster Start

```bash
quickrun install     # quickrun:// registrieren und Daemon beim Anmelden starten
quickrun daemon      # oder den Listener im Vordergrund laufen lassen
quickrun pair        # danach in der Browser-Erweiterung auf Pair klicken
```

`quickrun install` registriert das `quickrun://`-Schema — darüber kann die Erweiterung einen
installierten, aber nicht laufenden Daemon starten.

## Browser-Erweiterung

Die Erweiterung setzt den Run-Button auf GitHub. Sie ist nicht erforderlich — die CLI funktioniert
allein — aber sie ist der Grund, aus dem QuickRun existiert.

| Browser | Wo |
|---|---|
| Chrome | Chrome Web Store *(in Prüfung)* |
| Edge | Edge Add-ons *(in Prüfung)* |
| Firefox | Firefox Add-ons *(in Prüfung)* |
| Opera | Chrome-Build über Operas Chrome-Extension-Unterstützung installieren |

Bis die Store-Einträge live sind, entpackt laden:

```bash
git clone https://github.com/fgilde/QuickRun
cd QuickRun/extension
sh build.sh
```

Dann in Chrome oder Edge: `chrome://extensions` → Entwicklermodus → Entpackte Erweiterung laden →
`extension/dist/chromium`. In Firefox: `about:debugging` → Dieser Firefox → Temporäres Add-on laden →
`extension/dist/firefox/manifest.json`.

## Aktualisieren

```bash
quickrun update          # installiert, wenn QuickRun das Binary besitzt
quickrun update --check  # meldet nur
```

QuickRun leitet aus dem Installationspfad ab, wer das Binary verwaltet. Hat ein Paketmanager es
dort abgelegt, meldet `update` die Version und den passenden Befehl statt die Datei zu
überschreiben — zwei Updater, die um dieselbe Datei kämpfen, sind der Anfang von Versionschaos.

## Deinstallieren

```bash
quickrun clean --all   # zuerst die ausgecheckten Workspaces entfernen
quickrun uninstall     # quickrun:// und den Autostart-Eintrag abmelden
```

Danach das Binary entfernen, oder `winget uninstall fgilde.QuickRun` /
`brew uninstall quickrun` / `scoop uninstall quickrun`.
