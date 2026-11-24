using System.Text.RegularExpressions;

namespace ITDriver.Core;

/// <summary>Parsed vendor/device information from a PnP hardware ID such as PCI\VEN_10DE&amp;DEV_2484.</summary>
public sealed partial record HardwareId(string Bus, string VendorId, string? DeviceId, string Raw)
{
    [GeneratedRegex(@"(?:VEN|VID)_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VendorRegex();

    [GeneratedRegex(@"(?:DEV|PID)_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceRegex();

    public static HardwareId? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var slash = raw.IndexOf('\\');
        if (slash <= 0) return null;
        var bus = raw[..slash].ToUpperInvariant();
        var v = VendorRegex().Match(raw);
        if (!v.Success) return null;
        var d = DeviceRegex().Match(raw);
        return new HardwareId(bus, v.Groups[1].Value.ToUpperInvariant(), d.Success ? d.Groups[1].Value.ToUpperInvariant() : null, raw);
    }

    /// <summary>"PCI" or "USB" style namespace used for the vendor lookup (HDAUDIO uses PCI vendor IDs).</summary>
    public string VendorNamespace => Bus switch
    {
        "USB" or "USBSTOR" or "HID" or "BTHENUM" => "USB",
        _ => "PCI",
    };
}

/// <summary>Plain-language explanations for Device Manager problem codes.</summary>
public static class DeviceProblems
{
    public static string Describe(int code) => code switch
    {
        0 => "OK",
        1 => "Gerät ist nicht richtig konfiguriert (Treiber fehlt)",
        3 => "Treiber ist beschädigt oder es fehlt Speicher",
        10 => "Gerät kann nicht gestartet werden",
        12 => "Nicht genügend freie Ressourcen",
        14 => "Neustart erforderlich",
        18 => "Treiber muss neu installiert werden",
        19 => "Registrierungsfehler – Treiber neu installieren",
        21 => "Gerät wird entfernt",
        22 => "Gerät ist deaktiviert",
        24 => "Gerät nicht vorhanden oder Treiber unvollständig",
        28 => "Kein Treiber installiert",
        29 => "Gerät von der Firmware deaktiviert",
        31 => "Treiber kann nicht geladen werden",
        32 => "Treiberdienst ist deaktiviert",
        37 => "Treiber konnte nicht initialisiert werden",
        39 => "Treiber beschädigt oder fehlt",
        43 => "Gerät hat ein Problem gemeldet (Code 43)",
        45 => "Gerät ist nicht angeschlossen",
        48 => "Treiber wurde wegen bekannter Probleme blockiert",
        52 => "Treibersignatur kann nicht überprüft werden",
        _ => $"Problemcode {code}",
    };
}

public static class MicrosoftCatalog
{
    /// <summary>Search link on the official Microsoft Update Catalog for the device's most specific hardware ID.</summary>
    public static string SearchUrl(DeviceInfo device)
    {
        var id = device.HardwareIds.FirstOrDefault() ?? device.Name;
        // The catalog search works best with the "VEN_xxxx&DEV_xxxx" part.
        var parsed = HardwareId.TryParse(id);
        var query = parsed?.DeviceId != null
            ? (parsed.VendorNamespace == "USB" ? $"VID_{parsed.VendorId}&PID_{parsed.DeviceId}" : $"VEN_{parsed.VendorId}&DEV_{parsed.DeviceId}")
            : id;
        return "https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(query);
    }
}
