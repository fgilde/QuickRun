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
- **Windows** zeigt beim ersten Start eine SmartScreen-Warnung, und ein frisch veröffentlichter
  Download wird gelegentlich direkt mit **„Virus gefunden"** abgelehnt. Das ist die
  Machine-Learning-Heuristik von Defender — das Urteil lautet `Trojan:Script/Wacatac.B!ml`, wobei
  `!ml` „sieht nach etwas aus" heißt und nicht „ist bekanntermaßen etwas". Reagiert wird auf die
  Form: ein 140 MB großes, sich selbst entpackendes Binary, heruntergeladen Minuten nach dem Bau,
  von niemandem signiert. Dieselbe Datei, lokal gebaut, wird sauber gescannt, und ältere Releases
  werden nicht mehr gemeldet, sobald genug Leute sie ausgeführt haben.

  Was bei einem blockierten Download hilft:

  1. Die Datei gegen die `SHA256SUMS` des Releases prüfen — stimmt der Hash, hast du exakt das,
     was der Build erzeugt hat:
     ```powershell
     Get-FileHash .\quickrun-win-x64.zip -Algorithm SHA256
     ```
  2. Über `winget` oder `scoop` installieren; die laufen nicht über den Download-Pfad des Browsers.
  3. Oder einen Tag warten: solche Urteile verfallen, sobald die Datei nicht mehr neu ist.

- **Linux** interessiert es nicht.

Die eigentliche Abhilfe ist eine Signatur — sie trägt Reputation von einem Release zum nächsten,
statt dass jede Version bei null anfängt. Der Release-Workflow signiert Windows-Builds, sobald die
Signatur-Zugangsdaten hinterlegt sind; bis dahin sind die Prüfsummen oben der Beleg, dass ein
Download die Datei ist, die er zu sein behauptet.

## Die Browser-Erweiterung

Die Erweiterung setzt einen Run-Button auf GitHub. Sie ist nicht erforderlich — die Anwendung
funktioniert allein — aber sie ist der Grund, aus dem QuickRun existiert.

<ExtensionCards lang="de" />

Bis die Store-Einträge live sind, entpackt laden. Download entzippen, dann:

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
