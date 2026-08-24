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

### macOS-App-Bundle

macOS registriert das `quickrun://`-Schema über ein App-Bundle, was eine nackte Binary nicht ist.
Wenn die Browser-Erweiterung QuickRun starten können soll, wenn es nicht läuft, nimm das Bundle:

- [QuickRun-osx-arm64.app.zip](https://github.com/fgilde/QuickRun/releases/latest/download/QuickRun-osx-arm64.app.zip) — Apple Silicon
- [QuickRun-osx-x64.app.zip](https://github.com/fgilde/QuickRun/releases/latest/download/QuickRun-osx-x64.app.zip) — Intel

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
- **Windows** zeigt beim ersten Start eine SmartScreen-Warnung. Installationen über `scoop` oder
  `winget` umgehen das.
- **Linux** interessiert es nicht.

Signatur-Zertifikate kosten jährlich Geld und bringen nichts, solange es keine Nutzer zu schützen
gibt — deshalb warten sie.

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
