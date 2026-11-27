using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace ITDriver.Core;

/// <summary>One search result row of the Microsoft Update Catalog.</summary>
public sealed record CatalogEntry(string UpdateId, string Title, string Products, string Classification, DateTime? LastUpdated, string Version, long SizeBytes)
{
    public bool IsDriver => Classification.Contains("Driver", StringComparison.OrdinalIgnoreCase)
                            || Classification.Contains("Treiber", StringComparison.OrdinalIgnoreCase);

    public bool IsArm => Title.Contains("ARM64", StringComparison.OrdinalIgnoreCase) || Products.Contains("ARM64", StringComparison.OrdinalIgnoreCase);

    /// <summary>Product list targets Windows 10/11 client (not only Server or legacy Windows).</summary>
    public bool TargetsWindows10Or11 =>
        Products.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) || Products.Contains("Windows 11", StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, int> Win11Builds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["21H2"] = 22000, ["22H2"] = 22621, ["23H2"] = 22631, ["24H2"] = 26100, ["25H2"] = 26200, ["26H1"] = 28000, ["26H2"] = 26300,
    };

    /// <summary>Lowest Windows build the package is published for ("Windows 11 Client, version 24H2 and later" = 26100; Windows 10 = 0).</summary>
    public int MinBuild
    {
        get
        {
            if (Products.Contains("Windows 10", StringComparison.OrdinalIgnoreCase)) return 0;
            var builds = System.Text.RegularExpressions.Regex.Matches(Products, @"version\s+(\d\d[HhAaBb]\d)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Select(m => Win11Builds.TryGetValue(m.Groups[1].Value, out var b) ? b : 0).ToList();
            return builds.Count == 0 ? 0 : builds.Min();
        }
    }
}

/// <summary>
/// Searches the official Microsoft Update Catalog (catalog.update.microsoft.com) by hardware ID. The catalog contains
/// far more (and often newer) driver versions than Windows Update offers automatically – the same data that
/// commercial driver updaters show as "outdated". Downloads come directly from Microsoft's servers.
/// </summary>
public sealed partial class CatalogService
{
    private const string BaseUrl = "https://www.catalog.update.microsoft.com/";
    private readonly HttpClient _http;

    public CatalogService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(40) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ITDriver", SelfUpdater.CurrentVersion.ToString()));
        _http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
    }

    /// <summary>Searches the catalog (first result page, 25 entries; the catalog rejects automated paging postbacks,
    /// so callers combine several query forms instead).</summary>
    public async Task<List<CatalogEntry>> SearchAsync(string query, CancellationToken ct = default)
        => ParseSearch(await SearchHtmlAsync(query, ct));

    internal Task<string> SearchHtmlAsync(string query, CancellationToken ct = default)
        => _http.GetStringAsync(BaseUrl + "Search.aspx?q=" + Uri.EscapeDataString(query), ct);

    [GeneratedRegex(@"goToDetails\(\s*[""']([0-9a-fA-F\-]{36})[""']\s*\)\s*;?\s*['""]?[^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex RowRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\.url\s*=\s*'([^']+)'")]
    private static partial Regex DownloadUrlRegex();

    internal static List<CatalogEntry> ParseSearch(string html)
    {
        var list = new List<CatalogEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in RowRegex().Matches(html))
        {
            var id = m.Groups[1].Value;
            if (!seen.Add(id)) continue;
            string Cell(int n)
            {
                var cell = Regex.Match(html, $@"id=[""']{Regex.Escape(id)}_C{n}_R\d+[""'][^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                return cell.Success ? Clean(cell.Groups[1].Value) : "";
            }
            var sizeMatch = Regex.Match(html, $@"id=[""']{Regex.Escape(id)}_originalSize[""'][^>]*>\s*(\d+)\s*<", RegexOptions.IgnoreCase);
            list.Add(new CatalogEntry(
                id,
                Clean(m.Groups[2].Value),
                Cell(2),
                Cell(3),
                DateTime.TryParse(Cell(4), System.Globalization.CultureInfo.GetCultureInfo("en-US"), System.Globalization.DateTimeStyles.None, out var d) ? d : null,
                Cell(5),
                sizeMatch.Success && long.TryParse(sizeMatch.Groups[1].Value, out var size) ? size : 0));
        }
        return list;
    }

    private static string Clean(string s) => WebUtility.HtmlDecode(TagRegex().Replace(s, " ")).Trim().Replace("\r", " ").Replace("\n", " ").Replace("  ", " ");

    public async Task<List<string>> GetDownloadUrlsAsync(string updateId, CancellationToken ct = default)
    {
        var payload = $"[{{\"size\":0,\"languages\":\"\",\"uidInfo\":\"{updateId}\",\"updateID\":\"{updateId}\"}}]";
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("updateIDs", payload) });
        using var resp = await _http.PostAsync(BaseUrl + "DownloadDialog.aspx", content, ct);
        resp.EnsureSuccessStatusCode();
        return ParseDownloadUrls(await resp.Content.ReadAsStringAsync(ct));
    }

    internal static List<string> ParseDownloadUrls(string js)
        => DownloadUrlRegex().Matches(js).Select(m => m.Groups[1].Value)
            .Where(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            .Select(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + u[7..] : u)
            .Distinct().ToList();

    /// <summary>Catalog search terms for a device, most specific first (e.g. VEN_10EC&amp;DEV_8125&amp;SUBSYS_E0001458, then VEN_10EC&amp;DEV_8125).</summary>
    public static List<string> QueriesFor(DeviceInfo device)
    {
        var raw = device.HardwareIds.FirstOrDefault(h => HardwareId.TryParse(h) is { IsPhysicalBus: true, DeviceId: not null });
        var id = HardwareId.TryParse(raw);
        if (id is null) return new();
        var result = new List<string>();
        if (id.Bus == "ACPI")
        {
            result.Add($"{id.VendorId}{id.DeviceId}");
            return result;
        }
        var (v, d) = id.VendorNamespace == "USB" && raw!.Contains("VID_", StringComparison.OrdinalIgnoreCase) ? ("VID", "PID") : ("VEN", "DEV");
        var basic = $"{v}_{id.VendorId}&{d}_{id.DeviceId}";
        // The full ID including the bus prefix (e.g. "HDAUDIO\FUNC_01&VEN_10EC&DEV_1220") ranks the newest packages first;
        // the bare "VEN&DEV" form finds packages that list the device under other subsystem IDs.
        var prefixed = Regex.Match(raw!, $@"^.*?{d}_{id.DeviceId}", RegexOptions.IgnoreCase);
        if (prefixed.Success && prefixed.Value.Contains('\\')) result.Add(prefixed.Value.ToUpperInvariant());
        var subsys = Regex.Match(raw!, @"SUBSYS_([0-9A-F]{8})", RegexOptions.IgnoreCase);
        if (subsys.Success) result.Add($"{basic}&SUBSYS_{subsys.Groups[1].Value.ToUpperInvariant()}");
        result.Add(basic);
        return result;
    }

    /// <summary>Should this device be checked in the catalog? Only physical hardware with a vendor/device ID.</summary>
    /// <summary>Windows on ARM (also true when this x64 build runs emulated there).</summary>
    public static bool IsArm64Os => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64;

    public static bool IsCandidate(DeviceInfo d)
        => d.Present && !d.IsVirtual && d.ErrorCode is not (22 or 45) && QueriesFor(d).Count > 0;

    /// <summary>Picks the best newer catalog driver for a device, or null if the installed one is current.</summary>
    public static CatalogEntry? PickNewer(DeviceInfo device, IEnumerable<CatalogEntry> entries, int? osBuild = null, bool? arm64 = null)
    {
        var build = osBuild ?? Environment.OSVersion.Version.Build;
        // ARM PCs (Snapdragon & co.) need ARM64 drivers, x64 PCs must never get them.
        var arm = arm64 ?? IsArm64Os;
        entries = entries.Where(e => e.MinBuild <= build && e.IsArm == arm).ToList();
        var installed = VersionUtil.Parse(device.DriverVersion);
        if (UsesGenericDriverWithVendorAlternative(device))
        {
            // Microsoft's generic driver is active (e.g. "High Definition Audio", "Microsoft Basic Display"):
            // offer the newest vendor driver from the catalog regardless of the version number scheme.
            return entries.Where(e => e.IsDriver && e.TargetsWindows10Or11 && !string.IsNullOrWhiteSpace(e.Version))
                .OrderByDescending(e => e.LastUpdated).ThenByDescending(e => VersionUtil.Parse(e.Version)).FirstOrDefault();
        }
        var candidates = entries
            .Where(e => e.IsDriver && e.TargetsWindows10Or11 && !string.IsNullOrWhiteSpace(e.Version))
            .Where(e => device.IsMissingDriver || string.IsNullOrEmpty(device.DriverVersion) || VersionUtil.Parse(e.Version) > installed)
            // Same major version line as installed driver avoids jumping to unrelated packages (e.g. Microsoft inbox 10.0.x vs vendor 1.2.x)
            .Where(e => device.IsMissingDriver || string.IsNullOrEmpty(device.DriverVersion) || SameFamily(installed, VersionUtil.Parse(e.Version)))
            .OrderByDescending(e => VersionUtil.Parse(e.Version))
            .ThenByDescending(e => e.LastUpdated)
            .ToList();
        return candidates.FirstOrDefault();
    }

    private static readonly HashSet<string> VendorDriverClasses = new(StringComparer.OrdinalIgnoreCase) { "Display", "Net", "MEDIA", "Bluetooth" };

    /// <summary>Chipset makers whose "System devices" (GPIO, I2C, SMBus, PSP, MEI, Serial IO, power management; Qualcomm on ARM PCs)
    /// get dedicated drivers in the chipset packages that Windows' inbox drivers don't replace.</summary>
    private static readonly HashSet<string> ChipsetVendors = new(StringComparer.OrdinalIgnoreCase) { "AMD", "Intel", "Qualcomm" };

    /// <summary>Device of a class where vendor drivers clearly beat the Microsoft inbox driver (GPU, network, audio, Bluetooth, AMD/Intel chipset).</summary>
    internal static bool UsesGenericDriverWithVendorAlternative(DeviceInfo d)
        => string.Equals(d.DriverProvider, "Microsoft", StringComparison.OrdinalIgnoreCase)
           && !(d.InfName?.StartsWith("oem", StringComparison.OrdinalIgnoreCase) ?? false)
           && (VendorDriverClasses.Contains(d.Class ?? "")
               || string.Equals(d.Class, "System", StringComparison.OrdinalIgnoreCase) && ChipsetVendors.Contains(d.Vendor?.Name ?? ""))
           && d.Vendor is { IsMicrosoft: false }
           && !(d.HardwareIds.FirstOrDefault()?.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ?? false); // USB audio/headsets work best with the class driver

    private static bool SameFamily(Version installed, Version candidate)
        // Vendors keep the first version field stable or increase it moderately; huge jumps usually mean another package type.
        => candidate.Major >= installed.Major && candidate.Major - installed.Major <= 50 || installed.Major == 0;

    /// <summary>
    /// Checks all candidate devices against the catalog (a few requests in parallel) and returns update offers.
    /// Devices in <paramref name="skipDeviceIds"/> are skipped. Devices with a Windows Update offer are still searched,
    /// because the catalog often has a newer version than Windows Update ships (see <see cref="MergeOffers"/>).
    /// </summary>
    public async Task<List<DriverUpdate>> FindUpdatesAsync(IReadOnlyList<DeviceInfo> devices, ISet<string> skipDeviceIds, IProgress<string>? progress, CancellationToken ct = default)
    {
        var groups = devices.Where(d => IsCandidate(d) && !skipDeviceIds.Contains(d.InstanceId))
            .GroupBy(d => QueriesFor(d)[0], StringComparer.OrdinalIgnoreCase)
            .ToList();
        var results = new List<DriverUpdate>();
        var gate = new object();
        int done = 0;
        var errors = 0;
        await Parallel.ForEachAsync(groups, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (g, token) =>
        {
            var device = g.First();
            try
            {
                // Several query forms rank different packages first, so the results are combined. The bare VEN&DEV form
                // (last) is only needed when the more specific ones found no driver at all.
                var queries = QueriesFor(device);
                var entries = new List<CatalogEntry>();
                for (int i = 0; i < queries.Count; i++)
                {
                    if (i == queries.Count - 1 && i > 0 && entries.Any(e => e.IsDriver)) break;
                    foreach (var e in await SearchAsync(queries[i], token))
                        if (entries.All(x => x.UpdateId != e.UpdateId)) entries.Add(e);
                }
                var best = PickNewer(device, entries);
                if (best != null)
                {
                    lock (gate)
                    {
                        results.Add(new DriverUpdate
                        {
                            UpdateId = best.UpdateId,
                            Title = best.Title,
                            Source = UpdateSource.Catalog,
                            DriverClass = device.Class,
                            DriverModel = device.Name,
                            Provider = ProviderFromTitle(best.Title) ?? device.DriverProvider,
                            DriverDate = best.LastUpdated,
                            NewVersion = best.Version,
                            SizeBytes = best.SizeBytes,
                            HardwareId = device.HardwareIds.FirstOrDefault(),
                            DeviceInstanceId = device.InstanceId,
                            DeviceName = device.Name,
                            InstalledVersion = device.DriverVersion,
                            InstalledDate = device.DriverDate,
                            InstalledInf = device.InfName,
                            IsMissingDriver = device.IsMissingDriver,
                            InfoUrl = BaseUrl + "ScopedViewInline.aspx?updateid=" + best.UpdateId,
                        });
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errors);
                Log.Write($"Katalogsuche für {device.Name} fehlgeschlagen: {ex.Message}");
            }
            var n = Interlocked.Increment(ref done);
            progress?.Report($"Durchsuche Microsoft Update-Katalog ({n}/{groups.Count}) …");
        });
        if (errors > 0 && errors == groups.Count && groups.Count > 0)
            throw new InvalidOperationException("Der Microsoft Update-Katalog ist nicht erreichbar.");
        return results;
    }

    /// <summary>
    /// Combines offers from several sources: per device only the newest version is kept (Windows Update wins a tie,
    /// because it is installed through the official update stack). Offers without a matched device are kept as they are.
    /// </summary>
    public static List<DriverUpdate> MergeOffers(IEnumerable<DriverUpdate> windowsUpdate, IEnumerable<DriverUpdate> other)
    {
        var result = windowsUpdate.ToList();
        foreach (var o in other)
        {
            var existing = o.DeviceInstanceId == null ? null
                : result.FirstOrDefault(u => string.Equals(u.DeviceInstanceId, o.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
            if (existing == null) { result.Add(o); continue; }
            if (VersionUtil.Parse(o.NewVersion) > VersionUtil.Parse(existing.NewVersion) && !string.IsNullOrEmpty(existing.NewVersion) || string.IsNullOrEmpty(existing.NewVersion) && existing.Source != UpdateSource.WindowsUpdate)
            {
                result.Remove(existing);
                result.Add(o);
            }
        }
        return result;
    }

    internal static string? ProviderFromTitle(string title)
    {
        var i = title.IndexOf(" - ", StringComparison.Ordinal);
        return i > 0 ? title[..i].Trim() : null;
    }

    /// <summary>Downloads the catalog package (.cab), extracts it and installs it with pnputil.</summary>
    [SupportedOSPlatform("windows")]
    public async Task<InstallOutcome> InstallAsync(DriverUpdate update, IProgress<string>? progress, CancellationToken ct = default)
    {
        var outcome = new InstallOutcome { UpdateId = update.UpdateId, Title = update.Title };
        var work = Path.Combine(Path.GetTempPath(), "ITDriver-Catalog", update.UpdateId);
        try
        {
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            progress?.Report("Wird heruntergeladen …");
            var urls = await GetDownloadUrlsAsync(update.UpdateId, ct);
            if (urls.Count == 0) { outcome.Message = "Keine Download-Adresse im Katalog gefunden."; return outcome; }
            var extract = Path.Combine(work, "files");
            Directory.CreateDirectory(extract);
            int i = 0;
            foreach (var url in urls)
            {
                var uri = new Uri(url);
                if (!uri.Host.EndsWith("windowsupdate.com", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith("microsoft.com", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unerwartete Download-Quelle: " + uri.Host);
                var file = Path.Combine(work, $"{i++}-{Path.GetFileName(uri.LocalPath)}");
                await using (var fs = File.Create(file))
                await using (var s = await _http.GetStreamAsync(uri, ct))
                    await s.CopyToAsync(fs, ct);
                progress?.Report("Wird entpackt …");
                var target = Path.Combine(extract, Path.GetFileNameWithoutExtension(file));
                Directory.CreateDirectory(target);
                var ex = await ProcessRunner.RunAsync("expand.exe", $"-F:* {ProcessRunner.Quote(file)} {ProcessRunner.Quote(target)}", ct);
                if (!ex.Ok) Log.Write("expand: " + ex.Combined);
            }
            if (!Directory.EnumerateFiles(extract, "*.inf", SearchOption.AllDirectories).Any())
            {
                outcome.Message = "Das Paket enthält keinen installierbaren Treiber (INF).";
                return outcome;
            }
            progress?.Report("Wird installiert …");
            var r = await ProcessRunner.RunAsync("pnputil.exe", $"/add-driver {ProcessRunner.Quote(Path.Combine(extract, "*.inf"))} /subdirs /install", ct);
            Log.Write($"pnputil Katalog {update.Title}: {r.Combined.Trim()}");
            // 0 = ok, 3010 = ok, reboot required. 259 (ERROR_NO_MORE_ITEMS) = added, but no better match for any device.
            outcome.Success = r.ExitCode is 0 or 3010;
            outcome.RebootRequired = r.ExitCode == 3010;
            outcome.Message = r.ExitCode switch
            {
                0 => "Erfolgreich installiert.",
                3010 => "Installiert – Neustart erforderlich.",
                259 => "Treiber wurde dem Treiberspeicher hinzugefügt, Windows behält aber den bisherigen (passender für dieses Gerät).",
                _ => $"Installation fehlgeschlagen (pnputil Code {r.ExitCode}).",
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            outcome.Message = "Fehler: " + ex.Message;
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
        }
        return outcome;
    }
}
