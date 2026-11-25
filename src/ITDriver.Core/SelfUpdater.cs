using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace ITDriver.Core;

public sealed record AppRelease(Version Version, string Tag, string Name, string Notes, string? ZipUrl, string? ShaUrl, string HtmlUrl,
    string? SetupUrl = null, string? SetupShaUrl = null);

/// <summary>Checks GitHub Releases for a newer ITDriver and installs it (SHA-256 verified).</summary>
public sealed class SelfUpdater
{
    public const string Repository = "MarcoEckerlin/ITDriver";
    public const string AssetName = "ITDriver-win-x64.zip";
    public const string SetupAssetName = "ITDriver-Setup.exe";

    private readonly HttpClient _http;

    public SelfUpdater(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ITDriver", CurrentVersion.ToString()));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public static Version CurrentVersion =>
        typeof(SelfUpdater).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(1, 0, 0);

    public async Task<AppRelease?> GetLatestAsync(CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        return ParseRelease(json);
    }

    internal static AppRelease? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        string? zip = null, sha = null, setup = null, setupSha = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString();
                var url = a.GetProperty("browser_download_url").GetString();
                if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase)) zip = url;
                if (string.Equals(name, AssetName + ".sha256", StringComparison.OrdinalIgnoreCase)) sha = url;
                if (string.Equals(name, SetupAssetName, StringComparison.OrdinalIgnoreCase)) setup = url;
                if (string.Equals(name, SetupAssetName + ".sha256", StringComparison.OrdinalIgnoreCase)) setupSha = url;
            }
        }
        return new AppRelease(
            VersionUtil.Parse(tag),
            tag,
            root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag,
            root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            zip, sha,
            root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "",
            setup, setupSha);
    }

    public static bool IsNewer(AppRelease release, Version current)
        => Normalize(release.Version) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>Downloads, verifies and stages the update, then starts a helper script that swaps the files after this process exits.</summary>
    public async Task<string> DownloadAndApplyAsync(AppRelease release, IProgress<string>? progress, CancellationToken ct = default)
    {
        // Installed via setup (uninstaller next to the exe): update through the new setup so "Apps & Features" stays correct.
        if (IsInstalledWithSetup && release.SetupUrl != null && release.SetupShaUrl != null)
            return await RunSetupAsync(release, progress, ct);

        if (release.ZipUrl is null || release.ShaUrl is null)
            throw new InvalidOperationException("Das Release enthält kein signiertes Update-Paket (ZIP + SHA256).");

        var work = Path.Combine(Path.GetTempPath(), "ITDriver-Update-" + release.Tag);
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        var zipPath = Path.Combine(work, AssetName);

        progress?.Report("Lade Update herunter …");
        await using (var fs = File.Create(zipPath))
        await using (var s = await _http.GetStreamAsync(release.ZipUrl, ct))
            await s.CopyToAsync(fs, ct);

        progress?.Report("Prüfe Prüfsumme …");
        var expected = ParseSha256((await _http.GetStringAsync(release.ShaUrl, ct)));
        string actual;
        await using (var zs = File.OpenRead(zipPath))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(zs, ct));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Prüfsumme stimmt nicht – Update wurde verworfen.");

        var extract = Path.Combine(work, "files");
        ZipFile.ExtractToDirectory(zipPath, extract);

        var appDir = AppContext.BaseDirectory.TrimEnd('\\');
        var exe = Environment.ProcessPath ?? Path.Combine(appDir, "ITDriver.exe");
        var script = Path.Combine(work, "apply-update.cmd");
        await File.WriteAllTextAsync(script,
            "@echo off\r\n" +
            $"rem ITDriver Update auf {release.Tag}\r\n" +
            ":wait\r\n" +
            $"tasklist /FI \"PID eq {Environment.ProcessId}\" | find \"{Environment.ProcessId}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            $"robocopy \"{extract}\" \"{appDir}\" /E /R:3 /W:1 >nul\r\n" +
            $"start \"\" \"{exe}\" --updated\r\n", ct);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false });
        return script;
    }

    public static bool IsInstalledWithSetup => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    private async Task<string> RunSetupAsync(AppRelease release, IProgress<string>? progress, CancellationToken ct)
    {
        var work = Path.Combine(Path.GetTempPath(), "ITDriver-Update-" + release.Tag);
        Directory.CreateDirectory(work);
        var setup = Path.Combine(work, SetupAssetName);
        progress?.Report("Lade Installer herunter …");
        await using (var fs = File.Create(setup))
        await using (var s = await _http.GetStreamAsync(release.SetupUrl, ct))
            await s.CopyToAsync(fs, ct);
        progress?.Report("Prüfe Prüfsumme …");
        var expected = ParseSha256(await _http.GetStringAsync(release.SetupShaUrl, ct));
        string actual;
        await using (var zs = File.OpenRead(setup))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(zs, ct));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Prüfsumme stimmt nicht – Update wurde verworfen.");
        // Silent install; the setup closes this app, installs and restarts ITDriver with --updated.
        Process.Start(new ProcessStartInfo(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
        return setup;
    }

    internal static string ParseSha256(string text)
        => text.Trim().Split(new[] { ' ', '\t', '\r', '\n', '*' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
}
