namespace ITDriver.Core;

public enum ScanSchedule { Off, Daily, Weekly, Monthly }

public sealed class AppSettings
{
    public bool CreateRestorePoint { get; set; } = true;
    public bool BackupBeforeInstall { get; set; } = true;
    public bool AutoCheckAppUpdates { get; set; } = true;
    public bool ScanOnStartup { get; set; } = false;
    public bool ShowVendorRecommendations { get; set; } = true;
    /// <summary>Also search the Microsoft Update Catalog per device for newer drivers than Windows Update offers.</summary>
    public bool SearchCatalog { get; set; } = true;
    public int OldDriverYears { get; set; } = 3;
    public ScanSchedule Schedule { get; set; } = ScanSchedule.Weekly;
    public string ScheduleTime { get; set; } = "12:00";
    public DayOfWeek ScheduleDay { get; set; } = DayOfWeek.Monday;
    public string? BackupFolder { get; set; }
    public int KeepBackups { get; set; } = 10;
    /// <summary>Windows Update IDs or titles the user chose to ignore.</summary>
    public List<string> IgnoredUpdates { get; set; } = new();

    public string EffectiveBackupFolder => string.IsNullOrWhiteSpace(BackupFolder) ? AppPaths.DefaultBackupRoot : BackupFolder!;

    public static AppSettings Load() => AppPaths.LoadJson(AppPaths.Settings, () => new AppSettings());
    public void Save() => AppPaths.SaveJson(AppPaths.Settings, this);

    public bool IsIgnored(DriverUpdate u) =>
        IgnoredUpdates.Any(i => i.Equals(u.UpdateId, StringComparison.OrdinalIgnoreCase) || i.Equals(u.Title, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One entry in the installation history, with everything needed for a rollback.</summary>
public sealed class HistoryEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Action { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? DeviceInstanceId { get; set; }
    public string? DeviceName { get; set; }
    public string? PreviousVersion { get; set; }
    public string? PreviousInf { get; set; }
    public string? NewVersion { get; set; }
    public string? NewInf { get; set; }
    public string? BackupFolder { get; set; }
    public bool RestorePointCreated { get; set; }
    public bool RolledBack { get; set; }

    public bool CanRollback => Success && !RolledBack && Action == "Installiert"
                               && !string.IsNullOrEmpty(NewInf) && NewInf!.StartsWith("oem", StringComparison.OrdinalIgnoreCase)
                               && !string.Equals(NewInf, PreviousInf, StringComparison.OrdinalIgnoreCase);
}

public static class History
{
    public static List<HistoryEntry> Load() => AppPaths.LoadJson(AppPaths.History, () => new List<HistoryEntry>());

    public static void Add(HistoryEntry entry)
    {
        var all = Load();
        all.Insert(0, entry);
        if (all.Count > 500) all.RemoveRange(500, all.Count - 500);
        AppPaths.SaveJson(AppPaths.History, all);
    }

    public static void Save(List<HistoryEntry> entries) => AppPaths.SaveJson(AppPaths.History, entries);
}
