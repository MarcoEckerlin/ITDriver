namespace ITDriver.Core;

public enum Severity { Info, Recommended, Important }

public sealed class Recommendation
{
    public Severity Severity { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? Url { get; set; }
    public string? UrlText { get; set; }
    public string? DeviceName { get; set; }

    public string SeverityText => Severity switch
    {
        Severity.Important => "Wichtig",
        Severity.Recommended => "Empfohlen",
        _ => "Hinweis",
    };
}

/// <summary>
/// Rules that go beyond Windows Update: missing drivers, generic Microsoft drivers on GPUs, very old drivers,
/// and pointers to the official vendor / PC manufacturer tools for the most common hardware.
/// </summary>
public static class RecommendationEngine
{
    private static readonly HashSet<string> RelevantClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Display", "Net", "MEDIA", "Bluetooth", "SCSIAdapter", "HDC", "USB", "System", "Camera", "Image", "Biometric", "Printer", "AudioEndpoint", "Sensor", "Monitor", "Mouse", "Keyboard", "HIDClass",
    };

    public static List<Recommendation> Build(IReadOnlyList<DeviceInfo> devices, SystemSummary? system, IReadOnlyList<DriverUpdate> wuUpdates, int oldDriverYears = 3)
    {
        var list = new List<Recommendation>();
        var updatedDevices = new HashSet<string>(wuUpdates.Select(u => u.DeviceInstanceId ?? ""), StringComparer.OrdinalIgnoreCase);

        foreach (var d in devices.Where(d => d.Present && d.HasProblem && d.ErrorCode != 22 && d.ErrorCode != 45))
        {
            var vendor = d.Vendor;
            list.Add(new Recommendation
            {
                Severity = Severity.Important,
                DeviceName = d.Name,
                Title = d.IsMissingDriver ? $"Treiber fehlt: {d.Name}" : $"Geräteproblem: {d.Name}",
                Detail = d.ProblemText + (updatedDevices.Contains(d.InstanceId)
                    ? " – Windows Update bietet einen passenden Treiber an (siehe Updates)."
                    : vendor != null ? $" – Treiber beim Hersteller {vendor.Name} oder im Microsoft Update-Katalog suchen." : " – im Microsoft Update-Katalog nach der Hardware-ID suchen."),
                Url = updatedDevices.Contains(d.InstanceId) ? null : (vendor?.DownloadUrl ?? d.CatalogSearchUrl),
                UrlText = vendor != null ? $"{vendor.Name} Downloads" : "Update-Katalog",
            });
        }

        // Graphics cards running Microsoft Basic Display Adapter / generic drivers.
        foreach (var d in devices.Where(d => d.Present && string.Equals(d.Class, "Display", StringComparison.OrdinalIgnoreCase)))
        {
            var vendor = d.Vendor;
            if (vendor is null || vendor.IsMicrosoft) continue;
            if (d.UsesGenericMicrosoftDriver || d.Name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new Recommendation
                {
                    Severity = Severity.Important,
                    DeviceName = d.Name,
                    Title = $"Grafikkarte läuft mit Standardtreiber",
                    Detail = $"Für {d.Name} ist nur der Microsoft-Standardtreiber aktiv. Installiere den Treiber von {vendor.Name} für volle Leistung.",
                    Url = vendor.DownloadUrl,
                    UrlText = vendor.Tool ?? $"{vendor.Name} Treiber",
                });
            }
            else if (vendor.Name is "NVIDIA" or "AMD" or "Intel" && d.DriverAgeDays > 180)
            {
                list.Add(new Recommendation
                {
                    Severity = Severity.Recommended,
                    DeviceName = d.Name,
                    Title = $"Grafiktreiber ist {d.DriverAgeDays / 30} Monate alt",
                    Detail = $"{vendor.Name} veröffentlicht regelmäßig neue Grafiktreiber (Leistung, Spiele-Fixes, Sicherheit). Windows Update liefert diese oft verzögert. Installiert: {d.DriverVersion} vom {d.DriverDate:dd.MM.yyyy}.",
                    Url = vendor.DownloadUrl,
                    UrlText = vendor.Tool ?? $"{vendor.Name} Treiber",
                });
            }
        }

        // Very old third-party drivers on relevant device classes.
        var oldLimit = DateTime.Today.AddYears(-Math.Max(1, oldDriverYears));
        foreach (var d in devices.Where(d => d.Present && !d.HasProblem && !d.IsVirtual && d.DriverDate < oldLimit
                                             && RelevantClasses.Contains(d.Class ?? "")
                                             && !string.Equals(d.DriverProvider, "Microsoft", StringComparison.OrdinalIgnoreCase)
                                             && !updatedDevices.Contains(d.InstanceId)
                                             && !string.Equals(d.Class, "Display", StringComparison.OrdinalIgnoreCase)))
        {
            var vendor = d.Vendor;
            list.Add(new Recommendation
            {
                Severity = Severity.Info,
                DeviceName = d.Name,
                Title = $"Alter Treiber: {d.Name}",
                Detail = $"Treiber {d.DriverVersion} vom {d.DriverDate:dd.MM.yyyy} ({d.DriverProvider}). " +
                         (vendor?.Hint ?? "Prüfe beim Hersteller, ob es eine neuere Version gibt."),
                Url = vendor?.DownloadUrl ?? d.CatalogSearchUrl,
                UrlText = vendor != null ? $"{vendor.Name} Downloads" : "Update-Katalog",
            });
        }

        // PC / mainboard manufacturer tool (BIOS, chipset, OEM-specific drivers).
        var oem = VendorCatalog.FindOem(system?.Manufacturer) ?? VendorCatalog.FindOem(system?.Board);
        var boardUrl = system is null ? null : VendorUpdateService.BoardSupportUrl(system);
        if (oem != null && !oem.IsMicrosoft)
        {
            list.Add(new Recommendation
            {
                Severity = Severity.Info,
                Title = $"Support-Seite von {oem.Name}",
                Detail = $"BIOS-/Firmware-Updates und Mainboard-Treiber (Chipsatz, LAN, WLAN, Audio, RGB) für {(string.IsNullOrWhiteSpace(system?.BoardProduct) ? system?.Model ?? system?.Board : system!.BoardProduct)} gibt es beim Hersteller" +
                         (oem.Tool != null ? $" (Tool: {oem.Tool})." : "."),
                Url = boardUrl ?? oem.DownloadUrl,
                UrlText = boardUrl != null ? $"{oem.Name}: Treiber für dieses Board" : $"{oem.Name} Support",
            });
        }

        // AMD/Intel chipset packages: drivers for the system devices that Windows only covers with generic drivers.
        if (system != null)
        {
            var amdChipset = VendorUpdateService.AmdChipset(system);
            if (VendorUpdateService.AmdChipsetPage(amdChipset) is { } page)
                list.Add(new Recommendation
                {
                    Severity = Severity.Info,
                    Title = $"AMD Chipsatztreiber ({amdChipset})",
                    Detail = "Die AMD Chipset Software enthält Treiber für GPIO, I2C, SMBus, PSP und die Energiepläne (bei X3D-Prozessoren auch den 3D V-Cache Optimizer). ITDriver prüft die Version direkt bei AMD.",
                    Url = page,
                    UrlText = "AMD Chipsatz-Downloads",
                });
            else if (system.Cpu.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                list.Add(new Recommendation
                {
                    Severity = Severity.Info,
                    Title = "Intel Chipsatz- und Grafiktreiber",
                    Detail = "Intel verteilt Chipsatz (INF), Management Engine, Serial IO, Grafik, WLAN und Bluetooth über den Intel Driver & Support Assistant. ITDriver findet viele davon zusätzlich im Microsoft Update-Katalog.",
                    Url = "https://www.intel.de/content/www/de/de/support/detect.html",
                    UrlText = "Intel Driver & Support Assistant",
                });
        }

        return list
            .GroupBy(r => r.Title).Select(g => g.First())
            .OrderByDescending(r => r.Severity).ThenBy(r => r.Title)
            .ToList();
    }
}
