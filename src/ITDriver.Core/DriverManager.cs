using System.Net;
using System.Runtime.Versioning;
using System.Text;

namespace ITDriver.Core;

public sealed class ScanResult
{
    public DateTime Time { get; set; } = DateTime.Now;
    public SystemSummary System { get; set; } = new();
    public List<DeviceInfo> Devices { get; set; } = new();
    public List<DriverUpdate> Updates { get; set; } = new();
    public List<DriverUpdate> IgnoredUpdates { get; set; } = new();
    public List<Recommendation> Recommendations { get; set; } = new();
    public string? WindowsUpdateError { get; set; }
    public string? CatalogError { get; set; }
    public int MissingCount => Updates.Count(u => u.IsMissingDriver) + Devices.Count(d => d.Present && d.IsMissingDriver && !d.IsVirtual && Updates.All(u => u.DeviceInstanceId != d.InstanceId));

    public int ProblemCount => Devices.Count(d => d.Present && d.HasProblem && d.ErrorCode is not (22 or 45));
}

/// <summary>High-level workflow used by the UI and the command line: scan, install safely, report.</summary>
[SupportedOSPlatform("windows")]
public sealed class DriverManager
{
    public HardwareScanner Scanner { get; } = new();
    public WindowsUpdateService WindowsUpdate { get; } = new();
    public CatalogService Catalog { get; } = new();
    public VendorUpdateService Vendors { get; } = new();
    public DriverStoreService Store { get; } = new();
    public RestorePointService RestorePoints { get; } = new();
    public DriverBackupService Backups { get; }
    public RollbackService Rollback { get; }
    public AppSettings Settings { get; private set; }

    public DriverManager(AppSettings settings)
    {
        Settings = settings;
        Backups = new DriverBackupService(Store, () => Settings.EffectiveBackupFolder);
        Rollback = new RollbackService(Store);
    }

    public async Task<ScanResult> ScanAsync(IProgress<string>? progress, bool includeWindowsUpdate = true, CancellationToken ct = default)
    {
        var result = new ScanResult();
        progress?.Report("Lese Systeminformationen …");
        result.System = await Task.Run(Scanner.ScanSystem, ct);
        progress?.Report("Erkenne Hardware und installierte Treiber …");
        result.Devices = await Task.Run(Scanner.ScanDevices, ct);

        if (includeWindowsUpdate)
        {
            progress?.Report("Suche Treiber-Updates bei Windows Update (kann 1–3 Minuten dauern) …");
            try
            {
                var all = await WindowsUpdate.SearchAsync(result.Devices, ct);
                result.Updates = all.Where(u => !Settings.IsIgnored(u)).ToList();
                result.IgnoredUpdates = all.Where(Settings.IsIgnored).ToList();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.WindowsUpdateError = ex is System.Runtime.InteropServices.COMException c
                    ? $"0x{c.HResult:X8}: {WuErrors.Describe(c.HResult)}"
                    : ex.Message;
                Log.Write("Windows Update Suche fehlgeschlagen: " + result.WindowsUpdateError);
            }
        }

        if (Settings.SearchCatalog)
        {
            progress?.Report("Durchsuche Microsoft Update-Katalog nach neueren Treibern …");
            try
            {
                var ignoredDevices = result.IgnoredUpdates.Select(u => u.DeviceInstanceId).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
                var found = await Catalog.FindUpdatesAsync(result.Devices, ignoredDevices, progress, ct);
                result.Updates = CatalogService.MergeOffers(result.Updates, found.Where(u => !Settings.IsIgnored(u)));
                result.IgnoredUpdates.AddRange(found.Where(Settings.IsIgnored));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.CatalogError = ex.Message;
                Log.Write("Katalogsuche fehlgeschlagen: " + ex.Message);
            }
        }

        if (Settings.SearchVendors)
        {
            try
            {
                var installedSoftware = await Task.Run(VendorUpdateService.ReadInstalledSoftware, ct);
                var vendorOffers = await Vendors.FindUpdatesAsync(result.System, result.Devices, installedSoftware, progress, ct);
                result.Updates = CatalogService.MergeOffers(result.Updates, vendorOffers.Where(u => !Settings.IsIgnored(u)));
                result.IgnoredUpdates.AddRange(vendorOffers.Where(Settings.IsIgnored));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Log.Write("Herstellerprüfung fehlgeschlagen: " + ex.Message); }
        }

        result.Recommendations = Settings.ShowVendorRecommendations
            ? RecommendationEngine.Build(result.Devices, result.System, result.Updates, Settings.OldDriverYears)
            : new();
        try { AppPaths.SaveJson(AppPaths.LastScan, new { result.Time, Updates = result.Updates.Count, Problems = result.ProblemCount }); } catch { }
        Log.Write($"Scan: {result.Devices.Count} Geräte, {result.Updates.Count} Updates, {result.ProblemCount} Probleme");
        return result;
    }

    /// <summary>
    /// Installs the given updates one by one. Before the first change a restore point is created and the
    /// currently installed driver package of each affected device is backed up, so it can be rolled back.
    /// </summary>
    public async Task<List<InstallOutcome>> InstallAsync(IReadOnlyList<DriverUpdate> updates, Action<DriverUpdate, string>? onState, IProgress<string>? progress, CancellationToken ct = default)
    {
        var outcomes = new List<InstallOutcome>();
        if (updates.Count == 0) return outcomes;

        foreach (var u in updates.Where(u => u.Source == UpdateSource.Vendor))
        {
            // Vendor packages come with the vendor's own installer: open the official download page instead of installing silently.
            if (u.InfoUrl != null) OpenUrl(u.InfoUrl);
            onState?.Invoke(u, "↗ Herstellerseite geöffnet");
            outcomes.Add(new InstallOutcome { UpdateId = u.UpdateId, Title = u.Title, Success = false, Message = "Download-Seite des Herstellers geöffnet." });
        }
        updates = updates.Where(u => u.Source != UpdateSource.Vendor).ToList();
        if (updates.Count == 0) return outcomes;

        bool restorePoint = false;
        if (Settings.CreateRestorePoint)
        {
            progress?.Report("Erstelle Wiederherstellungspunkt …");
            var err = await RestorePoints.CreateAsync($"ITDriver: {updates.Count} Treiber-Update(s)");
            restorePoint = err is null;
            if (err != null) { Log.Write(err); progress?.Report(err); }
        }

        string? backupFolder = null;
        if (Settings.BackupBeforeInstall)
        {
            var infs = updates.Select(u => u.InstalledInf).Where(i => i != null && i.StartsWith("oem", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (infs.Count > 0)
            {
                try
                {
                    var b = await Backups.BackupAsync("Vor Update", infs!, progress, ct);
                    backupFolder = b.Folder;
                    Backups.Prune(Settings.KeepBackups);
                }
                catch (Exception ex) { Log.Write("Backup vor Update fehlgeschlagen: " + ex.Message); }
            }
        }

        foreach (var u in updates)
        {
            ct.ThrowIfCancellationRequested();
            onState?.Invoke(u, "Läuft …");
            progress?.Report($"Installiere {u.DisplayName} …");
            var stateProgress = new Progress<string>(s => onState?.Invoke(u, s));
            var outcome = u.Source == UpdateSource.Catalog
                ? await Catalog.InstallAsync(u, stateProgress, ct)
                : await WindowsUpdate.InstallAsync(u, stateProgress, ct);
            outcomes.Add(outcome);
            onState?.Invoke(u, outcome.Success ? (outcome.RebootRequired ? "✔ Neustart nötig" : "✔ Installiert") : "✖ " + outcome.Message);

            DeviceInfo? after = null;
            if (outcome.Success && u.DeviceInstanceId != null)
            {
                try { after = (await Task.Run(Scanner.ScanDevices, ct)).FirstOrDefault(d => d.InstanceId == u.DeviceInstanceId); }
                catch { }
            }

            History.Add(new HistoryEntry
            {
                Action = "Installiert",
                Title = u.Title,
                Success = outcome.Success,
                Message = outcome.Message,
                DeviceInstanceId = u.DeviceInstanceId,
                DeviceName = u.DisplayName,
                PreviousVersion = u.InstalledVersion,
                PreviousInf = u.InstalledInf,
                NewVersion = after?.DriverVersion,
                NewInf = after?.InfName,
                BackupFolder = backupFolder != null && u.InstalledInf != null ? Path.Combine(backupFolder, Path.GetFileNameWithoutExtension(u.InstalledInf)) : null,
                RestorePointCreated = restorePoint,
            });
            Log.Write($"{u.Title}: {outcome.Message}");
        }
        return outcomes;
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("Link konnte nicht geöffnet werden: " + ex.Message); }
    }

    public async Task<(bool Ok, string Message)> RollbackAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        var r = await Rollback.RollbackAsync(entry, ct);
        if (r.Ok)
        {
            var all = History.Load();
            var match = all.FirstOrDefault(h => h.Time == entry.Time && h.Title == entry.Title);
            if (match != null) match.RolledBack = true;
            History.Save(all);
            History.Add(new HistoryEntry { Action = "Zurückgesetzt", Title = entry.Title, DeviceName = entry.DeviceName, Success = true, Message = r.Message });
        }
        return r;
    }
}

/// <summary>Exports the scan as JSON (for support / reference) or as a readable HTML report.</summary>
public static class ReportExporter
{
    public static void ExportJson(ScanResult scan, string path)
    {
        AppPaths.SaveJson(path, new
        {
            Tool = "ITDriver",
            AppVersion = SelfUpdater.CurrentVersion.ToString(),
            Created = scan.Time,
            scan.System,
            ProblemDevices = scan.Devices.Where(d => d.HasProblem),
            scan.Devices,
            WindowsUpdateDrivers = scan.Updates,
            scan.Recommendations,
        });
    }

    public static void ExportHtml(ScanResult scan, string path)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>ITDriver Bericht</title><style>")
          .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1b1f24}h1{color:#0b62d6}table{border-collapse:collapse;width:100%;margin-bottom:24px}")
          .Append("th,td{border-bottom:1px solid #ddd;padding:6px 8px;text-align:left;font-size:13px}th{background:#f2f5f9}.bad{color:#c62828;font-weight:600}")
          .Append("</style></head><body>");
        sb.Append($"<h1>ITDriver – Treiberbericht</h1><p>Erstellt am {scan.Time:dd.MM.yyyy HH:mm}</p>");
        var s = scan.System;
        sb.Append("<h2>System</h2><table>")
          .Append($"<tr><th>Betriebssystem</th><td>{E(s.OS)} ({E(s.OSBuild)})</td></tr>")
          .Append($"<tr><th>Gerät</th><td>{E(s.Manufacturer)} {E(s.Model)}</td></tr>")
          .Append($"<tr><th>Mainboard</th><td>{E(s.Board)}</td></tr><tr><th>BIOS</th><td>{E(s.Bios)}</td></tr>")
          .Append($"<tr><th>CPU</th><td>{E(s.Cpu)}</td></tr><tr><th>RAM</th><td>{s.RamGB} GB</td></tr>")
          .Append($"<tr><th>Grafik</th><td>{E(string.Join(", ", s.Gpus))}</td></tr></table>");
        sb.Append($"<h2>Verfügbare Updates ({scan.Updates.Count})</h2><table><tr><th>Gerät</th><th>Update</th><th>Installiert</th><th>Neu vom</th></tr>");
        foreach (var u in scan.Updates)
            sb.Append($"<tr><td>{E(u.DisplayName)}</td><td>{E(u.Title)}</td><td>{E(u.InstalledVersion)}</td><td>{u.DriverDate:dd.MM.yyyy}</td></tr>");
        sb.Append("</table>");
        sb.Append($"<h2>Empfehlungen ({scan.Recommendations.Count})</h2><table><tr><th>Stufe</th><th>Thema</th><th>Details</th></tr>");
        foreach (var r in scan.Recommendations)
            sb.Append($"<tr><td>{E(r.SeverityText)}</td><td>{E(r.Title)}</td><td>{E(r.Detail)}{(r.Url != null ? $" <a href=\"{E(r.Url)}\">{E(r.UrlText)}</a>" : "")}</td></tr>");
        sb.Append("</table>");
        sb.Append($"<h2>Geräte ({scan.Devices.Count})</h2><table><tr><th>Klasse</th><th>Gerät</th><th>Hersteller</th><th>Treiber</th><th>Datum</th><th>Status</th></tr>");
        foreach (var d in scan.Devices.Where(d => d.Present))
            sb.Append($"<tr><td>{E(d.Class)}</td><td>{E(d.Name)}</td><td>{E(d.VendorName)}</td><td>{E(d.DriverVersion)}</td><td>{d.DriverDate:dd.MM.yyyy}</td><td class=\"{(d.HasProblem ? "bad" : "")}\">{E(d.ProblemText)}</td></tr>");
        sb.Append("</table></body></html>");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }
}
