# ITDriver

**Kostenloser, quelloffener Treiber-Updater für Windows 10 und 11.** Keine Werbung, keine Bezahlschranke, keine Treiber von dubiosen Servern.

ITDriver erkennt deine Hardware, findet fehlende und veraltete Treiber, installiert sie sicher und kann jede Änderung wieder rückgängig machen.

## Funktionen

| Bereich | Was ITDriver macht |
|---|---|
| **Scan** | Liest alle Geräte samt Treiberversion, Datum, Anbieter und Hardware-IDs (WMI). Erkennt Geräte ohne Treiber und mit Fehlercodes (Code 28, 43 …) und erklärt sie auf Deutsch. |
| **Updates** | Fragt den offiziellen **Windows-Update-Dienst** nach Treibern für deine Geräte (dieselbe Quelle wie „Optionale Updates“, gespeist aus dem Microsoft Update-Katalog). Download und Installation mit einem Klick, einzeln oder alle. |
| **Hersteller-Empfehlungen** | Kuratierte Liste mit über 60 der gängigsten Hardware-Hersteller (NVIDIA, AMD, Intel, Realtek, Broadcom, Qualcomm, MediaTek, Samsung, WD, Logitech, Razer, Corsair, Drucker, USB-Seriell-Adapter …) und den Support-Tools der PC-Hersteller (Dell, Lenovo, HP, ASUS, MSI, Gigabyte, ASRock, Acer …). Warnt z. B., wenn die Grafikkarte nur mit dem Microsoft-Standardtreiber läuft oder der Grafiktreiber älter als 6 Monate ist. |
| **Sicherheit** | Vor jeder Installation: Wiederherstellungspunkt + Backup des bisherigen Treibers. |
| **Zurücksetzen** | Im Verlauf kann jeder von ITDriver installierte Treiber mit einem Klick auf die vorherige Version zurückgesetzt werden. |
| **Backup** | Komplett-Backup aller Treiber (z. B. vor einer Neuinstallation), Wiederherstellung, Export als ZIP mit eingebautem Wiederherstellungs-Skript (funktioniert auch ohne ITDriver). |
| **Bereinigung** | Findet alte, nicht mehr genutzte Treiberversionen im Treiberspeicher und gibt Speicherplatz frei. Treiber in Benutzung oder startkritische Treiber werden nie entfernt. |
| **Geplanter Scan** | Täglich/wöchentlich/monatlich über die Windows-Aufgabenplanung, mit Benachrichtigung im Infobereich. Installiert nichts automatisch. |
| **Ignorieren** | Einzelne Updates dauerhaft ausblenden. |
| **Berichte** | HTML-Bericht oder JSON-Inventar deines Systems exportieren. |
| **Auto-Update** | ITDriver prüft GitHub Releases auf neue Versionen und aktualisiert sich selbst (SHA-256-geprüft). |
| **Kommandozeile** | `ITDriver.exe --report bericht.html`, `--export-inventory inventar.json [--no-windows-update]`, `--background-scan` |

## Woher kommen die Treiber?

- **Windows Update / Microsoft Update-Katalog** – alle Installationen laufen über die offizielle Windows-Update-Schnittstelle. Die Treiber sind von Microsoft signiert und geprüft.
- **Hersteller** – für Treiber, die Windows Update nicht (aktuell) anbietet, verlinkt ITDriver ausschließlich die offiziellen Download-Seiten bzw. Tools der Hersteller und den Microsoft Update-Katalog mit der passenden Hardware-ID.

ITDriver verteilt selbst **keine** Treiberdateien.

## Installation

1. Unter [Releases](../../releases) die Datei `ITDriver-win-x64.zip` herunterladen und entpacken.
2. `ITDriver.exe` starten (fragt nach Administratorrechten – nötig für Treiberinstallation und Wiederherstellungspunkte).

Es wird keine .NET-Installation benötigt.

## Entwicklung

```
dotnet test tests/ITDriver.Core.Tests
dotnet publish src/ITDriver.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

- `src/ITDriver.Core` – Logik (Scan, Windows Update, Backup, Rollback, Bereinigung, Aufgabenplanung, Self-Update)
- `src/ITDriver.App` – WPF-Oberfläche
- `tools/Get-ITDriverInventory.ps1` – Hardware-Inventar als JSON (nur lesend), z. B. für Fehlerberichte

**Neue Version veröffentlichen:** Tag `vX.Y.Z` pushen. Der Release-Workflow baut, testet, erzeugt `ITDriver-win-x64.zip` + `.sha256` und veröffentlicht das Release. Installierte Apps finden es automatisch.

## Lizenz

MIT
