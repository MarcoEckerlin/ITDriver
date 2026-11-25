using System.IO.Compression;
using System.Management;
using System.Runtime.Versioning;

namespace ITDriver.Core;

/// <summary>Creates System Restore points before driver changes.</summary>
[SupportedOSPlatform("windows")]
public sealed class RestorePointService
{
    private const int DeviceDriverInstall = 10;
    private const int BeginSystemChange = 100;

    /// <summary>Returns null on success, otherwise a German error text.</summary>
    public Task<string?> CreateAsync(string description) => Task.Run<string?>(() =>
    {
        try
        {
            using var cls = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
            var result = cls.InvokeMethod("CreateRestorePoint", new object[] { description, DeviceDriverInstall, BeginSystemChange });
            var code = Convert.ToInt32(result ?? 0);
            return code switch
            {
                0 => null,
                1058 => "Die Computerwiederherstellung ist deaktiviert (Dienst aus).",
                _ => $"Wiederherstellungspunkt konnte nicht erstellt werden (Code {code}). Ist der Computerschutz für Laufwerk C: aktiviert?",
            };
        }
        catch (Exception ex)
        {
            return "Wiederherstellungspunkt konnte nicht erstellt werden: " + ex.Message;
        }
    });

    /// <summary>Opens the Windows System Restore wizard.</summary>
    public static void OpenSystemRestore() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rstrui.exe") { UseShellExecute = true });
}

public sealed class BackupInfo
{
    public string Folder { get; set; } = "";
    public DateTime Created { get; set; }
    public string Label { get; set; } = "";
    public int PackageCount { get; set; }
    public long SizeBytes { get; set; }
    public List<DriverPackage> Packages { get; set; } = new();

    public string SizeText => $"{SizeBytes / 1048576.0:0.#} MB";
}

/// <summary>Driver backups with pnputil: full or per-package export, restore and ZIP export for offline use.</summary>
[SupportedOSPlatform("windows")]
public sealed class DriverBackupService
{
    private readonly DriverStoreService _store;
    private readonly Func<string> _root;

    public DriverBackupService(DriverStoreService store, Func<string> backupRoot)
    {
        _store = store;
        _root = backupRoot;
    }

    public async Task<BackupInfo> BackupAsync(string label, IReadOnlyCollection<string>? publishedNames, IProgress<string>? progress, CancellationToken ct = default)
    {
        var folder = Path.Combine(_root(), $"{DateTime.Now:yyyyMMdd-HHmmss}-{Sanitize(label)}");
        Directory.CreateDirectory(folder);

        var all = await _store.GetThirdPartyPackagesAsync(ct);
        var selected = publishedNames is null
            ? all.Where(p => !p.Inbox).ToList()
            : all.Where(p => publishedNames.Contains(p.PublishedName, StringComparer.OrdinalIgnoreCase)).ToList();

        if (publishedNames is null)
        {
            progress?.Report($"Sichere alle {selected.Count} Treiberpakete …");
            var r = await _store.ExportAsync("*", folder, ct);
            if (!r.Ok) Log.Write("pnputil /export-driver *: " + r.Combined);
        }
        else
        {
            int n = 0;
            foreach (var p in selected)
            {
                progress?.Report($"Sichere {p.PublishedName} ({++n}/{selected.Count}) …");
                var r = await _store.ExportAsync(p.PublishedName, Path.Combine(folder, Path.GetFileNameWithoutExtension(p.PublishedName)), ct);
                if (!r.Ok) Log.Write($"pnputil /export-driver {p.PublishedName}: " + r.Combined);
            }
        }

        var info = new BackupInfo
        {
            Folder = folder,
            Created = DateTime.Now,
            Label = label,
            Packages = selected,
            PackageCount = selected.Count,
            SizeBytes = DriverStoreService.FolderSize(folder),
        };
        AppPaths.SaveJson(Path.Combine(folder, "itdriver-backup.json"), info);
        Log.Write($"Backup erstellt: {folder} ({info.PackageCount} Pakete)");
        return info;
    }

    public List<BackupInfo> List()
    {
        var root = _root();
        if (!Directory.Exists(root)) return new();
        return Directory.EnumerateDirectories(root)
            .Select(d => AppPaths.LoadJson<BackupInfo?>(Path.Combine(d, "itdriver-backup.json"), () => null) is { } b ? Fix(b, d) : null)
            .Where(b => b != null)
            .Select(b => b!)
            .OrderByDescending(b => b.Created)
            .ToList();

        static BackupInfo Fix(BackupInfo b, string d) { b.Folder = d; return b; }
    }

    public async Task<ProcessResult> RestoreAsync(BackupInfo backup, CancellationToken ct = default)
    {
        Log.Write($"Stelle Treiber aus {backup.Folder} wieder her");
        return await _store.AddAndInstallAsync(backup.Folder, ct);
    }

    public Task ExportZipAsync(BackupInfo backup, string zipPath) => Task.Run(() =>
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(backup.Folder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        // A small helper so the backup can be restored on a fresh Windows without ITDriver.
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var entry = zip.CreateEntry("Treiber-wiederherstellen.cmd");
        using var w = new StreamWriter(entry.Open());
        w.Write("@echo off\r\nrem ITDriver: installiert alle Treiber aus diesem Ordner (als Administrator ausfuehren)\r\n" +
                "pnputil /add-driver \"%~dp0*.inf\" /subdirs /install\r\npause\r\n");
    });

    public void Delete(BackupInfo backup)
    {
        if (Directory.Exists(backup.Folder)) Directory.Delete(backup.Folder, recursive: true);
    }

    /// <summary>Keeps the newest <paramref name="keep"/> automatic backups.</summary>
    public void Prune(int keep)
    {
        foreach (var b in List().Where(b => b.Label.StartsWith("Vor Update", StringComparison.Ordinal)).Skip(Math.Max(1, keep)))
        {
            try { Delete(b); } catch (Exception ex) { Log.Write("Backup konnte nicht gelöscht werden: " + ex.Message); }
        }
    }

    internal static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { ' ', '\\', '/', ':' }).ToHashSet();
        var cleaned = new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }
}

/// <summary>Rolls back a driver installed by ITDriver: re-adds the backed-up package and removes the new one.</summary>
[SupportedOSPlatform("windows")]
public sealed class RollbackService
{
    private readonly DriverStoreService _store;
    public RollbackService(DriverStoreService store) => _store = store;

    public async Task<(bool Ok, string Message)> RollbackAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        if (!entry.CanRollback) return (false, "Für diesen Eintrag ist kein automatisches Zurücksetzen möglich. Nutze den Wiederherstellungspunkt.");

        if (!string.IsNullOrEmpty(entry.BackupFolder) && Directory.Exists(entry.BackupFolder))
        {
            var add = await _store.AddAndInstallAsync(entry.BackupFolder, ct);
            Log.Write("Rollback add-driver: " + add.Combined);
        }

        // Removing the new package makes Windows fall back to the best remaining driver (the previous one).
        var del = await _store.DeleteAsync(entry.NewInf!, uninstall: true, ct);
        Log.Write($"Rollback delete-driver {entry.NewInf}: " + del.Combined);
        await _store.RescanDevicesAsync(ct);

        if (!del.Ok)
            return (false, $"Das neue Treiberpaket {entry.NewInf} konnte nicht entfernt werden. Details im Protokoll. Alternativ: Wiederherstellungspunkt nutzen.");
        return (true, $"Treiber für \"{entry.DeviceName}\" wurde zurückgesetzt. Ein Neustart wird empfohlen.");
    }
}
