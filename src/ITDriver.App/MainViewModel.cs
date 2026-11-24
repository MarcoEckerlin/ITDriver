using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using ITDriver.Core;

namespace ITDriver.App;

public sealed class CleanupItem : ObservableObject
{
    private bool _isSelected = true;
    public required DriverStoreCleanup.Candidate Candidate { get; init; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public string Name => Candidate.Package.PublishedName;
    public string Inf => Candidate.Package.InfFileName;
    public string? Provider => Candidate.Package.ProviderName;
    public string? Class => Candidate.Package.ClassName;
    public string? Version => Candidate.Package.Version;
    public DateTime? Date => Candidate.Package.Date;
    public string NewerVersion => $"{Candidate.Newer.Version} ({Candidate.Newer.PublishedName})";
    public string SizeText => $"{Candidate.SizeBytes / 1048576.0:0.#} MB";
}

public sealed class MainViewModel : ObservableObject
{
    private bool _isBusy;
    private string _status = "Bereit.";
    private ScanResult? _scan;
    private string _deviceFilter = "";
    private bool _onlyProblems;
    private bool _showHidden;
    private AppRelease? _availableRelease;
    private CancellationTokenSource? _cts;

    public DriverManager Manager { get; }
    public AppSettings Settings { get; }

    public ObservableCollection<DriverUpdate> Updates { get; } = new();
    public ObservableCollection<DriverUpdate> IgnoredUpdates { get; } = new();
    public ObservableCollection<DeviceInfo> Devices { get; } = new();
    public ObservableCollection<Recommendation> Recommendations { get; } = new();
    public ObservableCollection<BackupInfo> Backups { get; } = new();
    public ObservableCollection<HistoryEntry> HistoryEntries { get; } = new();
    public ObservableCollection<CleanupItem> Cleanup { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ICollectionView DevicesView { get; }

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        Manager = new DriverManager(Settings);
        DevicesView = CollectionViewSource.GetDefaultView(Devices);
        DevicesView.Filter = FilterDevice;
        Log.Written += line => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            LogLines.Insert(0, line);
            if (LogLines.Count > 300) LogLines.RemoveAt(LogLines.Count - 1);
        });
        ReloadBackups();
        ReloadHistory();
    }

    public string AppVersion => "Version " + SelfUpdater.CurrentVersion.ToString(3);
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) OnPropertyChanged(nameof(IsIdle)); } }
    public bool IsIdle => !_isBusy;
    public string Status { get => _status; set => Set(ref _status, value); }

    public ScanResult? Scan
    {
        get => _scan;
        private set
        {
            Set(ref _scan, value);
            foreach (var p in new[] { nameof(HasScan), nameof(SystemLine), nameof(SummaryTitle), nameof(SummaryText), nameof(LastScanText),
                                      nameof(UpdateBadge), nameof(ProblemBadge), nameof(RecommendationBadge), nameof(HealthColor), nameof(WindowsUpdateError) })
                OnPropertyChanged(p);
        }
    }

    public bool HasScan => _scan != null;
    public string? WindowsUpdateError => _scan?.WindowsUpdateError is { } e ? "Windows Update konnte nicht abgefragt werden: " + e : null;
    public string LastScanText => _scan is null ? "Noch kein Scan in dieser Sitzung." : $"Letzter Scan: {_scan.Time:dd.MM.yyyy HH:mm}";
    public string UpdateBadge => _scan is { Updates.Count: > 0 } s ? s.Updates.Count.ToString() : "";
    public string ProblemBadge => _scan is { ProblemCount: > 0 } s ? s.ProblemCount.ToString() : "";
    public string RecommendationBadge => _scan is { Recommendations.Count: > 0 } s ? s.Recommendations.Count.ToString() : "";

    public string SummaryTitle => _scan switch
    {
        null => "Scanne deinen PC",
        { Updates.Count: 0, ProblemCount: 0 } => "Alle Treiber sind auf dem neuesten Stand",
        var s when s.Updates.Count > 0 => $"{s.Updates.Count} Treiber-Update(s) verfügbar",
        var s => $"{s.ProblemCount} Gerät(e) mit Problemen",
    };

    public string SummaryText => _scan is null
        ? "ITDriver erkennt deine Hardware, findet fehlende und veraltete Treiber über Windows Update und zeigt dir die offiziellen Herstellerquellen."
        : $"{_scan.Devices.Count(d => d.Present)} Geräte erkannt · {_scan.ProblemCount} mit Problemen · {_scan.Recommendations.Count} Empfehlungen";

    public string HealthColor => _scan switch
    {
        null => "#0B62D6",
        { Updates.Count: 0, ProblemCount: 0 } => "#1E8E3E",
        { ProblemCount: > 0 } => "#C62828",
        _ => "#C77700",
    };

    public string SystemLine => _scan?.System is { } s
        ? $"{s.OS} (Build {s.OSBuild})\n{s.Manufacturer} {s.Model}\nMainboard: {s.Board}\nBIOS: {s.Bios}\nCPU: {s.Cpu}\nRAM: {s.RamGB} GB\nGrafik: {string.Join(", ", s.Gpus)}"
        : "";

    public string DeviceFilter { get => _deviceFilter; set { if (Set(ref _deviceFilter, value)) DevicesView.Refresh(); } }
    public bool OnlyProblems { get => _onlyProblems; set { if (Set(ref _onlyProblems, value)) DevicesView.Refresh(); } }
    public bool ShowHidden { get => _showHidden; set { if (Set(ref _showHidden, value)) DevicesView.Refresh(); } }

    public AppRelease? AvailableRelease
    {
        get => _availableRelease;
        set { Set(ref _availableRelease, value); OnPropertyChanged(nameof(HasAppUpdate)); OnPropertyChanged(nameof(AppUpdateText)); }
    }
    public bool HasAppUpdate => _availableRelease != null;
    public string AppUpdateText => _availableRelease is { } r ? $"Neue Version {r.Version.ToString(3)} verfügbar" : "";

    public long CleanupBytes => Cleanup.Where(c => c.IsSelected).Sum(c => c.Candidate.SizeBytes);
    public string CleanupSummary => Cleanup.Count == 0 ? "" : $"{Cleanup.Count} alte Pakete gefunden · ausgewählt: {CleanupBytes / 1048576.0:0.#} MB";

    private bool FilterDevice(object o)
    {
        if (o is not DeviceInfo d) return false;
        if (!_showHidden && !d.Present) return false;
        if (_onlyProblems && !d.HasProblem) return false;
        if (string.IsNullOrWhiteSpace(_deviceFilter)) return true;
        var f = _deviceFilter.Trim();
        return d.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
               || (d.Class?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false)
               || d.VendorName.Contains(f, StringComparison.OrdinalIgnoreCase)
               || d.HardwareIds.Any(h => h.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Runs a long operation with busy state, status text, cancellation and error reporting.</summary>
    public async Task RunAsync(string status, Func<CancellationToken, Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = status;
        _cts = new CancellationTokenSource();
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Abgebrochen.";
        }
        catch (Exception ex)
        {
            Log.Write("Fehler: " + ex);
            Status = "Fehler: " + ex.Message;
            MessageBox.Show(ex.Message, "ITDriver", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    public void Cancel() => _cts?.Cancel();

    private IProgress<string> Progress => new Progress<string>(s => Status = s);

    public Task ScanAsync() => RunAsync("Scan läuft …", async ct =>
    {
        var scan = await Manager.ScanAsync(Progress, true, ct);
        Fill(Updates, scan.Updates.OrderByDescending(u => u.IsMissingDriver).ThenBy(u => u.DisplayName));
        Fill(IgnoredUpdates, scan.IgnoredUpdates);
        Fill(Devices, scan.Devices);
        Fill(Recommendations, scan.Recommendations);
        Scan = scan;
        Status = scan.WindowsUpdateError is null
            ? $"Scan abgeschlossen: {scan.Updates.Count} Updates, {scan.ProblemCount} Probleme."
            : "Scan abgeschlossen, aber Windows Update war nicht erreichbar.";
    });

    public Task InstallAsync(IReadOnlyList<DriverUpdate> selected) => RunAsync("Installation läuft …", async ct =>
    {
        if (selected.Count == 0) { Status = "Keine Updates ausgewählt."; return; }
        var dispatcher = Application.Current.Dispatcher;
        var outcomes = await Manager.InstallAsync(selected, (u, s) => dispatcher.BeginInvoke(() => u.State = s), Progress, ct);
        ReloadHistory();
        ReloadBackups();
        var ok = outcomes.Count(o => o.Success);
        var reboot = outcomes.Any(o => o.RebootRequired);
        Status = $"{ok} von {outcomes.Count} Updates installiert." + (reboot ? " Neustart erforderlich." : "");
        foreach (var u in selected.Where(u => outcomes.Any(o => o.UpdateId == u.UpdateId && o.Success)).ToList())
            Updates.Remove(u);
        Scan = Scan; // refresh badges
        if (reboot && MessageBox.Show("Einige Treiber werden erst nach einem Neustart aktiv. Jetzt neu starten?", "ITDriver",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { CreateNoWindow = true, UseShellExecute = false });
    });

    public void Ignore(DriverUpdate u)
    {
        if (!Settings.IgnoredUpdates.Contains(u.UpdateId)) Settings.IgnoredUpdates.Add(u.UpdateId);
        Settings.Save();
        Updates.Remove(u);
        IgnoredUpdates.Add(u);
        Status = $"\"{u.Title}\" wird ignoriert.";
    }

    public void Unignore(DriverUpdate u)
    {
        Settings.IgnoredUpdates.RemoveAll(i => i.Equals(u.UpdateId, StringComparison.OrdinalIgnoreCase) || i.Equals(u.Title, StringComparison.OrdinalIgnoreCase));
        Settings.Save();
        IgnoredUpdates.Remove(u);
        Updates.Add(u);
    }

    public Task BackupAllAsync() => RunAsync("Sichere alle Treiber …", async ct =>
    {
        var b = await Manager.Backups.BackupAsync("Vollständig", null, Progress, ct);
        ReloadBackups();
        Status = $"Backup erstellt: {b.PackageCount} Treiberpakete, {b.SizeText}.";
    });

    public Task BackupDeviceAsync(DeviceInfo d) => RunAsync($"Sichere Treiber von {d.Name} …", async ct =>
    {
        if (d.InfName is null || !d.InfName.StartsWith("oem", StringComparison.OrdinalIgnoreCase))
        {
            Status = "Dieses Gerät nutzt einen in Windows enthaltenen Treiber – ein Backup ist nicht nötig.";
            return;
        }
        var b = await Manager.Backups.BackupAsync(d.Name, new[] { d.InfName }, Progress, ct);
        ReloadBackups();
        Status = $"Backup erstellt: {b.Folder}";
    });

    public Task RestoreBackupAsync(BackupInfo b) => RunAsync("Stelle Treiber wieder her …", async ct =>
    {
        if (Settings.CreateRestorePoint)
        {
            Status = "Erstelle Wiederherstellungspunkt …";
            await Manager.RestorePoints.CreateAsync("ITDriver: Treiber aus Backup wiederherstellen");
        }
        var r = await Manager.Backups.RestoreAsync(b, ct);
        History.Add(new HistoryEntry { Action = "Wiederhergestellt", Title = $"Backup {b.Created:dd.MM.yyyy HH:mm} ({b.Label})", Success = r.Ok || r.ExitCode == 3010, Message = r.Combined.Trim() });
        ReloadHistory();
        Status = r.Ok || r.ExitCode == 3010 ? "Treiber wurden wiederhergestellt. Ein Neustart wird empfohlen." : "Wiederherstellung mit Fehlern beendet – Details im Verlauf.";
    });

    public Task CreateRestorePointAsync() => RunAsync("Erstelle Wiederherstellungspunkt …", async _ =>
    {
        var err = await Manager.RestorePoints.CreateAsync("ITDriver: manueller Wiederherstellungspunkt");
        Status = err ?? "Wiederherstellungspunkt erstellt (Windows erstellt höchstens einen pro 24 Stunden).";
    });

    public Task AnalyzeCleanupAsync() => RunAsync("Analysiere Treiberspeicher …", async ct =>
    {
        var packages = await Manager.Store.GetThirdPartyPackagesAsync(ct);
        var devices = Scan?.Devices ?? await Task.Run(Manager.Scanner.ScanDevices, ct);
        var inUse = devices.Where(d => d.InfName != null).Select(d => d.InfName!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = await Task.Run(() => DriverStoreCleanup.FindCandidates(packages, inUse, p => DriverStoreService.FolderSize(p.RepositoryFolder)), ct);
        Cleanup.Clear();
        foreach (var c in candidates)
        {
            var item = new CleanupItem { Candidate = c };
            item.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(CleanupSummary)); };
            Cleanup.Add(item);
        }
        OnPropertyChanged(nameof(CleanupSummary));
        Status = candidates.Count == 0 ? "Keine überflüssigen Treiberpakete gefunden." : $"{candidates.Count} alte Treiberpakete gefunden.";
    });

    public Task RunCleanupAsync() => RunAsync("Entferne alte Treiberpakete …", async ct =>
    {
        var selected = Cleanup.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0) return;
        int ok = 0;
        foreach (var c in selected)
        {
            Status = $"Entferne {c.Name} …";
            // No /uninstall and no /force: Windows refuses to delete a package that is still in use.
            var r = await Manager.Store.DeleteAsync(c.Name, uninstall: false, ct);
            Log.Write($"delete-driver {c.Name}: {r.Combined.Trim()}");
            if (r.Ok) { ok++; Cleanup.Remove(c); }
        }
        History.Add(new HistoryEntry { Action = "Bereinigt", Title = $"{ok} alte Treiberpakete entfernt", Success = ok > 0 });
        ReloadHistory();
        OnPropertyChanged(nameof(CleanupSummary));
        Status = $"{ok} von {selected.Count} Paketen entfernt.";
    });

    public Task RollbackAsync(HistoryEntry entry) => RunAsync("Setze Treiber zurück …", async ct =>
    {
        var (ok, msg) = await Manager.RollbackAsync(entry, ct);
        ReloadHistory();
        Status = msg;
        MessageBox.Show(msg, "ITDriver", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    });

    public async Task CheckAppUpdateAsync(bool silent)
    {
        try
        {
            var latest = await new SelfUpdater().GetLatestAsync();
            if (latest != null && SelfUpdater.IsNewer(latest, SelfUpdater.CurrentVersion))
            {
                AvailableRelease = latest;
                if (!silent) Status = AppUpdateText;
            }
            else if (!silent)
            {
                Status = "ITDriver ist aktuell.";
                MessageBox.Show($"Du verwendest die neueste Version ({SelfUpdater.CurrentVersion.ToString(3)}).", "ITDriver", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Update-Prüfung fehlgeschlagen: " + ex.Message);
            if (!silent) MessageBox.Show("Update-Prüfung fehlgeschlagen: " + ex.Message, "ITDriver", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public Task InstallAppUpdateAsync() => RunAsync("Aktualisiere ITDriver …", async ct =>
    {
        if (AvailableRelease is null) return;
        var notes = string.IsNullOrWhiteSpace(AvailableRelease.Notes) ? "" : "\n\nNeu:\n" + (AvailableRelease.Notes.Length > 600 ? AvailableRelease.Notes[..600] + " …" : AvailableRelease.Notes);
        if (MessageBox.Show($"ITDriver {AvailableRelease.Version.ToString(3)} installieren? Die App startet danach neu.{notes}", "ITDriver-Update",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await new SelfUpdater().DownloadAndApplyAsync(AvailableRelease, Progress, ct);
        Application.Current.Shutdown();
    });

    public void ReloadBackups() => Fill(Backups, Manager.Backups.List());
    public void ReloadHistory() => Fill(HistoryEntries, History.Load());

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    public static void Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            // explorer.exe opens web links in the user's normal (non-elevated) browser.
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + url + "\"") { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write("Konnte nicht öffnen: " + ex.Message); }
    }
}
