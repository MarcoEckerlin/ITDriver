using ITDriver.Core;
using Xunit;

namespace ITDriver.Core.Tests;

/// <summary>
/// Devices from the reference PC (Gigabyte X870 AORUS ELITE WIFI7 ICE, Ryzen 9 7900X3D, Radeon RX 7900 XT, Windows 11)
/// to keep recommendations useful and free of noise on real hardware.
/// </summary>
public class ReferencePcTests
{
    private static DeviceInfo D(string name, string cls, string hwid, string ver, string? date, string provider, string inf, int error = 0) => new()
    {
        Name = name, Class = cls, InstanceId = hwid + "\\1", HardwareIds = new[] { hwid }, DriverVersion = ver,
        DriverDate = date is null ? null : DateTime.Parse(date), DriverProvider = provider, InfName = inf, ErrorCode = error,
    };

    private static readonly SystemSummary System = new()
    {
        OS = "Microsoft Windows 11 Pro", Manufacturer = "Gigabyte Technology Co., Ltd.", Model = "X870 AORUS ELITE WIFI7 ICE",
        Board = "Gigabyte Technology Co., Ltd. X870 AORUS ELITE WIFI7 ICE", Cpu = "AMD Ryzen 9 7900X3D 12-Core Processor",
    };

    private static List<DeviceInfo> Devices() => new()
    {
        D("AMD Radeon RX 7900 XT", "Display", @"PCI\VEN_1002&DEV_744C&SUBSYS_2423148C&REV_CC", "32.0.32029.2010", "2026-11-18", "Advanced Micro Devices, Inc.", "oem123.inf"),
        D("AMD Radeon(TM) Graphics", "Display", @"PCI\VEN_1002&DEV_164E&SUBSYS_D0001458&REV_CA", "32.0.21045.29003", "2026-11-17", "Advanced Micro Devices, Inc.", "oem10.inf"),
        D("Meta Virtual Monitor", "Display", @"Root\MetaVirtualScreenDriver", "5.3.57.114", "2025-06-11", "Meta Inc.", "oem116.inf"),
        D("Realtek PCIe 2.5GbE Family Controller", "Net", @"PCI\VEN_10EC&DEV_8125&SUBSYS_E0001458&REV_0C", "1125.29.50.202", "2026-02-02", "Realtek", "oem143.inf"),
        D("MediaTek Wi-Fi 7 MT7925 Wireless LAN Card", "Net", @"PCI\VEN_14C3&DEV_7925&SUBSYS_E112105B&REV_00", "5.7.0.4669", "2025-09-27", "MediaTek, Inc.", "oem58.inf"),
        D("TeamViewer VPN Adapter", "Net", "TEAMVIEWERVPN", "9.24.6.601", "2020-08-10", "TeamViewer Germany GmbH", "oem156.inf", error: 22),
        D("Tailscale Tunnel", "Net", "Wintun", "0.14.0.0", "2021-10-13", "WireGuard LLC", "oem79.inf"),
        D("MediaTek Bluetooth Adapter", "Bluetooth", @"USB\VID_0489&PID_E124&REV_0100&MI_00", "1.1044.0.556", "2025-09-17", "Mediatek Inc.", "oem59.inf"),
        D("Realtek(R) Audio", "MEDIA", @"HDAUDIO\FUNC_01&VEN_10EC&DEV_1220&SUBSYS_1458A0C3&REV_1001", "6.0.9927.1", "2025-12-16", "Realtek Semiconductor Corp.", "oem35.inf"),
        D("Steam Streaming Microphone", "MEDIA", @"ROOT\SteamStreamingMicrophone", "8.33.15.17", "2017-07-28", "Valve Corporation", "oem13.inf"),
        D("Bigscreen Audio Stream 1.2", "MEDIA", "*BSVAD", "1.0.0.2", "2019-07-02", "Bigscreen", "oem16.inf"),
        D("Standard NVM Express Controller", "SCSIAdapter", @"PCI\VEN_C0A9&DEV_5415&SUBSYS_2B00C0A9&REV_01", "10.0.26100.9974", null, "Microsoft", "stornvme.inf"),
        D("AMD I2C Controller", "System", @"ACPI\VEN_AMDI&DEV_0010", "1.2.0.133", "2026-09-09", "Advanced Micro Devices, Inc.", "oem152.inf"),
        D("ITE USB Connector Client Device", "USB", @"ACPI\VEN_ITE&DEV_8800", "1.3.8.18", "2024-10-07", "ITE", "oem61.inf"),
        D("Nefarius Virtual Gamepad Emulation Bus", "System", @"Nefarius\ViGEmBus\Gen1", "1.21.442.0", "2022-08-30", "Nefarius Software Solutions e.U.", "oem86.inf"),
        D("LIGHTSPEED Receiver", "USB", @"USB\VID_046D&PID_C547&REV_0402", "1.1.91.2790", "2026-06-17", "Logitech", "oem32.inf"),
        D("HyperX Cloud III Wireless", "MEDIA", @"USB\VID_03F0&PID_05B7&REV_4109&MI_00", "10.0.26100.9974", "2026-08-11", "Microsoft", "wdma_usb.inf"),
    };

    [Theory]
    [InlineData("AMD Radeon RX 7900 XT", "AMD")]
    [InlineData("AMD I2C Controller", "AMD")]
    [InlineData("ITE USB Connector Client Device", "ITE")]
    [InlineData("MediaTek Bluetooth Adapter", "Foxconn / Hon Hai (WLAN-/Bluetooth-Modul)")]
    [InlineData("Standard NVM Express Controller", "Crucial / Micron")]
    [InlineData("LIGHTSPEED Receiver", "Logitech")]
    [InlineData("Realtek(R) Audio", "Realtek")]
    public void Recognizes_vendors_on_reference_pc(string device, string vendor)
        => Assert.Equal(vendor, Devices().Single(d => d.Name == device).Vendor?.Name);

    [Theory]
    [InlineData("Meta Virtual Monitor", true)]
    [InlineData("Steam Streaming Microphone", true)]
    [InlineData("Bigscreen Audio Stream 1.2", true)]
    [InlineData("Tailscale Tunnel", true)]
    [InlineData("Nefarius Virtual Gamepad Emulation Bus", true)]
    [InlineData("AMD Radeon RX 7900 XT", false)]
    [InlineData("AMD I2C Controller", false)]
    public void Detects_virtual_devices(string device, bool isVirtual)
        => Assert.Equal(isVirtual, Devices().Single(d => d.Name == device).IsVirtual);

    [Fact]
    public void Recommendations_are_free_of_noise()
    {
        var recs = RecommendationEngine.Build(Devices(), System, new List<DriverUpdate>());
        // Disabled VPN adapter (code 22), fresh AMD GPU drivers and old virtual audio/VPN/gamepad drivers must not be flagged.
        Assert.DoesNotContain(recs, r => r.DeviceName is "TeamViewer VPN Adapter" or "Steam Streaming Microphone" or "Bigscreen Audio Stream 1.2"
                                                         or "Tailscale Tunnel" or "Nefarius Virtual Gamepad Emulation Bus" or "AMD Radeon RX 7900 XT");
        Assert.DoesNotContain(recs, r => r.Severity == Severity.Important);
        // The mainboard manufacturer's support page is suggested.
        Assert.Contains(recs, r => r.Title == "Support-Seite von Gigabyte" && r.Url!.Contains("gigabyte.com"));
    }

    [Fact]
    public void Inbox_placeholder_date_is_unknown()
    {
        Assert.Null(HardwareScanner.NormalizeDriverDate(new DateTime(2006, 6, 21)));
        Assert.Equal(new DateTime(2024, 1, 1), HardwareScanner.NormalizeDriverDate(new DateTime(2024, 1, 1)));
    }

    [Fact]
    public void Problem_count_ignores_disabled_devices()
    {
        var scan = new ScanResult { Devices = Devices() };
        Assert.Equal(0, scan.ProblemCount);
    }
}
