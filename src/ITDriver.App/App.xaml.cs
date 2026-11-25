using System.Runtime.InteropServices;
using System.Windows;
using ITDriver.Core;

namespace ITDriver.App;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write("Unbehandelter Fehler: " + ex.Exception);
            MessageBox.Show(ex.Exception.Message, "ITDriver – Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        var args = e.Args;
        if (args.Length > 0)
        {
            var cmd = args[0].ToLowerInvariant();
            if (cmd is "--background-scan")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                await BackgroundScan.RunAsync();
                Shutdown();
                return;
            }
            if (cmd is "--export-inventory" or "--report")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AttachConsole(-1);
                var path = args.Length > 1 ? args[1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    cmd == "--report" ? $"ITDriver-Bericht-{DateTime.Now:yyyyMMdd-HHmm}.html" : $"ITDriver-Inventar-{DateTime.Now:yyyyMMdd-HHmm}.json");
                var withWu = !args.Contains("--no-windows-update", StringComparer.OrdinalIgnoreCase);
                try
                {
                    var manager = new DriverManager(AppSettings.Load());
                    var scan = await manager.ScanAsync(new Progress<string>(Console.WriteLine), withWu);
                    if (cmd == "--report") ReportExporter.ExportHtml(scan, path); else ReportExporter.ExportJson(scan, path);
                    Console.WriteLine($"Gespeichert: {path}");
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Shutdown(1);
                }
                return;
            }
        }

        var window = new MainWindow(justUpdated: args.Contains("--updated", StringComparer.OrdinalIgnoreCase));
        MainWindow = window;
        window.Show();
    }
}
