using ITDriver.Core;
using Xunit;

namespace ITDriver.Core.Tests;

public class CatalogTests
{
    // Structure of a catalog.update.microsoft.com search result page (trimmed).
    private const string SearchHtml = """
    <table id="ctl00_catalogBody_updateMatches">
    <tr id="headerRow"><td>Title</td></tr>
    <tr id="0e1b2c3d-1111-2222-3333-444455556666_R1">
      <td class="resultsbottomBorder" id="0e1b2c3d-1111-2222-3333-444455556666_C0_R1"><input type="button" value="Download"/></td>
      <td class="resultsbottomBorder resultspadding" id="0e1b2c3d-1111-2222-3333-444455556666_C1_R1">
        <a id="0e1b2c3d-1111-2222-3333-444455556666_link" href="javascript:void(0);" onclick='goToDetails("0e1b2c3d-1111-2222-3333-444455556666");'>
          Realtek Semiconductor Corp. - Net - 1125.31.725.2025
        </a>
      </td>
      <td class="resultsbottomBorder resultspadding" id="0e1b2c3d-1111-2222-3333-444455556666_C2_R1">Windows 10 and later drivers,Windows 11 Client, version 24H2 and later, Servicing Drivers</td>
      <td class="resultsbottomBorder resultspadding" id="0e1b2c3d-1111-2222-3333-444455556666_C3_R1">Drivers (Networking)</td>
      <td class="resultsbottomBorder resultspadding" id="0e1b2c3d-1111-2222-3333-444455556666_C4_R1">7/25/2025</td>
      <td class="resultsbottomBorder resultspadding" id="0e1b2c3d-1111-2222-3333-444455556666_C5_R1">1125.31.725.2025</td>
      <td class="resultsbottomBorder resultspadding" id="0e1b2c3d-1111-2222-3333-444455556666_C6_R1"><span id="0e1b2c3d-1111-2222-3333-444455556666_size">1.8 MB</span><span style="display:none;" id="0e1b2c3d-1111-2222-3333-444455556666_originalSize">1887436</span></td>
    </tr>
    <tr id="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_R2">
      <td class="resultsbottomBorder resultspadding" id="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_C1_R2">
        <a id="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_link" href="javascript:void(0);" onclick='goToDetails("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");'>Realtek Semiconductor Corp. - Net - 10.50.511.2021</a>
      </td>
      <td class="resultsbottomBorder resultspadding" id="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_C2_R2">Windows 10 and later drivers</td>
      <td class="resultsbottomBorder resultspadding" id="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_C3_R2">Drivers (Networking)</td>
      <td class="resultsbottomBorder resultspadding" id="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_C4_R2">5/11/2021</td>
      <td class="resultsbottomBorder resultspadding" id="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_C5_R2">10.50.511.2021</td>
      <td class="resultsbottomBorder resultspadding" id="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_C6_R2"><span id="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_size">900 KB</span><span style="display:none;" id="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_originalSize">921600</span></td>
    </tr>
    </table>
    """;

    [Fact]
    public void Parses_search_results()
    {
        var list = CatalogService.ParseSearch(SearchHtml);
        Assert.Equal(2, list.Count);
        var e = list[0];
        Assert.Equal("0e1b2c3d-1111-2222-3333-444455556666", e.UpdateId);
        Assert.Equal("Realtek Semiconductor Corp. - Net - 1125.31.725.2025", e.Title);
        Assert.Equal("1125.31.725.2025", e.Version);
        Assert.Equal(new DateTime(2025, 7, 25), e.LastUpdated);
        Assert.Equal(1887436, e.SizeBytes);
        Assert.True(e.IsDriver);
        Assert.True(e.TargetsWindows10Or11);
        Assert.False(e.IsArm);
    }

    [Fact]
    public void Parses_download_dialog()
    {
        const string js = """
        var downloadInformation = new Array();
        downloadInformation[0] = new Object();
        downloadInformation[0].files = new Array();
        downloadInformation[0].files[0] = new Object();
        downloadInformation[0].files[0].url = 'http://catalog.s.download.windowsupdate.com/d/msdownload/update/driver/drvs/2025/07/abc_def.cab';
        downloadInformation[0].files[0].digest = 'xyz=';
        """;
        Assert.Equal(new[] { "https://catalog.s.download.windowsupdate.com/d/msdownload/update/driver/drvs/2025/07/abc_def.cab" }, CatalogService.ParseDownloadUrls(js));
    }

    private static DeviceInfo Dev(string hwid, string? ver, string provider = "Realtek", string inf = "oem143.inf", string cls = "Net", int error = 0) => new()
    {
        Name = "Gerät", Class = cls, InstanceId = hwid + "\\1", HardwareIds = new[] { hwid }, DriverVersion = ver, DriverProvider = provider, InfName = inf, ErrorCode = error,
    };

    [Theory]
    [InlineData(@"PCI\VEN_10EC&DEV_8125&SUBSYS_E0001458&REV_0C", "VEN_10EC&DEV_8125&SUBSYS_E0001458|VEN_10EC&DEV_8125")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_1220&SUBSYS_1458A0C3&REV_1001", "VEN_10EC&DEV_1220&SUBSYS_1458A0C3|VEN_10EC&DEV_1220")]
    [InlineData(@"USB\VID_0489&PID_E124&REV_0100&MI_00", "VID_0489&PID_E124")]
    [InlineData(@"ACPI\VEN_AMDI&DEV_0010", "AMDI0010")]
    [InlineData(@"ROOT\SteamStreamingMicrophone", "")]
    public void Builds_queries(string hwid, string expected)
        => Assert.Equal(expected, string.Join("|", CatalogService.QueriesFor(Dev(hwid, "1.0"))));

    [Fact]
    public void Picks_newer_driver_only()
    {
        var entries = CatalogService.ParseSearch(SearchHtml);
        Assert.Equal("1125.31.725.2025", CatalogService.PickNewer(Dev(@"PCI\VEN_10EC&DEV_8125", "1125.29.50.202"), entries)?.Version);
        Assert.Null(CatalogService.PickNewer(Dev(@"PCI\VEN_10EC&DEV_8125", "1125.31.725.2025"), entries));
        // Missing driver: newest is offered.
        Assert.Equal("1125.31.725.2025", CatalogService.PickNewer(Dev(@"PCI\VEN_10EC&DEV_8125", null, "", "", error: 28), entries)?.Version);
    }

    [Fact]
    public void Generic_microsoft_driver_gets_vendor_driver()
    {
        var entries = CatalogService.ParseSearch(SearchHtml);
        var dev = Dev(@"PCI\VEN_10EC&DEV_8125", "10.0.26100.1", provider: "Microsoft", inf: "net8125.inf");
        Assert.Equal("1125.31.725.2025", CatalogService.PickNewer(dev, entries)?.Version);
        // ...but not for USB/system devices where the class driver is the better choice.
        var usb = Dev(@"USB\VID_03F0&PID_05B7", "10.0.26100.1", provider: "Microsoft", inf: "wdma_usb.inf", cls: "MEDIA");
        Assert.False(CatalogService.UsesGenericDriverWithVendorAlternative(usb));
    }

    [Fact]
    public void Version_from_windows_update_title()
    {
        Assert.Equal("10.69.1004.2024", WindowsUpdateService.VersionFromTitle("Realtek Semiconductor Corp. - Net - 10.69.1004.2024"));
        Assert.Equal("1.0.4.55794", WindowsUpdateService.VersionFromTitle("Microsoft Corporation AudioProcessingObject Driver Update (1.0.4.55794)"));
        Assert.Null(WindowsUpdateService.VersionFromTitle("Intel Display"));
    }

    /// <summary>Live check against catalog.update.microsoft.com; runs only where ITDRIVER_ONLINE_TESTS=1 (CI on Windows).</summary>
    [Fact]
    public async Task Live_catalog_search_and_download_link()
    {
        if (Environment.GetEnvironmentVariable("ITDRIVER_ONLINE_TESTS") != "1") return;
        var svc = new CatalogService();
        var html = await svc.SearchHtmlAsync("VEN_10EC&DEV_8125");
        var results = CatalogService.ParseSearch(html);
        if (results.Count == 0)
        {
            // Show the relevant part of the real page in the CI log to adapt the parser.
            var i = Math.Max(0, html.IndexOf("_R1", StringComparison.Ordinal) - 1500);
            Assert.Fail($"Keine Treffer geparst (HTML {html.Length} Zeichen). Ausschnitt:\n{html.Substring(i, Math.Min(5000, html.Length - i))}");
        }
        Assert.Contains(results, r => r.IsDriver && !string.IsNullOrEmpty(r.Version));
        var urls = await svc.GetDownloadUrlsAsync(results.First(r => r.IsDriver).UpdateId);
        Assert.NotEmpty(urls);
        Assert.All(urls, u => Assert.Contains("windowsupdate.com", u));
    }
}

public class SetupReleaseTests
{
    [Fact]
    public void Parses_setup_assets()
    {
        var json = """
        {"tag_name":"v1.1.0","assets":[
          {"name":"ITDriver-Setup.exe","browser_download_url":"https://e/setup"},
          {"name":"ITDriver-Setup.exe.sha256","browser_download_url":"https://e/setup.sha"},
          {"name":"ITDriver-win-x64.zip","browser_download_url":"https://e/zip"},
          {"name":"ITDriver-win-x64.zip.sha256","browser_download_url":"https://e/zip.sha"}]}
        """;
        var r = SelfUpdater.ParseRelease(json)!;
        Assert.Equal("https://e/setup", r.SetupUrl);
        Assert.Equal("https://e/setup.sha", r.SetupShaUrl);
        Assert.Equal("https://e/zip", r.ZipUrl);
    }
}
