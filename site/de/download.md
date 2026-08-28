# Download

Alles hier zeigt immer auf das neueste Release. In keinem Link steht eine Version, ein Lesezeichen
veraltet also nie.

## Die Anwendung

<DownloadHero lang="de" />

Archiv entpacken und `quickrun` an eine Stelle im `PATH` legen. Ohne Argumente starten — oder
doppelklicken — und QuickRun legt ein Icon ins Tray und öffnet sein Fenster. Es geht kein
Konsolenfenster auf: die Binary ist eine Desktop-Anwendung, die zusätzlich als
Kommandozeilenwerkzeug funktioniert, wenn du sie aus einem Terminal startest.

`winget install fgilde.QuickRun` funktioniert, sobald das Paket ins winget-Repository aufgenommen
ist; die Einreichung ist [in Prüfung](https://github.com/microsoft/winget-pkgs/pulls?q=fgilde.QuickRun).
Bis dahin scoop nehmen, das aus dem Manifest dieser Seite installiert.

Homebrew-Formula und scoop-Manifest werden mit jedem Release neu erzeugt und von hier ausgeliefert —
ein eigenes Tap- oder Bucket-Repository braucht keines davon.

### macOS: das Cask, nicht die Formula

```bash
brew install --cask fgilde/tap/quickrun
```

Das ist die Installation, die ein Mac erwartet. Sie legt **QuickRun.app** in `/Applications` — damit
erscheint es mit Icon in Launchpad und Spotlight, kann das `quickrun://`-Schema beanspruchen (das
steckt in der `Info.plist` eines App-Bundles und ist für eine nackte Binary nicht zu haben), und die
Binary im Bundle wird in den `PATH` verlinkt: `quickrun` im Terminal und die App sind dieselbe
Installation.

Die Formula installiert nur die Kommandozeile:

```bash
brew install fgilde/tap/quickrun   # kein App-Bundle, kein Launchpad-Eintrag, kein quickrun://
```

Beide werden auch von dieser Seite ausgeliefert, das Tap braucht es also nicht:

```bash
brew install --cask https://fgilde.github.io/QuickRun/quickrun-cask.rb
brew install https://fgilde.github.io/QuickRun/quickrun.rb
```

In beiden Fällen aktualisiert `brew upgrade quickrun`, und QuickRun lässt seine eigene Binary in
Ruhe, weil Homebrew sie besitzt.

Das Bundle gibt es auch als normalen Download, falls Homebrew nicht in Frage kommt:

- [QuickRun-osx-arm64.app.zip](https://github.com/fgilde/QuickRun/releases/latest/download/QuickRun-osx-arm64.app.zip) — Apple Silicon
- [QuickRun-osx-x64.app.zip](https://github.com/fgilde/QuickRun/releases/latest/download/QuickRun-osx-x64.app.zip) — Intel

Von Hand geladen trägt es das Quarantäne-Flag, und macOS nennt es beschädigt. Einmal entfernen:

```bash
xattr -dr com.apple.quarantine /Applications/QuickRun.app
```

### Download prüfen

Jedes Release veröffentlicht
[SHA256SUMS](https://github.com/fgilde/QuickRun/releases/latest/download/SHA256SUMS) für jede
Binary. Der Linux-Installer prüft das automatisch; `quickrun update` prüft es, bevor es etwas
ersetzt.

```bash
sha256sum -c --ignore-missing SHA256SUMS
```

### Unsignierte Binaries

QuickRun ist noch nicht code-signiert:

- **macOS** verweigert die Ausführung eines heruntergeladenen unsignierten Binaries. Homebrew
  entfernt das Quarantäne-Attribut, deshalb ist es der empfohlene Weg; `install.sh` entfernt es
  ebenfalls. Bei manuellem Download selbst entfernen:
  ```bash
  xattr -d com.apple.quarantine ./quickrun
  ```
- **Windows** zeigt beim ersten Start eine SmartScreen-Warnung, weil das Binary noch nicht
  signiert ist. Installationen über `scoop` oder `winget` umgehen das.

  Ein Release, v0.8.3, ging weiter: die veröffentlichte Zip-Datei wurde von Browsern mit „Virus
  gefunden" abgelehnt, weil Defenders Machine-Learning-Modell sie `Trojan:Script/Wacatac.B!ml`
  nannte — `!ml` ist eine Vermutung anhand der Form, kein Treffer gegen etwas Bekanntes. Sie war
  falsch, und sie betraf genau diese eine Datei: derselbe Quellcode lokal gebaut wurde sauber
  gescannt, und derselbe Quellcode erneut in der CI gebaut ließ sich sauber herunterladen. Jeder
  Windows-Build wird jetzt auf dem Build-Rechner geprüft, bevor er veröffentlicht wird — ein
  Release, das abgelehnt würde, scheitert dort statt bei dir.

  Falls doch einmal ein Download blockiert wird: das Erhaltene gegen die `SHA256SUMS` des Releases
  prüfen,
  ```powershell
  Get-FileHash .\quickrun-win-x64.zip -Algorithm SHA256
  ```
  und über `winget` oder `scoop` installieren — die laufen nicht über den Download-Pfad des Browsers.

- **Linux** interessiert es nicht.

Die dauerhafte Antwort ist eine Signatur; der Release-Workflow signiert Windows-Builds, sobald die
Signatur-Zugangsdaten hinterlegt sind. Bis dahin sind die Prüfsummen oben der Beleg, dass ein
Download die Datei ist, die er zu sein behauptet.

## Die Browser-Erweiterung

Die Erweiterung setzt einen Run-Button auf GitHub. Sie ist nicht erforderlich — die Anwendung
funktioniert allein — aber sie ist der Grund, aus dem QuickRun existiert.

<ExtensionCards lang="de" />

**Edge** installiert sie aus dem [Microsoft-Edge-Add-ons-Store](https://microsoftedge.microsoft.com/addons/detail/quickrun/dbnknhijahmiildfabckibabpieobnhd),
Updates kommen dann mit denen des Browsers.

Wo ein Store sie noch nicht führt, entpackt laden. Download entzippen, dann:

- **Chrome, Edge, Opera** — `chrome://extensions` → Entwicklermodus → Entpackte Erweiterung laden →
  der entpackte Ordner
- **Firefox** — `about:debugging` → Dieser Firefox → Temporäres Add-on laden → die `manifest.json`
  darin

Das war es: es gibt nichts zu koppeln. QuickRun nimmt nur Anfragen von einer Browser-Erweiterung an.

## Wie es weitergeht

- [Erster Start](/de/install)
- [Config-Referenz](/de/config) — um das eigene Repository startbar zu machen
- [Wie die Erweiterung funktioniert](/de/extension)
- [Sicherheitsmodell](/de/security)
