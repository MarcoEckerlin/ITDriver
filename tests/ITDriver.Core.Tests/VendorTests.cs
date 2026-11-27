using ITDriver.Core;
using Xunit;

namespace ITDriver.Core.Tests;

public class VendorTests
{
    private static SystemSummary Sys(string cpu, string maker, string product) => new()
    {
        Cpu = cpu, BoardManufacturer = maker, BoardProduct = product, Board = $"{maker} {product}",
    };

    [Theory]
    [InlineData("X870 AORUS ELITE WIFI7 ICE", "X870")]
    [InlineData("ROG STRIX X870E-E GAMING WIFI", "X870E")]
    [InlineData("TUF GAMING B650-PLUS WIFI", "B650")]
    [InlineData("MAG B550 TOMAHAWK (MS-7C91)", "B550")]
    [InlineData("PRO Z790-A WIFI (MS-7E07)", null)]
    public void Detects_amd_chipset(string product, string? chipset)
        => Assert.Equal(chipset, VendorUpdateService.AmdChipset(Sys("AMD Ryzen 9 7900X3D 12-Core Processor", "X", product)));

    [Fact]
    public void Intel_cpu_has_no_amd_chipset()
        => Assert.Null(VendorUpdateService.AmdChipset(Sys("Intel(R) Core(TM) i7-14700K", "ASUSTeK COMPUTER INC.", "PRIME B650-PLUS")));

    [Theory]
    [InlineData("X870", "https://www.amd.com/en/support/downloads/drivers.html/chipsets/am5/x870.html")]
    [InlineData("X570", "https://www.amd.com/en/support/downloads/drivers.html/chipsets/am4/x570.html")]
    public void Builds_amd_chipset_page(string chipset, string url)
        => Assert.Equal(url, VendorUpdateService.AmdChipsetPage(chipset));

    [Fact]
    public void Parses_amd_chipset_version()
        => Assert.Equal("8.09.28.1124", VendorUpdateService.ParseAmdChipsetVersion(
            """<a href="https://drivers.amd.com/drivers/amd_chipset_software_8.09.28.1124.exe">x</a> <a href="https://drivers.amd.com/drivers/amd_chipset_software_7.01.08.129.exe">""" ));

    [Theory]
    [InlineData("AMD Radeon RX 7900 XT", "radeon-rx-7000-series/amd-radeon-rx-7900-xt.html")]
    [InlineData("AMD Radeon RX 9070 XT", "radeon-rx-9000-series/amd-radeon-rx-9070-xt.html")]
    [InlineData("AMD Radeon(TM) RX 6800 XT", "radeon-rx-6000-series/amd-radeon-rx-6800-xt.html")]
    [InlineData("AMD Radeon RX 7600", "radeon-rx-7000-series/amd-radeon-rx-7600.html")]
    [InlineData("AMD Radeon(TM) Graphics", null)]
    public void Builds_radeon_page(string name, string? tail)
    {
        var url = VendorUpdateService.RadeonPage(name);
        if (tail is null) Assert.Null(url);
        else Assert.EndsWith("/graphics/radeon-rx/" + tail, url);
    }

    private const string AdrenalinHtml = """
        <a href="https://drivers.amd.com/drivers/installer/26.20/whql/amd-software-adrenalin-edition-26.11.2-minimalsetup-261118_web.exe">
        <a href="https://drivers.amd.com/drivers/whql-amd-software-adrenalin-edition-26.11.2-win11-b.exe">
        """;

    [Fact]
    public void Parses_adrenalin_release()
    {
        Assert.Equal("26.11.2", VendorUpdateService.ParseAdrenalinVersion(AdrenalinHtml));
        Assert.Equal(new DateTime(2026, 11, 18), VendorUpdateService.ParseAdrenalinBuildDate(AdrenalinHtml));
    }

    [Theory]
    [InlineData("26.10.1", null, true)]
    [InlineData("26.11.2", null, false)]
    [InlineData(null, "2026-11-18", false)]   // reference PC: driver of 18.11.2026 = current release
    [InlineData(null, "2026-09-02", true)]
    [InlineData("23.20.11.01", "2026-11-18", false)] // internal version number is ignored, date decides
    public void Compares_adrenalin(string? installed, string? driverDate, bool newer)
        => Assert.Equal(newer, VendorUpdateService.AdrenalinIsNewer("26.11.2", new DateTime(2026, 11, 18), installed,
            driverDate is null ? null : DateTime.Parse(driverDate)));

    private const string AsusJson = """
        {"Result":{"Count":4,"Obj":[
          {"Name":"LAN","Count":2,"Files":[
            {"Version":"1125.27.50.919","Title":"Realtek RTL8125 LAN driver v1125.27.50.919 for Windows 11 64-bit.","FileSize":"14.67 MB","ReleaseDate":"2025/12/31","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/ASUS/mb/04LAN/a.zip"}},
            {"Version":"1125.21.903.2024","Title":"Realtek LAN Driver V1125.21.903.2024 for Windows 11 64-bit.","FileSize":"12 MB","ReleaseDate":"2024/10/01","DownloadUrl":{"Global":"https://dlcdnets.asus.com/b.zip"}}]},
          {"Name":"Wireless","Count":1,"Files":[
            {"Version":"5.7.0.6054","Title":"MediaTek MT7925/7927 WiFi Driver v5.7.0.6054 for Windows 11 64-bit.","FileSize":"30 MB","ReleaseDate":"2026/07/06","DownloadUrl":{"Global":"https://dlcdnets.asus.com/c.zip"}}]},
          {"Name":"Audio","Count":1,"Files":[
            {"Version":"6.0.9800.1","Title":"Realtek Audio Driver V6.0.9800.1","FileSize":"500 MB","ReleaseDate":"2025/01/01","DownloadUrl":{"Global":"https://dlcdnets.asus.com/d.zip"}}]}
        ]},"Status":"SUCCESS"}
        """;

    [Fact]
    public void Parses_asus_driver_list()
    {
        var p = VendorUpdateService.ParseAsusDrivers(AsusJson);
        Assert.Equal(4, p.Count);
        Assert.Equal("LAN", p[0].Category);
        Assert.Equal("1125.27.50.919", p[0].Version);
        Assert.Equal(new DateTime(2025, 12, 31), p[0].Released);
        Assert.True(p[0].SizeBytes > 14_000_000);
    }

    [Fact]
    public void Matches_board_packages_to_devices()
    {
        var devices = new List<DeviceInfo>
        {
            new() { Name = "Realtek PCIe 2.5GbE Family Controller", Class = "Net", InstanceId = "lan", HardwareIds = new[] { @"PCI\VEN_10EC&DEV_8125" }, DriverVersion = "1125.20.1.2024", DriverProvider = "Realtek" },
            new() { Name = "MediaTek Wi-Fi 7 MT7925 Wireless LAN Card", Class = "Net", InstanceId = "wlan", HardwareIds = new[] { @"PCI\VEN_14C3&DEV_7925" }, DriverVersion = "5.7.0.4669", DriverProvider = "MediaTek, Inc." },
            new() { Name = "Realtek(R) Audio", Class = "MEDIA", InstanceId = "audio", HardwareIds = new[] { @"HDAUDIO\FUNC_01&VEN_10EC&DEV_1220" }, DriverVersion = "6.0.9927.1", DriverProvider = "Realtek Semiconductor Corp." },
        };
        var offers = VendorUpdateService.MatchBoardPackages(VendorUpdateService.ParseAsusDrivers(AsusJson), devices, "https://board");
        Assert.Equal(2, offers.Count);
        var lan = offers.Single(o => o.DeviceInstanceId == "lan");
        Assert.Equal("1125.27.50.919", lan.NewVersion);
        Assert.Equal(UpdateSource.Vendor, lan.Source);
        Assert.False(lan.IsSelected);
        Assert.Equal("5.7.0.6054", offers.Single(o => o.DeviceInstanceId == "wlan").NewVersion);
        Assert.DoesNotContain(offers, o => o.DeviceInstanceId == "audio"); // installed audio driver is newer than the board package
    }

    [Theory]
    [InlineData("Gigabyte Technology Co., Ltd.", "X870 AORUS ELITE WIFI7 ICE", "https://www.gigabyte.com/Search?kw=X870%20AORUS%20ELITE%20WIFI7%20ICE")]
    [InlineData("ASUSTeK COMPUTER INC.", "ROG STRIX X870E-E GAMING WIFI", "https://www.asus.com/supportonly/ROG%20STRIX%20X870E-E%20GAMING%20WIFI/helpdesk_download/")]
    [InlineData("Micro-Star International Co., Ltd.", "MAG X870 TOMAHAWK WIFI (MS-7E51)", "https://www.msi.com/search/MAG%20X870%20TOMAHAWK%20WIFI")]
    [InlineData("Dell Inc.", "0XYZ12", null)]
    public void Builds_board_support_links(string maker, string product, string? url)
        => Assert.Equal(url, VendorUpdateService.BoardSupportUrl(Sys("AMD", maker, product)));

    [Fact]
    public void Merge_keeps_newest_offer_per_device()
    {
        var wu = new DriverUpdate { Title = "WU", DeviceInstanceId = "a", NewVersion = "6.0.9600.1", Source = UpdateSource.WindowsUpdate };
        var wuOnly = new DriverUpdate { Title = "WU2", DeviceInstanceId = "b", NewVersion = "1.0", Source = UpdateSource.WindowsUpdate };
        var catNewer = new DriverUpdate { Title = "Cat", DeviceInstanceId = "a", NewVersion = "6.0.9800.1", Source = UpdateSource.Catalog };
        var catOlder = new DriverUpdate { Title = "Cat2", DeviceInstanceId = "b", NewVersion = "0.9", Source = UpdateSource.Catalog };
        var package = new DriverUpdate { Title = "AMD Chipset", Source = UpdateSource.Vendor, NewVersion = "8.0" };
        var merged = CatalogService.MergeOffers(new[] { wu, wuOnly }, new[] { catNewer, catOlder, package });
        Assert.Equal(new[] { "WU2", "Cat", "AMD Chipset" }, merged.Select(m => m.Title).OrderBy(t => t == "WU2" ? 0 : t == "Cat" ? 1 : 2));
        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void Amd_system_device_on_inbox_driver_gets_vendor_driver()
    {
        var gpio = new DeviceInfo
        {
            Name = "GPIO-Controller", Class = "System", HardwareIds = new[] { @"ACPI\VEN_AMDI&DEV_0030", @"ACPI\AMDI0030" },
            DriverProvider = "Microsoft", DriverVersion = "10.0.26100.1", InfName = "machine.inf",
        };
        Assert.True(CatalogService.UsesGenericDriverWithVendorAlternative(gpio));
        var entry = new CatalogEntry("x", "Advanced Micro Devices, Inc System Driver Update (2.2.0.137)", "Windows 11 Client, version 24H2 and later, Servicing Drivers", "Drivers (Other Hardware)", new DateTime(2025, 3, 1), "2.2.0.137", 1000);
        Assert.Equal("x", CatalogService.PickNewer(gpio, new[] { entry }, osBuild: 26100)?.UpdateId);
    }

    /// <summary>Live checks of the official vendor sources; run only where ITDRIVER_ONLINE_TESTS=1 (CI).</summary>
    [Fact]
    public async Task Live_vendor_sources()
    {
        if (Environment.GetEnvironmentVariable("ITDRIVER_ONLINE_TESTS") != "1") return;
        using var http = new HttpClient();
        var svc = new VendorUpdateService();
        var sys = Sys("AMD Ryzen 9 7900X3D 12-Core Processor", "ASUSTeK COMPUTER INC.", "TUF GAMING X870-PLUS WIFI");
        var devices = new List<DeviceInfo>
        {
            new() { Name = "AMD Radeon RX 7900 XT", Class = "Display", InstanceId = "gpu", HardwareIds = new[] { @"PCI\VEN_1002&DEV_744C" }, DriverVersion = "32.0.1", DriverDate = new DateTime(2024, 1, 1) },
            new() { Name = "Realtek PCIe 2.5GbE Family Controller", Class = "Net", InstanceId = "lan", HardwareIds = new[] { @"PCI\VEN_10EC&DEV_8125" }, DriverVersion = "1125.1.1.2020", DriverProvider = "Realtek" },
        };
        var offers = await svc.FindUpdatesAsync(sys, devices, new VendorUpdateService.InstalledSoftware("1.0.0.0", null), null);
        Assert.Contains(offers, o => o.Title.StartsWith("AMD Chipset Software"));
        Assert.Contains(offers, o => o.Title.StartsWith("AMD Software: Adrenalin Edition"));
        Assert.Contains(offers, o => o.DeviceInstanceId == "lan" && o.Provider == "ASUS");
    }

    [Fact]
    public async Task Live_full_id_query_finds_newer_realtek_audio()
    {
        if (Environment.GetEnvironmentVariable("ITDRIVER_ONLINE_TESTS") != "1") return;
        // Reference PC: Realtek ALC1220 audio with 6.0.9927.1 installed; the catalog has newer packages under the full ID.
        var dev = new DeviceInfo
        {
            Name = "Realtek(R) Audio", Class = "MEDIA", InstanceId = "audio", Present = true,
            HardwareIds = new[] { @"HDAUDIO\FUNC_01&VEN_10EC&DEV_1220&SUBSYS_1458A0C3&REV_1001" },
            DriverVersion = "6.0.9927.1", DriverProvider = "Realtek Semiconductor Corp.", InfName = "oem35.inf",
        };
        var offers = await new CatalogService().FindUpdatesAsync(new[] { dev }, new HashSet<string>(), null);
        Assert.Single(offers);
        Assert.True(VersionUtil.Parse(offers[0].NewVersion) > VersionUtil.Parse("6.0.9927.1"));
    }
}

public class AutoUpdateTests
{
    private static AppRelease Rel(string v, bool assets = true) => new(new Version(v), "v" + v, "ITDriver " + v, "", assets ? "z" : null, assets ? "zs" : null, "h",
        assets ? "s" : null, assets ? "ss" : null);

    [Fact]
    public void Prompts_for_newer_release()
        => Assert.Equal(SelfUpdater.AutoAction.Prompt, SelfUpdater.Decide(Rel("1.3.0"), new Version(1, 2, 0), new AppSettings()));

    [Fact]
    public void Installs_without_asking_when_enabled()
        => Assert.Equal(SelfUpdater.AutoAction.Install, SelfUpdater.Decide(Rel("1.3.0"), new Version(1, 2, 0), new AppSettings { AutoInstallAppUpdates = true }));

    [Fact]
    public void Ignores_current_and_older()
    {
        Assert.Equal(SelfUpdater.AutoAction.None, SelfUpdater.Decide(Rel("1.2.0"), new Version(1, 2, 0), new AppSettings { AutoInstallAppUpdates = true }));
        Assert.Equal(SelfUpdater.AutoAction.None, SelfUpdater.Decide(null, new Version(1, 2, 0), new AppSettings()));
    }

    [Fact]
    public void Respects_skipped_version_but_not_newer_ones()
    {
        var s = new AppSettings { SkippedAppVersion = "1.3.0" };
        Assert.Equal(SelfUpdater.AutoAction.None, SelfUpdater.Decide(Rel("1.3.0"), new Version(1, 2, 0), s));
        Assert.Equal(SelfUpdater.AutoAction.Prompt, SelfUpdater.Decide(Rel("1.3.1"), new Version(1, 2, 0), s));
    }

    [Fact]
    public void Release_without_packages_is_only_announced()
        => Assert.Equal(SelfUpdater.AutoAction.Prompt, SelfUpdater.Decide(Rel("1.3.0", assets: false), new Version(1, 2, 0), new AppSettings { AutoInstallAppUpdates = true }));
}
