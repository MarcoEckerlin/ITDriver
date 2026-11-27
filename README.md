# ITDriver | AI SLOP

**Kostenloser, quelloffener Treiber-Updater für Windows 10 und 11.** Keine Werbung, keine Bezahlschranke, keine Treiber von dubiosen Servern.

ITDriver erkennt deine Hardware, findet fehlende und veraltete Treiber, installiert sie sicher und kann jede Änderung wieder rückgängig machen.

## Funktionen

| Bereich | Was ITDriver macht |
|---|---|
| **Scan** | Liest alle Geräte samt Treiberversion, Datum, Anbieter und Hardware-IDs (WMI). Erkennt Geräte ohne Treiber und mit Fehlercodes (Code 28, 43 …) und erklärt sie auf Deutsch. |
| **Updates** | Fragt den offiziellen **Windows-Update-Dienst** nach Treibern für deine Geräte und durchsucht zusätzlich den **Microsoft Update-Katalog** pro Gerät nach neueren Versionen und fehlenden Treibern (deutlich mehr Treffer als Windows Update allein). Download direkt von Microsoft, Installation mit einem Klick, einzeln oder alle. |
| **Hersteller-Empfehlungen** | Kuratierte Liste mit über 60 der gängigsten Hardware-Hersteller (NVIDIA, AMD, Intel, Realtek, Broadcom, Qualcomm, MediaTek, Samsung, WD, Logitech, Razer, Corsair, Drucker, USB-Seriell-Adapter …) und den Support-Tools der PC-Hersteller (Dell, Lenovo, HP, ASUS, MSI, Gigabyte, ASRock, Acer …). Warnt z. B., wenn die Grafikkarte nur mit dem Microsoft-Standardtreiber läuft oder der Grafiktreiber älter als 6 Monate ist. |
| **Sicherheit** | Vor jeder Installation: Wiederherstellungspunkt + Backup des bisherigen Treibers. |
| **Zurücksetzen** | Im Verlauf kann jeder von ITDriver installierte Treiber mit einem Klick auf die vorherige Version zurückgesetzt werden. |
| **Backup** | Komplett-Backup aller Treiber (z. B. vor einer Neuinstallation), Wiederherstellung, Export als ZIP mit eingebautem Wiederherstellungs-Skript (funktioniert auch ohne ITDriver). |
| **Bereinigung** | Findet alte, nicht mehr genutzte Treiberversionen im Treiberspeicher und gibt Speicherplatz frei. Treiber in Benutzung oder startkritische Treiber werden nie entfernt. |
| **Geplanter Scan** | Täglich/wöchentlich/monatlich über die Windows-Aufgabenplanung, mit Benachrichtigung im Infobereich. Installiert nichts automatisch. |
| **Ignorieren** | Einzelne Updates dauerhaft ausblenden. |
| **Berichte** | HTML-Bericht oder JSON-Inventar deines Systems exportieren. |
| **Auto-Update** | ITDriver prüft beim Start, alle 6 Stunden und beim geplanten Hintergrund-Scan die GitHub Releases auf neue Versionen. Es fragt einmal pro Version (installieren, später, überspringen) oder installiert auf Wunsch automatisch ohne Nachfrage – immer SHA-256-geprüft, bei installierter Version über den Setup. |
| **Kommandozeile** | `ITDriver.exe --report bericht.html`, `--export-inventory inventar.json [--no-windows-update]`, `--background-scan` |

## Woher kommen die Treiber?

- **Windows Update** – über die offizielle Windows-Update-Schnittstelle.
- **Microsoft Update-Katalog** (catalog.update.microsoft.com) – Suche per Hardware-ID; die Pakete kommen direkt von Microsofts Download-Servern und werden mit `pnputil` installiert. Alle Treiber dort sind von Microsoft (WHQL) signiert.
- **Hersteller** – ITDriver prüft direkt bei AMD die aktuelle **AMD Chipset Software** (passend zum Mainboard-Chipsatz, z. B. X870/B650/B550) und **Radeon Adrenalin**, und bei **ASUS-Mainboards** die offizielle Treiberliste des Boards (LAN, WLAN, Bluetooth, Audio). Diese Pakete lädt ITDriver direkt vom Server des Herstellers (drivers.amd.com, dlcdnets.asus.com) und installiert sie selbst: Die AMD-Installer müssen gültig von AMD signiert sein und laufen still durch, ASUS-Treiberpakete werden per pnputil eingespielt. Nur der Radeon-Treiber öffnet AMDs eigenen Installer. Intel, Gigabyte und MSI blockieren automatische Abfragen – **Intel-Chipsatztreiber** (Chipset INF, MEI, Serial IO, SMBus …), Realtek- und Mainboard-Treiber holt ITDriver deshalb aus dem Microsoft Update-Katalog und installiert sie ebenfalls direkt.
- **ARM-PCs (Snapdragon)** – Auf Windows on ARM sucht ITDriver gezielt ARM64-Treiber im Katalog (inkl. Qualcomm-Chipsatzgeräte), auf x64-PCs werden ARM64-Pakete nie angeboten.
- **Ohne Rückfrage** – Ein Klick auf Installieren startet sofort, alle ausgewählten Updates laufen nacheinander durch (Neustart-Frage einmal am Ende). Wer vorher gefragt werden möchte, schaltet das unter Einstellungen ein.

ITDriver verteilt selbst **keine** Treiberdateien.

## Installation

**Installer (empfohlen):** Unter [Releases](../../releases) `ITDriver-Setup.exe` herunterladen und ausführen. Legt Startmenü- und optional Desktop-Verknüpfung an und lässt sich über „Apps & Features“ deinstallieren. Updates installiert die App danach selbst.

**Portabel:** `ITDriver-win-x64.zip` entpacken und `ITDriver.exe` starten.

ITDriver fragt nach Administratorrechten (nötig für Treiberinstallation und Wiederherstellungspunkte). Es wird keine .NET-Installation benötigt. Die Dateien sind (noch) nicht digital signiert, daher warnt Windows SmartScreen beim ersten Start („Weitere Informationen“ → „Trotzdem ausführen“).

## Entwicklung

```
dotnet test tests/ITDriver.Core.Tests
dotnet publish src/ITDriver.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

- `src/ITDriver.Core` – Logik (Scan, Windows Update, Backup, Rollback, Bereinigung, Aufgabenplanung, Self-Update)
- `src/ITDriver.App` – WPF-Oberfläche
- `installer/ITDriver.iss` – Inno-Setup-Skript für den Installer
- `tools/Get-ITDriverInventory.ps1` – Hardware-Inventar als JSON (nur lesend), z. B. für Fehlerberichte

**Neue Version veröffentlichen:** Tag `vX.Y.Z` pushen oder unter Actions → Release → „Run workflow“ die Version eingeben. Der Release-Workflow baut, testet, erzeugt `ITDriver-win-x64.zip` + `.sha256` und veröffentlicht das Release. Installierte Apps finden es automatisch.

## Lizenz

MIT
