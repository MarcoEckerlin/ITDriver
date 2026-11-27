using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ITDriver.Core;
using Microsoft.Win32;

namespace ITDriver.App;

/// <summary>Static values for XAML combo boxes.</summary>
public static class Schedules
{
    public static readonly ScanSchedule Off = ScanSchedule.Off, Daily = ScanSchedule.Daily, Weekly = ScanSchedule.Weekly, Monthly = ScanSchedule.Monthly;
    public static readonly DayOfWeek Mon = DayOfWeek.Monday, Tue = DayOfWeek.Tuesday, Wed = DayOfWeek.Wednesday, Thu = DayOfWeek.Thursday,
        Fri = DayOfWeek.Friday, Sat = DayOfWeek.Saturday, Sun = DayOfWeek.Sunday;
}

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow(bool justUpdated = false)
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) =>
        {
            if (!File.Exists(AppPaths.Settings))
            {
                // First start: store defaults and register the weekly background scan.
                try
                {
                    _vm.Settings.Save();
                    await new SchedulerService().ApplyAsync(_vm.Settings, Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ITDriver.exe"));
                }
                catch (Exception ex) { Log.Write("Erststart-Einrichtung: " + ex.Message); }
            }
            if (justUpdated) _vm.Status = $"ITDriver wurde auf Version {SelfUpdater.CurrentVersion.ToString(3)} aktualisiert.";
            if (_vm.Settings.AutoCheckAppUpdates) _ = _vm.CheckAppUpdateAsync(silent: true);
            // Keep checking while ITDriver stays open (every 6 hours).
            var updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            updateTimer.Tick += async (_, _) => { if (_vm.Settings.AutoCheckAppUpdates) await _vm.CheckAppUpdateAsync(silent: true); };
            updateTimer.Start();
            if (_vm.Settings.ScanOnStartup) await _vm.ScanAsync();
        };
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Pages is null || sender is not RadioButton rb) return;
        Pages.SelectedIndex = Nav.Children.IndexOf(rb);
    }

    private void Navigate(int index) => ((RadioButton)Nav.Children[index]).IsChecked = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => _vm.Cancel();

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ScanAsync();
        if (_vm.Scan is { Updates.Count: > 0 } && Pages.SelectedIndex == 0) Navigate(1);
    }

    private async void InstallAll_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Updates.Count == 0)
        {
            _vm.Status = "Keine Updates zum Installieren.";
            return;
        }
        foreach (var u in _vm.Updates) u.IsSelected = true;
        Navigate(1);
        await InstallWithConfirm(_vm.Updates.ToList());
    }

    private async void InstallSelected_Click(object sender, RoutedEventArgs e)
        => await InstallWithConfirm(_vm.Updates.Where(u => u.IsSelected).ToList());

    private async Task InstallWithConfirm(List<DriverUpdate> list)
    {
        if (list.Count == 0) { _vm.Status = "Keine Updates ausgewählt."; return; }
        var text = $"{list.Count} Treiber-Update(s) installieren?\n\n" + string.Join("\n", list.Take(12).Select(u => "• " + u.DisplayName)) +
                   (list.Count > 12 ? "\n…" : "") +
                   (_vm.Settings.CreateRestorePoint ? "\n\nVorher wird ein Wiederherstellungspunkt erstellt." : "");
        if (MessageBox.Show(text, "ITDriver", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await _vm.InstallAsync(list);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var u in _vm.Updates) u.IsSelected = true; }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { foreach (var u in _vm.Updates) u.IsSelected = false; }

    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        if (UpdatesGrid.SelectedItem is DriverUpdate u) _vm.Ignore(u);
        else _vm.Status = "Bitte zuerst ein Update in der Liste markieren.";
    }

    private void Unignore_Click(object sender, RoutedEventArgs e)
    {
        if (IgnoredList.SelectedItem is DriverUpdate u) _vm.Unignore(u);
    }

    private void UpdateInfo_Click(object sender, RoutedEventArgs e)
    {
        if (UpdatesGrid.SelectedItem is not DriverUpdate u) { _vm.Status = "Bitte zuerst ein Update markieren."; return; }
        if (u.Source != UpdateSource.WindowsUpdate && u.InfoUrl != null) { MainViewModel.Open(u.InfoUrl); return; }
        var device = _vm.Devices.FirstOrDefault(d => d.InstanceId == u.DeviceInstanceId);
        MainViewModel.Open(device?.CatalogSearchUrl ?? "https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(u.Title));
    }

    private DeviceInfo? SelectedDevice()
    {
        if (DevicesGrid.SelectedItem is DeviceInfo d) return d;
        _vm.Status = "Bitte zuerst ein Gerät markieren.";
        return null;
    }

    private void DeviceVendor_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice() is not { } d) return;
        if (d.Vendor is { } v) MainViewModel.Open(v.DownloadUrl);
        else
        {
            _vm.Status = "Hersteller nicht in der Liste – öffne den Microsoft Update-Katalog.";
            MainViewModel.Open(d.CatalogSearchUrl);
        }
    }

    private void DeviceCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice() is { } d) MainViewModel.Open(d.CatalogSearchUrl);
    }

    private async void DeviceBackup_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice() is { } d) await _vm.BackupDeviceAsync(d);
    }

    private void DeviceCopy_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice() is not { } d) return;
        Clipboard.SetText($"{d.Name}\r\n{d.InstanceId}\r\n" + string.Join("\r\n", d.HardwareIds));
        _vm.Status = "Hardware-IDs in die Zwischenablage kopiert.";
    }

    private void DevMgmt_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });

    private void OpenUrl_Click(object sender, RoutedEventArgs e) => MainViewModel.Open((sender as FrameworkElement)?.Tag as string);

    private async void BackupAll_Click(object sender, RoutedEventArgs e)
    {
        await _vm.BackupAllAsync();
        Navigate(4);
    }

    private BackupInfo? SelectedBackup()
    {
        if (BackupsGrid.SelectedItem is BackupInfo b) return b;
        _vm.Status = "Bitte zuerst ein Backup markieren.";
        return null;
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBackup() is not { } b) return;
        if (MessageBox.Show($"Alle {b.PackageCount} Treiber aus dem Backup vom {b.Created:dd.MM.yyyy HH:mm} installieren?", "ITDriver",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await _vm.RestoreBackupAsync(b);
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBackup() is not { } b) return;
        var dlg = new SaveFileDialog { Filter = "ZIP-Archiv|*.zip", FileName = $"ITDriver-Backup-{b.Created:yyyyMMdd-HHmm}.zip" };
        if (dlg.ShowDialog(this) != true) return;
        await _vm.RunAsync("Exportiere Backup …", async _ =>
        {
            await _vm.Manager.Backups.ExportZipAsync(b, dlg.FileName);
            _vm.Status = "Backup exportiert: " + dlg.FileName;
        });
    }

    private void OpenBackup_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBackup() is { } b) MainViewModel.Open(b.Folder);
    }

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBackup() is not { } b) return;
        if (MessageBox.Show("Backup endgültig löschen?", "ITDriver", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _vm.Manager.Backups.Delete(b);
        _vm.ReloadBackups();
    }

    private async void RestorePoint_Click(object sender, RoutedEventArgs e) => await _vm.CreateRestorePointAsync();
    private void SystemRestore_Click(object sender, RoutedEventArgs e) => RestorePointService.OpenSystemRestore();

    private async void AnalyzeCleanup_Click(object sender, RoutedEventArgs e) => await _vm.AnalyzeCleanupAsync();

    private async void RunCleanup_Click(object sender, RoutedEventArgs e)
    {
        var count = _vm.Cleanup.Count(c => c.IsSelected);
        if (count == 0) { _vm.Status = "Nichts ausgewählt – zuerst analysieren."; return; }
        if (MessageBox.Show($"{count} alte Treiberpakete aus dem Treiberspeicher entfernen?", "ITDriver",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await _vm.RunCleanupAsync();
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryEntry h) { _vm.Status = "Bitte zuerst einen Eintrag markieren."; return; }
        if (!h.CanRollback)
        {
            if (MessageBox.Show("Für diesen Eintrag ist kein automatisches Zurücksetzen möglich. Systemwiederherstellung öffnen?", "ITDriver",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                RestorePointService.OpenSystemRestore();
            return;
        }
        if (MessageBox.Show($"Treiber für \"{h.DeviceName}\" auf Version {h.PreviousVersion} zurücksetzen?", "ITDriver",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await _vm.RollbackAsync(h);
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        MainViewModel.Open(AppPaths.Logs);
    }

    private void BrowseBackup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Backup-Ordner wählen" };
        if (dlg.ShowDialog(this) == true)
        {
            _vm.Settings.BackupFolder = dlg.FolderName;
            // Refresh the bound TextBox.
            DataContext = null;
            DataContext = _vm;
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _vm.Settings.Save();
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ITDriver.exe");
        var (ok, msg) = await new SchedulerService().ApplyAsync(_vm.Settings, exe);
        _vm.ReloadBackups();
        _vm.Status = "Einstellungen gespeichert. " + msg;
        if (!ok) MessageBox.Show(msg, "ITDriver", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e) => await _vm.CheckAppUpdateAsync(silent: false);

    private async void InstallAppUpdate_Click(object sender, RoutedEventArgs e) => await _vm.InstallAppUpdateAsync();

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Scan is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "HTML-Bericht|*.html|JSON (Inventar)|*.json",
            FileName = $"ITDriver-Bericht-{DateTime.Now:yyyyMMdd-HHmm}",
        };
        if (dlg.ShowDialog(this) != true) return;
        if (dlg.FilterIndex == 2) ReportExporter.ExportJson(_vm.Scan, dlg.FileName);
        else ReportExporter.ExportHtml(_vm.Scan, dlg.FileName);
        _vm.Status = "Bericht gespeichert: " + dlg.FileName;
        if (dlg.FilterIndex != 2) MainViewModel.Open(dlg.FileName);
    }
}
