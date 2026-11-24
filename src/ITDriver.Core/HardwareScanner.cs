using System.Management;
using System.Runtime.Versioning;

namespace ITDriver.Core;

/// <summary>Reads all PnP devices and their bound drivers via WMI (read-only).</summary>
[SupportedOSPlatform("windows")]
public sealed class HardwareScanner
{
    public List<DeviceInfo> ScanDevices()
    {
        var drivers = new Dictionary<string, ManagementBaseObject>(StringComparer.OrdinalIgnoreCase);
        using (var s = new ManagementObjectSearcher("SELECT DeviceID, DriverVersion, DriverDate, DriverProviderName, InfName, IsSigned FROM Win32_PnPSignedDriver"))
        {
            foreach (var o in s.Get())
                if (o["DeviceID"] is string id && !drivers.ContainsKey(id)) drivers[id] = o;
        }

        var list = new List<DeviceInfo>();
        using (var s = new ManagementObjectSearcher("SELECT Name, PNPClass, Manufacturer, PNPDeviceID, HardwareID, CompatibleID, Status, ConfigManagerErrorCode, Present FROM Win32_PnPEntity"))
        {
            foreach (var o in s.Get())
            {
                var id = o["PNPDeviceID"] as string ?? "";
                drivers.TryGetValue(id, out var d);
                list.Add(new DeviceInfo
                {
                    Name = o["Name"] as string ?? id,
                    Class = o["PNPClass"] as string,
                    Manufacturer = o["Manufacturer"] as string,
                    InstanceId = id,
                    HardwareIds = o["HardwareID"] as string[] ?? Array.Empty<string>(),
                    CompatibleIds = o["CompatibleID"] as string[] ?? Array.Empty<string>(),
                    Status = o["Status"] as string,
                    ErrorCode = Convert.ToInt32(o["ConfigManagerErrorCode"] ?? 0),
                    Present = o["Present"] as bool? ?? true,
                    DriverVersion = d?["DriverVersion"] as string,
                    DriverDate = ParseCim(d?["DriverDate"] as string),
                    DriverProvider = d?["DriverProviderName"] as string,
                    InfName = d?["InfName"] as string,
                    IsSigned = d?["IsSigned"] as bool?,
                });
            }
        }
        return list.OrderBy(x => x.Class).ThenBy(x => x.Name).ToList();
    }

    public SystemSummary ScanSystem()
    {
        var sum = new SystemSummary();
        Query("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem", o =>
        {
            sum.OS = o["Caption"] as string ?? "";
            sum.OSBuild = $"{o["Version"]}";
        });
        Query("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem", o =>
        {
            sum.Manufacturer = (o["Manufacturer"] as string ?? "").Trim();
            sum.Model = (o["Model"] as string ?? "").Trim();
            sum.RamGB = Math.Round(Convert.ToDouble(o["TotalPhysicalMemory"] ?? 0) / (1L << 30), 1);
        });
        Query("SELECT Manufacturer, Product FROM Win32_BaseBoard", o => sum.Board = $"{o["Manufacturer"]} {o["Product"]}".Trim());
        Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS", o =>
        {
            var date = ParseCim(o["ReleaseDate"] as string);
            sum.Bios = $"{o["SMBIOSBIOSVersion"]}{(date is { } d ? $" ({d:dd.MM.yyyy})" : "")}";
        });
        Query("SELECT Name FROM Win32_Processor", o => sum.Cpu = (o["Name"] as string ?? "").Trim());
        Query("SELECT Name FROM Win32_VideoController", o => sum.Gpus.Add(o["Name"] as string ?? ""));
        return sum;
    }

    private static void Query(string wql, Action<ManagementBaseObject> each)
    {
        try
        {
            using var s = new ManagementObjectSearcher(wql);
            foreach (var o in s.Get()) each(o);
        }
        catch (Exception ex) { Log.Write($"WMI-Abfrage fehlgeschlagen ({wql}): {ex.Message}"); }
    }

    internal static DateTime? ParseCim(string? cim)
    {
        if (string.IsNullOrWhiteSpace(cim) || cim.Length < 8) return null;
        return DateTime.TryParseExact(cim[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
    }
}
