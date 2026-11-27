using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ITDriver.Core;

/// <summary>A driver package listed on an official vendor support page or API.</summary>
public sealed record VendorPackage(string Vendor, string Category, string Title, string Version, DateTime? Released, string Url, long SizeBytes = 0);

/// <summary>
/// Checks the official support sources of AMD (chipset software, Radeon Adrenalin) and mainboard makers for newer
/// driver packages than installed. ITDriver only reads version lists and links to the vendor's own pages –
/// packages are never re-hosted. Sources that block automated requests (Intel, Gigabyte, MSI) are covered by the
/// Microsoft Update Catalog plus direct links to the board's support page.
/// </summary>
public sealed partial class VendorUpdateService
{
    private readonly HttpClient _http;

    public VendorUpdateService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(30) };
        // Vendor sites reject clients without a browser-like user agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36 ITDriver/" + SelfUpdater.CurrentVersion);
        _http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
    }

    // ---------------------------------------------------------------- AMD chipset

    private static readonly HashSet<string> Am5 = new(StringComparer.OrdinalIgnoreCase) { "A620", "B650", "B650E", "X670", "X670E", "B840", "B850", "X870", "X870E" };
    private static readonly HashSet<string> Am4 = new(StringComparer.OrdinalIgnoreCase) { "A320", "B350", "X370", "B450", "X470", "A520", "B550", "X570" };

    [GeneratedRegex(@"(?<![A-Z0-9])([ABX]\d{3}E?)(?![0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex ChipsetRegex();

    /// <summary>AMD chipset of the board, e.g. "X870" from "X870 AORUS ELITE WIFI7 ICE", or null for non-AMD boards.</summary>
    public static string? AmdChipset(SystemSummary system)
    {
        if (!system.Cpu.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return null;
        var product = string.IsNullOrWhiteSpace(system.BoardProduct) ? system.Board : system.BoardProduct;
        foreach (Match m in ChipsetRegex().Matches(product))
        {
            var c = m.Groups[1].Value.ToUpperInvariant();
            if (Am5.Contains(c) || Am4.Contains(c)) return c;
        }
        return null;
    }

    public static string? AmdChipsetPage(string? chipset)
    {
        if (chipset is null) return null;
        var socket = Am5.Contains(chipset) ? "am5" : Am4.Contains(chipset) ? "am4" : null;
        return socket is null ? null : $"https://www.amd.com/en/support/downloads/drivers.html/chipsets/{socket}/{chipset.ToLowerInvariant()}.html";
    }

    [GeneratedRegex(@"amd_chipset_software_(\d+(?:\.\d+){2,3})\.exe", RegexOptions.IgnoreCase)]
    private static partial Regex AmdChipsetVersionRegex();

    internal static string? ParseAmdChipsetVersion(string html)
        => AmdChipsetVersionRegex().Matches(html).Select(m => m.Groups[1].Value).OrderByDescending(VersionUtil.Parse).FirstOrDefault();

    // ---------------------------------------------------------------- AMD Radeon

    [GeneratedRegex(@"Radeon(?:\(TM\)|™)?\s+RX\s+(\d)(\d{3})\s*(XTX|XT|GRE|S)?", RegexOptions.IgnoreCase)]
    private static partial Regex RadeonRegex();

    /// <summary>AMD driver page of a discrete Radeon RX card, e.g. ".../radeon-rx-7000-series/amd-radeon-rx-7900-xt.html".</summary>
    public static string? RadeonPage(string gpuName)
    {
        var m = RadeonRegex().Match(gpuName);
        if (!m.Success || m.Groups[1].Value is not ("5" or "6" or "7" or "9")) return null;
        var model = m.Groups[1].Value + m.Groups[2].Value;
        var suffix = m.Groups[3].Success ? "-" + m.Groups[3].Value.ToLowerInvariant() : "";
        return $"https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-{m.Groups[1].Value}000-series/amd-radeon-rx-{model}{suffix}.html";
    }

    [GeneratedRegex(@"adrenalin-edition-(\d+\.\d+\.\d+)-win1[01]", RegexOptions.IgnoreCase)]
    private static partial Regex AdrenalinRegex();

    internal static string? ParseAdrenalinVersion(string html)
        => AdrenalinRegex().Matches(html).Select(m => m.Groups[1].Value).OrderByDescending(VersionUtil.Parse).FirstOrDefault();

    /// <summary>Build date of the current Adrenalin release from its installer name ("…-minimalsetup-261118_web.exe" = 18.11.2026).</summary>
    internal static DateTime? ParseAdrenalinBuildDate(string html)
    {
        var dates = Regex.Matches(html, @"minimalsetup-(\d{6})_web", RegexOptions.IgnoreCase)
            .Select(m => DateTime.TryParseExact(m.Groups[1].Value, "yyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : (DateTime?)null)
            .Where(d => d != null).ToList();
        return dates.Count == 0 ? null : dates.Max();
    }

    /// <summary>
    /// Is the Adrenalin release on AMD's page newer than what's installed? Uses the installed Adrenalin version when known,
    /// otherwise the GPU driver date (AMD drivers are dated with their build day).
    /// </summary>
    internal static bool AdrenalinIsNewer(string latest, DateTime? latestBuild, string? installedVersion, DateTime? installedDriverDate)
    {
        if (installedVersion != null && Regex.IsMatch(installedVersion, @"^\d{2}\.\d{1,2}\.\d{1,2}$"))
            return VersionUtil.Parse(latest) > VersionUtil.Parse(installedVersion);
        return latestBuild != null && installedDriverDate != null && installedDriverDate.Value.Date < latestBuild.Value.Date.AddDays(-3);
    }

    // ---------------------------------------------------------------- ASUS mainboards

    public static string AsusDriversApi(string model, int osId = 52)
        => $"https://www.asus.com/support/api/product.asmx/GetPDDrivers?website=global&model={Uri.EscapeDataString(model)}&pdhashedid=&osid={osId}";

    internal static List<VendorPackage> ParseAsusDrivers(string json)
    {
        var list = new List<VendorPackage>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Result", out var result) || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("Obj", out var groups) || groups.ValueKind != JsonValueKind.Array) return list;
        foreach (var g in groups.EnumerateArray())
        {
            var category = g.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
            if (!g.TryGetProperty("Files", out var files) || files.ValueKind != JsonValueKind.Array) continue;
            foreach (var f in files.EnumerateArray())
            {
                string S(string p) => f.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var url = f.TryGetProperty("DownloadUrl", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("Global", out var gl) ? gl.GetString() ?? "" : "";
                DateTime? released = DateTime.TryParseExact(S("ReleaseDate"), "yyyy/MM/dd", null, System.Globalization.DateTimeStyles.None, out var r) ? r : null;
                list.Add(new VendorPackage("ASUS", category, S("Title"), S("Version"), released, url, ParseSize(S("FileSize"))));
            }
        }
        return list;
    }

    private static long ParseSize(string text)
    {
        var m = Regex.Match(text, @"([\d.]+)\s*(KB|MB|GB)", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return 0;
        return (long)(v * (m.Groups[2].Value.ToUpperInvariant() switch { "KB" => 1024, "MB" => 1 << 20, _ => 1 << 30 }));
    }

    /// <summary>Chip makers that appear in mainboard driver titles, with the names Windows uses for them.</summary>
    private static readonly (string[] TitleWords, string[] DeviceWords)[] ChipMakers =
    {
        (new[] { "Realtek" }, new[] { "Realtek" }),
        (new[] { "Intel" }, new[] { "Intel" }),
        (new[] { "MTK", "MediaTek" }, new[] { "MediaTek", "Mediatek" }),
        (new[] { "QCA", "Qualcomm", "Killer" }, new[] { "Qualcomm", "Killer" }),
        (new[] { "Marvell", "Aquantia" }, new[] { "Marvell", "Aquantia" }),
    };

    /// <summary>
    /// Matches board packages to installed devices (LAN, WLAN, Bluetooth, audio) by category and chip maker and
    /// returns the newest package per device when it is newer than the installed driver.
    /// </summary>
    public static List<DriverUpdate> MatchBoardPackages(IEnumerable<VendorPackage> packages, IReadOnlyList<DeviceInfo> devices, string supportUrl)
    {
        var offers = new List<DriverUpdate>();
        var physical = devices.Where(d => d.Present && !d.IsVirtual).ToList();
        foreach (var group in packages.Where(p => !string.IsNullOrWhiteSpace(p.Version)).GroupBy(p => (p.Category, Maker: MakerOf(p.Title))))
        {
            if (group.Key.Maker is null) continue;
            var best = group.OrderByDescending(p => VersionUtil.Parse(p.Version)).ThenByDescending(p => p.Released).First();
            var device = physical.FirstOrDefault(d => FitsCategory(d, group.Key.Category) && DeviceWords(group.Key.Maker).Any(w =>
                d.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || (d.DriverProvider?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false)));
            if (device is null || string.IsNullOrEmpty(device.DriverVersion)) continue;
            var installed = VersionUtil.Parse(device.DriverVersion);
            var offered = VersionUtil.Parse(best.Version);
            // Same numbering scheme only (e.g. Realtek 1125.x vs 1126.x), never a jump between unrelated version lines.
            if (offered <= installed || offered.Major < installed.Major || offered.Major - installed.Major > 5) continue;
            offers.Add(new DriverUpdate
            {
                UpdateId = "vendor:" + best.Url,
                Title = best.Title,
                Source = UpdateSource.Vendor,
                Provider = best.Vendor,
                DriverClass = device.Class,
                DriverModel = device.Name,
                DeviceInstanceId = device.InstanceId,
                DeviceName = device.Name,
                InstalledVersion = device.DriverVersion,
                InstalledDate = device.DriverDate,
                InstalledInf = device.InfName,
                NewVersion = best.Version,
                DriverDate = best.Released,
                SizeBytes = best.SizeBytes,
                InfoUrl = supportUrl,
                IsSelected = false,
            });
        }
        return offers;
    }

    private static string? MakerOf(string title)
        => ChipMakers.FirstOrDefault(m => m.TitleWords.Any(w => Regex.IsMatch(title, $@"\b{w}\b", RegexOptions.IgnoreCase))).TitleWords?[0];

    private static string[] DeviceWords(string maker) => ChipMakers.First(m => m.TitleWords[0] == maker).DeviceWords;

    private static bool FitsCategory(DeviceInfo d, string category)
    {
        bool wireless = Regex.IsMatch(d.Name, "Wi-?Fi|Wireless|WLAN|802\\.11", RegexOptions.IgnoreCase);
        return category.ToUpperInvariant() switch
        {
            "LAN" => d.Class == "Net" && !wireless,
            "WIRELESS" or "WLAN" or "WIFI" => d.Class == "Net" && wireless,
            "BLUETOOTH" => d.Class == "Bluetooth" && !d.Name.Contains("Enumerator", StringComparison.OrdinalIgnoreCase) && !d.Name.Contains("Device (", StringComparison.OrdinalIgnoreCase),
            "AUDIO" => d.Class == "MEDIA" && (d.HardwareIds.FirstOrDefault()?.StartsWith("HDAUDIO", StringComparison.OrdinalIgnoreCase) ?? false),
            _ => false,
        };
    }

    // ---------------------------------------------------------------- Mainboard support pages

    /// <summary>Direct link to the board's driver downloads at its manufacturer (search page where the exact URL isn't predictable).</summary>
    public static string? BoardSupportUrl(SystemSummary system)
    {
        var maker = string.IsNullOrWhiteSpace(system.BoardManufacturer) ? system.Board : system.BoardManufacturer;
        var product = system.BoardProduct.Trim();
        if (string.IsNullOrWhiteSpace(product)) return null;
        var q = Uri.EscapeDataString(Regex.Replace(product, @"\s*\(MS-\w+\)\s*$", "").Trim());
        if (maker.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
            return $"https://www.asus.com/supportonly/{q}/helpdesk_download/";
        if (maker.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase))
            return $"https://www.gigabyte.com/Search?kw={q}";
        if (maker.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(maker, @"\bMSI\b"))
            return $"https://www.msi.com/search/{q}";
        if (maker.Contains("ASRock", StringComparison.OrdinalIgnoreCase))
            return $"https://www.asrock.com/support/index.asp?cat=Drivers&Model={q}";
        return null;
    }

    // ---------------------------------------------------------------- Online check

    /// <summary>Installed software versions (DisplayName → DisplayVersion) used for package-level checks.</summary>
    public sealed record InstalledSoftware(string? AmdChipsetVersion, string? AdrenalinVersion);

    public async Task<List<DriverUpdate>> FindUpdatesAsync(SystemSummary system, IReadOnlyList<DeviceInfo> devices, InstalledSoftware installed, IProgress<string>? progress, CancellationToken ct = default)
    {
        var offers = new List<DriverUpdate>();
        var tasks = new List<Task>();
        var gate = new object();
        void Add(DriverUpdate u) { lock (gate) offers.Add(u); }

        // AMD chipset software (drivers for GPIO, I2C, PSP, SMBus, power plans, 3D V-Cache optimizer …)
        var chipset = AmdChipset(system);
        if (AmdChipsetPage(chipset) is { } chipsetPage)
            tasks.Add(Safe($"AMD-Chipsatz {chipset}", async () =>
            {
                var latest = ParseAmdChipsetVersion(await _http.GetStringAsync(chipsetPage, ct));
                if (latest != null && (installed.AmdChipsetVersion is null || VersionUtil.Parse(latest) > VersionUtil.Parse(installed.AmdChipsetVersion)))
                    Add(Package("AMD", $"AMD Chipset Software {latest}", $"AMD Chipsatztreiber ({chipset})", "System", installed.AmdChipsetVersion, latest, chipsetPage));
            }));

        // AMD Radeon Adrenalin
        var radeon = devices.FirstOrDefault(d => d.Present && d.Class == "Display" && RadeonPage(d.Name) != null);
        if (radeon != null && RadeonPage(radeon.Name) is { } gpuPage)
            tasks.Add(Safe(radeon.Name, async () =>
            {
                var html = await _http.GetStringAsync(gpuPage, ct);
                var latest = ParseAdrenalinVersion(html);
                if (latest != null && AdrenalinIsNewer(latest, ParseAdrenalinBuildDate(html), installed.AdrenalinVersion, radeon.DriverDate))
                    Add(Package("AMD", $"AMD Software: Adrenalin Edition {latest}", radeon.Name, "Display",
                        installed.AdrenalinVersion ?? $"Treiber {radeon.DriverVersion}", latest, gpuPage));
            }));

        // ASUS mainboards: official driver list per model
        var maker = system.BoardManufacturer + " " + system.Board;
        if (maker.Contains("ASUS", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(system.BoardProduct))
            tasks.Add(Safe($"ASUS {system.BoardProduct}", async () =>
            {
                var packages = ParseAsusDrivers(await _http.GetStringAsync(AsusDriversApi(system.BoardProduct), ct));
                foreach (var o in MatchBoardPackages(packages, devices, BoardSupportUrl(system) ?? "https://www.asus.com/support/download-center/")) Add(o);
            }));

        progress?.Report("Prüfe Herstellerseiten (AMD, Mainboard) …");
        await Task.WhenAll(tasks);
        return offers;
    }

    private static DriverUpdate Package(string vendor, string title, string deviceName, string cls, string? installedVersion, string newVersion, string url) => new()
    {
        UpdateId = "vendor:" + title,
        Title = title,
        Source = UpdateSource.Vendor,
        Provider = vendor,
        DriverClass = cls,
        DriverModel = deviceName,
        DeviceName = deviceName,
        InstalledVersion = installedVersion,
        NewVersion = newVersion,
        InfoUrl = url,
        IsSelected = false,
    };

    private static async Task Safe(string what, Func<Task> work)
    {
        try { await work(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Write($"Herstellerprüfung {what} fehlgeschlagen: {ex.Message}"); }
    }

    /// <summary>Reads installed AMD software versions from the registry (uninstall entries and the AMD driver key).</summary>
    [SupportedOSPlatform("windows")]
    public static InstalledSoftware ReadInstalledSoftware()
    {
        string? chipset = null, adrenalin = null;
        try
        {
            foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root);
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var app = key.OpenSubKey(name);
                    var display = app?.GetValue("DisplayName") as string;
                    var version = app?.GetValue("DisplayVersion") as string;
                    if (display is null || version is null) continue;
                    if (display.StartsWith("AMD Chipset Software", StringComparison.OrdinalIgnoreCase)) chipset ??= version;
                    else if (display.Equals("AMD Software", StringComparison.OrdinalIgnoreCase) || display.StartsWith("AMD Software:", StringComparison.OrdinalIgnoreCase)) adrenalin ??= version;
                }
            }
            using var cn = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AMD\CN");
            if (cn?.GetValue("RadeonSoftwareVersion") is string rsv && !string.IsNullOrWhiteSpace(rsv)) adrenalin = rsv;
        }
        catch (Exception ex) { Log.Write("Installierte Software konnte nicht gelesen werden: " + ex.Message); }
        return new InstalledSoftware(chipset, adrenalin);
    }
}
