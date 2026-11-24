using ITDriver.Core;
using Xunit;

namespace ITDriver.Core.Tests;

public class HardwareIdTests
{
    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_2484&SUBSYS_146B10DE&REV_A1", "PCI", "10DE", "2484", "PCI")]
    [InlineData(@"USB\VID_046D&PID_C52B&REV_2410", "USB", "046D", "C52B", "USB")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897&SUBSYS_14629D79", "HDAUDIO", "10EC", "0897", "PCI")]
    [InlineData(@"pci\ven_8086&dev_a370", "PCI", "8086", "A370", "PCI")]
    public void Parses_vendor_and_device(string raw, string bus, string ven, string dev, string ns)
    {
        var id = HardwareId.TryParse(raw)!;
        Assert.Equal(bus, id.Bus);
        Assert.Equal(ven, id.VendorId);
        Assert.Equal(dev, id.DeviceId);
        Assert.Equal(ns, id.VendorNamespace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"ACPI\PNP0A08")]
    [InlineData("ROOT_HUB30")]
    public void Returns_null_without_vendor(string? raw) => Assert.Null(HardwareId.TryParse(raw));
}

public class VendorCatalogTests
{
    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_2484", "NVIDIA")]
    [InlineData(@"PCI\VEN_1002&DEV_73BF", "AMD")]
    [InlineData(@"PCI\VEN_8086&DEV_A370", "Intel")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897", "Realtek")]
    [InlineData(@"USB\VID_046D&PID_C52B", "Logitech")]
    public void Finds_common_vendors(string hwid, string vendor)
    {
        var d = new DeviceInfo { HardwareIds = new[] { hwid } };
        Assert.Equal(vendor, d.Vendor?.Name);
    }

    [Fact]
    public void Usb_and_pci_namespaces_are_separate()
    {
        // 0x1038 is SteelSeries on USB, but not a known PCI vendor here.
        Assert.Equal("SteelSeries", VendorCatalog.Find(HardwareId.TryParse(@"USB\VID_1038&PID_1234")!)?.Name);
        Assert.Null(VendorCatalog.Find(HardwareId.TryParse(@"PCI\VEN_1038&DEV_1234")!));
    }

    [Theory]
    [InlineData("Dell Inc.", "Dell")]
    [InlineData("LENOVO", "Lenovo")]
    [InlineData("HP", "HP")]
    [InlineData("Micro-Star International Co., Ltd.", "MSI")]
    [InlineData("ASUSTeK COMPUTER INC.", "ASUS")]
    [InlineData("To Be Filled By O.E.M.", null)]
    public void Finds_oem(string manufacturer, string? expected) => Assert.Equal(expected, VendorCatalog.FindOem(manufacturer)?.Name);

    [Fact]
    public void Catalog_contains_many_vendors() => Assert.True(VendorCatalog.Count >= 50);
}

public class VersionTests
{
    [Theory]
    [InlineData("31.0.101.4502", "31.0.101.4502")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta", "1.2.3")]
    [InlineData("7", "7.0")]
    [InlineData(null, "0.0")]
    public void Parses_leniently(string? text, string expected) => Assert.Equal(Version.Parse(expected), VersionUtil.Parse(text));

    [Fact]
    public void Release_comparison()
    {
        var rel = new AppRelease(VersionUtil.Parse("v1.1.0"), "v1.1.0", "", "", null, null, "");
        Assert.True(SelfUpdater.IsNewer(rel, new Version(1, 0, 0, 0)));
        Assert.False(SelfUpdater.IsNewer(rel, new Version(1, 1, 0)));
    }
}

public class ParserTests
{
    [Fact]
    public void Parses_get_windowsdriver_json_array_and_single()
    {
        var json = """
        [{"Driver":"oem12.inf","OriginalFileName":"C:\\Windows\\System32\\DriverStore\\FileRepository\\nv_dispi.inf_amd64_abc\\nv_dispi.inf","ProviderName":"NVIDIA","ClassName":"Display","Version":"32.0.15.6094","Date":"2024-08-14","BootCritical":false,"Inbox":false},
         {"Driver":"oem3.inf","OriginalFileName":"C:\\x\\rt.inf","ProviderName":"Realtek","ClassName":"Net","Version":"10.1.0.0","Date":null,"BootCritical":true,"Inbox":false}]
        """;
        var list = DriverStoreService.ParseWindowsDriverJson(json);
        Assert.Equal(2, list.Count);
        Assert.Equal("nv_dispi.inf", list[0].InfFileName);
        Assert.Equal(@"C:\Windows\System32\DriverStore\FileRepository\nv_dispi.inf_amd64_abc", list[0].RepositoryFolder);
        Assert.Equal(new DateTime(2024, 8, 14), list[0].Date);
        Assert.True(list[1].BootCritical);

        var single = DriverStoreService.ParseWindowsDriverJson("""{"Driver":"oem1.inf","OriginalFileName":"a\\b.inf"}""");
        Assert.Single(single);
    }

    [Fact]
    public void Parses_github_release()
    {
        var json = """
        {"tag_name":"v1.2.0","name":"ITDriver 1.2.0","draft":false,"body":"Neu","html_url":"https://github.com/x",
         "assets":[{"name":"ITDriver-win-x64.zip","browser_download_url":"https://e/zip"},{"name":"ITDriver-win-x64.zip.sha256","browser_download_url":"https://e/sha"}]}
        """;
        var r = SelfUpdater.ParseRelease(json)!;
        Assert.Equal(new Version(1, 2, 0), r.Version);
        Assert.Equal("https://e/zip", r.ZipUrl);
        Assert.Equal("https://e/sha", r.ShaUrl);
    }

    [Fact]
    public void Parses_sha_file() => Assert.Equal("ABC123", SelfUpdater.ParseSha256("ABC123  ITDriver-win-x64.zip\n"));
}

public class CleanupTests
{
    private static DriverPackage P(string oem, string inf, string ver, string date, bool boot = false) => new()
    {
        PublishedName = oem, OriginalFileName = $@"C:\Repo\{inf}_x\{inf}", ProviderName = "NVIDIA", ClassName = "Display",
        Version = ver, Date = DateTime.Parse(date), BootCritical = boot,
    };

    [Fact]
    public void Finds_old_unused_duplicates_only()
    {
        var pkgs = new[]
        {
            P("oem10.inf", "nv_dispi.inf", "31.0.15.1", "2023-01-01"),
            P("oem11.inf", "nv_dispi.inf", "32.0.15.6094", "2024-08-01"),
            P("oem12.inf", "nv_dispi.inf", "30.0.1.1", "2022-01-01"),
            P("oem13.inf", "nv_dispi.inf", "29.0.1.1", "2021-01-01", boot: true),
            P("oem20.inf", "other.inf", "1.0.0.0", "2020-01-01"),
        };
        var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "oem12.inf", "oem11.inf" };
        var c = DriverStoreCleanup.FindCandidates(pkgs, inUse);
        Assert.Equal(new[] { "oem10.inf" }, c.Select(x => x.Package.PublishedName));
        Assert.Equal("oem11.inf", c[0].Newer.PublishedName);
    }
}

public class MatchingAndRecommendationTests
{
    [Fact]
    public void Matches_update_to_device_by_hardware_id()
    {
        var dev = new DeviceInfo { Name = "Realtek Audio", InstanceId = "HDAUDIO\\1", HardwareIds = new[] { @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897&SUBSYS_14629D79", @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897" }, DriverVersion = "6.0.1.1", InfName = "oem5.inf" };
        var index = WindowsUpdateService.BuildHardwareIdIndex(new[] { dev });
        var upd = new DriverUpdate { HardwareId = @"hdaudio\func_01&ven_10ec&dev_0897" };
        WindowsUpdateService.Match(upd, index);
        Assert.Equal("Realtek Audio", upd.DeviceName);
        Assert.Equal("oem5.inf", upd.InstalledInf);
    }

    [Fact]
    public void Recommends_vendor_driver_for_basic_display_and_missing_driver()
    {
        var devices = new List<DeviceInfo>
        {
            new() { Name = "Microsoft Basic Display Adapter", Class = "Display", InstanceId = "PCI\\1", HardwareIds = new[] { @"PCI\VEN_10DE&DEV_2484" }, DriverProvider = "Microsoft" },
            new() { Name = "Ethernet-Controller", Class = "Net", InstanceId = "PCI\\2", HardwareIds = new[] { @"PCI\VEN_10EC&DEV_8125" }, ErrorCode = 28 },
        };
        var recs = RecommendationEngine.Build(devices, new SystemSummary { Manufacturer = "Micro-Star International Co., Ltd.", Model = "MS-7C02" }, new List<DriverUpdate>());
        Assert.Contains(recs, r => r.Severity == Severity.Important && r.Url!.Contains("nvidia.com"));
        Assert.Contains(recs, r => r.Title.StartsWith("Treiber fehlt") && r.Url!.Contains("realtek"));
        Assert.Contains(recs, r => r.Title.Contains("MSI"));
    }

    [Fact]
    public void Catalog_url_uses_ven_dev()
    {
        var d = new DeviceInfo { HardwareIds = new[] { @"PCI\VEN_8086&DEV_A370&SUBSYS_00748086" } };
        Assert.Equal("https://www.catalog.update.microsoft.com/Search.aspx?q=VEN_8086%26DEV_A370", d.CatalogSearchUrl);
    }
}

public class SchedulerTests
{
    [Fact]
    public void Builds_weekly_task_arguments()
    {
        var s = new AppSettings { Schedule = ScanSchedule.Weekly, ScheduleDay = DayOfWeek.Friday, ScheduleTime = "18:30" };
        var args = SchedulerService.BuildCreateArguments(s, @"C:\Program Files\ITDriver\ITDriver.exe");
        Assert.Contains("/SC WEEKLY /D FRI", args);
        Assert.Contains("/ST 18:30", args);
        Assert.Contains(@"/TR ""\""C:\Program Files\ITDriver\ITDriver.exe\"" --background-scan""", args);
        Assert.Contains("/RL HIGHEST", args);
    }

    [Fact]
    public void Quote_handles_trailing_backslash() => Assert.Equal("\"C:\\Backups\"", ProcessRunner.Quote(@"C:\Backups\"));

    [Fact]
    public void Ignore_list_matches_id_or_title()
    {
        var s = new AppSettings { IgnoredUpdates = { "abc", "Intel - Display - 31.0" } };
        Assert.True(s.IsIgnored(new DriverUpdate { UpdateId = "ABC" }));
        Assert.True(s.IsIgnored(new DriverUpdate { UpdateId = "x", Title = "intel - display - 31.0" }));
        Assert.False(s.IsIgnored(new DriverUpdate { UpdateId = "y", Title = "z" }));
    }
}
