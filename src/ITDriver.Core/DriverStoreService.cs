using System.Runtime.Versioning;
using System.Text.Json;

namespace ITDriver.Core;

/// <summary>Driver store access via DISM (Get-WindowsDriver, language independent) and pnputil.exe.</summary>
[SupportedOSPlatform("windows")]
public sealed class DriverStoreService
{
    public async Task<List<DriverPackage>> GetThirdPartyPackagesAsync(CancellationToken ct = default)
    {
        const string script = "Get-WindowsDriver -Online | Select-Object Driver,OriginalFileName,ProviderName,ClassName,Version,@{n='Date';e={$_.Date.ToString('yyyy-MM-dd')}},BootCritical,Inbox | ConvertTo-Json -Depth 2";
        var r = await ProcessRunner.PowerShellAsync(script, ct);
        if (!r.Ok) throw new InvalidOperationException("Treiberspeicher konnte nicht gelesen werden: " + r.Error.Trim());
        return ParseWindowsDriverJson(r.Output);
    }

    internal static List<DriverPackage> ParseWindowsDriverJson(string json)
    {
        var list = new List<DriverPackage>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        using var doc = JsonDocument.Parse(json);
        IEnumerable<JsonElement> items = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray()
            : new[] { doc.RootElement };
        foreach (var e in items)
        {
            list.Add(new DriverPackage
            {
                PublishedName = Str(e, "Driver") ?? "",
                OriginalFileName = Str(e, "OriginalFileName") ?? "",
                ProviderName = Str(e, "ProviderName"),
                ClassName = Str(e, "ClassName"),
                Version = Str(e, "Version"),
                Date = DateTime.TryParse(Str(e, "Date"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null,
                BootCritical = e.TryGetProperty("BootCritical", out var b) && b.ValueKind == JsonValueKind.True,
                Inbox = e.TryGetProperty("Inbox", out var i) && i.ValueKind == JsonValueKind.True,
            });
        }
        return list;
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;

    public Task<ProcessResult> ExportAsync(string publishedNameOrStar, string targetFolder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetFolder);
        return ProcessRunner.RunAsync("pnputil.exe", $"/export-driver {publishedNameOrStar} {ProcessRunner.Quote(targetFolder)}", ct);
    }

    public Task<ProcessResult> AddAndInstallAsync(string folder, CancellationToken ct = default)
        => ProcessRunner.RunAsync("pnputil.exe", $"/add-driver {ProcessRunner.Quote(Path.Combine(folder, "*.inf"))} /subdirs /install", ct);

    /// <summary>Removes a package. Without <paramref name="uninstall"/> Windows refuses to delete packages that are in use (safe default).</summary>
    public Task<ProcessResult> DeleteAsync(string publishedName, bool uninstall, CancellationToken ct = default)
        => ProcessRunner.RunAsync("pnputil.exe", $"/delete-driver {publishedName}{(uninstall ? " /uninstall" : "")}", ct);

    public Task<ProcessResult> RescanDevicesAsync(CancellationToken ct = default)
        => ProcessRunner.RunAsync("pnputil.exe", "/scan-devices", ct);

    public static long FolderSize(string? folder)
    {
        if (folder is null || !Directory.Exists(folder)) return 0;
        try { return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
    }
}

/// <summary>Finds old, unused duplicate driver packages that only waste disk space.</summary>
public static class DriverStoreCleanup
{
    public sealed record Candidate(DriverPackage Package, DriverPackage Newer, long SizeBytes);

    /// <param name="inUseInfs">Published names (oemNN.inf) currently bound to any device.</param>
    public static List<Candidate> FindCandidates(IEnumerable<DriverPackage> packages, ISet<string> inUseInfs, Func<DriverPackage, long>? sizeOf = null)
    {
        var result = new List<Candidate>();
        var groups = packages
            .Where(p => !p.Inbox && p.PublishedName.StartsWith("oem", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => (p.InfFileName.ToLowerInvariant(), (p.ProviderName ?? "").ToLowerInvariant(), (p.ClassName ?? "").ToLowerInvariant()));
        foreach (var g in groups)
        {
            if (g.Count() < 2) continue;
            var ordered = g.OrderByDescending(p => VersionUtil.Parse(p.Version)).ThenByDescending(p => p.Date).ToList();
            var newest = ordered[0];
            foreach (var old in ordered.Skip(1))
            {
                if (old.BootCritical || inUseInfs.Contains(old.PublishedName)) continue;
                if (VersionUtil.Parse(old.Version) >= VersionUtil.Parse(newest.Version) && old.Date >= newest.Date) continue;
                result.Add(new Candidate(old, newest, sizeOf?.Invoke(old) ?? 0));
            }
        }
        return result.OrderByDescending(c => c.SizeBytes).ToList();
    }
}

public static class VersionUtil
{
    /// <summary>Parses driver/app versions like "31.0.101.4502", "v1.2.3" or "1.2.3-beta" leniently.</summary>
    public static Version Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new Version(0, 0);
        var t = text.Trim().TrimStart('v', 'V');
        var cut = t.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut > 0) t = t[..cut];
        var parts = t.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).Take(4).ToList();
        while (parts.Count < 2) parts.Add(0);
        return parts.Count switch
        {
            2 => new Version(parts[0], parts[1]),
            3 => new Version(parts[0], parts[1], parts[2]),
            _ => new Version(parts[0], parts[1], parts[2], parts[3]),
        };
    }
}
