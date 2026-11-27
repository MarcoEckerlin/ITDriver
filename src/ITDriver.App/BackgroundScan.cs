using System.Diagnostics;
using ITDriver.Core;
using WinForms = System.Windows.Forms;

namespace ITDriver.App;

/// <summary>Scheduled scan: runs without a window and shows a tray notification if something needs attention.</summary>
internal static class BackgroundScan
{
    public static async Task RunAsync()
    {
        ScanResult scan;
        try
        {
            scan = await new DriverManager(AppSettings.Load()).ScanAsync(null);
        }
        catch (Exception ex)
        {
            Log.Write("Geplanter Scan fehlgeschlagen: " + ex.Message);
            return;
        }

        // Also look for a new ITDriver version; it is installed when the app is opened (asks or installs per setting).
        AppRelease? appUpdate = null;
        var settings = AppSettings.Load();
        if (settings.AutoCheckAppUpdates)
        {
            try
            {
                var latest = await new SelfUpdater().GetLatestAsync();
                if (SelfUpdater.Decide(latest, SelfUpdater.CurrentVersion, settings) != SelfUpdater.AutoAction.None) appUpdate = latest;
            }
            catch (Exception ex) { Log.Write("Update-Prüfung im Hintergrund fehlgeschlagen: " + ex.Message); }
        }

        var important = scan.Recommendations.Count(r => r.Severity == Severity.Important);
        if (scan.Updates.Count == 0 && scan.ProblemCount == 0 && important == 0 && appUpdate is null) return;

        var text = scan.Updates.Count > 0
            ? $"{scan.Updates.Count} Treiber-Update(s) verfügbar."
            : "";
        if (scan.ProblemCount > 0) text += $" {scan.ProblemCount} Gerät(e) mit Problemen.";
        if (appUpdate != null) text += $" ITDriver {appUpdate.Version.ToString(3)} ist verfügbar.";
        text += " Klicken zum Öffnen.";

        using var icon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Visible = true,
            Text = "ITDriver",
        };
        var done = new TaskCompletionSource();
        void Open()
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }); } catch { }
            done.TrySetResult();
        }
        icon.BalloonTipClicked += (_, _) => Open();
        icon.Click += (_, _) => Open();
        icon.ShowBalloonTip(15000, "ITDriver", text.Trim(), WinForms.ToolTipIcon.Info);
        // Keep the tray icon for a while so the user can click it.
        await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(10)));
        icon.Visible = false;
    }
}
