using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ITDriver.Core;

/// <summary>
/// Finds, downloads and installs driver updates through the official Windows Update Agent API
/// (the same source as "Optional updates" in Windows Settings, backed by the Microsoft Update Catalog).
/// All COM work for one operation happens on one dedicated STA thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateService
{
    private const string ClientId = "ITDriver";

    public Task<List<DriverUpdate>> SearchAsync(IReadOnlyList<DeviceInfo> devices, CancellationToken ct = default)
        => RunSta(() => Search(devices, ct), ct);

    private static List<DriverUpdate> Search(IReadOnlyList<DeviceInfo> devices, CancellationToken ct)
    {
        dynamic session = CreateSession();
        dynamic searcher = session.CreateUpdateSearcher();
        searcher.Online = true;
        // Also search Microsoft Update (not only WSUS) where policy allows; ServerSelection 2 = ssWindowsUpdate.
        try { searcher.ServerSelection = 2; } catch { }
        dynamic result = searcher.Search("IsInstalled=0 and Type='Driver'");
        ct.ThrowIfCancellationRequested();

        var byHwid = BuildHardwareIdIndex(devices);
        var list = new List<DriverUpdate>();
        int count = result.Updates.Count;
        for (int i = 0; i < count; i++)
        {
            dynamic u = result.Updates.Item(i);
            var update = new DriverUpdate
            {
                UpdateId = (string)u.Identity.UpdateID,
                Title = (string)u.Title,
                DriverClass = Safe(() => (string)u.DriverClass),
                DriverModel = Safe(() => (string)u.DriverModel),
                Provider = Safe(() => (string)u.DriverProvider),
                DriverDate = Safe(() => (DateTime?)(DateTime)u.DriverVerDate),
                HardwareId = Safe(() => (string)u.DriverHardwareID),
                SizeBytes = Safe(() => Convert.ToInt64((decimal)u.MaxDownloadSize)),
                InfoUrl = Safe(() => (string)u.SupportUrl),
                Source = UpdateSource.WindowsUpdate,
            };
            Match(update, byHwid);
            list.Add(update);
        }
        return list;
    }

    internal static Dictionary<string, DeviceInfo> BuildHardwareIdIndex(IEnumerable<DeviceInfo> devices)
    {
        var index = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in devices)
            foreach (var id in d.HardwareIds.Concat(d.CompatibleIds))
                index.TryAdd(id, d);
        return index;
    }

    internal static void Match(DriverUpdate update, Dictionary<string, DeviceInfo> byHwid)
    {
        if (update.HardwareId is null || !byHwid.TryGetValue(update.HardwareId, out var dev)) return;
        update.DeviceInstanceId = dev.InstanceId;
        update.DeviceName = dev.Name;
        update.InstalledVersion = dev.DriverVersion;
        update.InstalledDate = dev.DriverDate;
        update.InstalledInf = dev.InfName;
        update.IsMissingDriver = dev.IsMissingDriver;
    }

    /// <summary>Downloads and installs one update. Returns the outcome; never throws for WU errors.</summary>
    public Task<InstallOutcome> InstallAsync(DriverUpdate update, IProgress<string>? progress, CancellationToken ct = default)
        => RunSta(() => Install(update, progress), ct);

    private static InstallOutcome Install(DriverUpdate update, IProgress<string>? progress)
    {
        var outcome = new InstallOutcome { UpdateId = update.UpdateId, Title = update.Title };
        try
        {
            dynamic session = CreateSession();
            dynamic searcher = session.CreateUpdateSearcher();
            try { searcher.ServerSelection = 2; } catch { }
            dynamic found = searcher.Search($"UpdateID='{update.UpdateId}'");
            if (found.Updates.Count == 0)
            {
                outcome.Message = "Update wird von Windows Update nicht mehr angeboten.";
                return outcome;
            }
            dynamic u = found.Updates.Item(0);
            if (!(bool)u.EulaAccepted) u.AcceptEula();

            dynamic coll = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)!;
            coll.Add(u);

            progress?.Report("Wird heruntergeladen …");
            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = coll;
            dynamic dl = downloader.Download();
            if ((int)dl.ResultCode is not (2 or 3))
            {
                outcome.Message = $"Download fehlgeschlagen (Code {(int)dl.ResultCode}, 0x{(int)dl.HResult:X8}).";
                return outcome;
            }

            progress?.Report("Wird installiert …");
            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = coll;
            try { installer.ForceQuiet = true; } catch { }
            dynamic res = installer.Install();
            dynamic r = res.GetUpdateResult(0);
            int code = (int)r.ResultCode; // 2 succeeded, 3 succeeded with errors, 4 failed, 5 aborted
            outcome.Success = code is 2 or 3;
            outcome.RebootRequired = (bool)res.RebootRequired || (bool)r.RebootRequired;
            outcome.Message = outcome.Success
                ? (outcome.RebootRequired ? "Installiert – Neustart erforderlich." : "Erfolgreich installiert.")
                : $"Installation fehlgeschlagen (Code {code}, 0x{(int)r.HResult:X8}).";
        }
        catch (COMException ex)
        {
            outcome.Message = $"Windows Update Fehler 0x{ex.HResult:X8}: {WuErrors.Describe(ex.HResult)}";
        }
        catch (Exception ex)
        {
            outcome.Message = ex.Message;
        }
        return outcome;
    }

    /// <summary>Hides an update in Windows Update so it is no longer offered (also hides it from Windows Settings).</summary>
    public Task<bool> SetHiddenAsync(string updateId, bool hidden, CancellationToken ct = default)
        => RunSta(() =>
        {
            dynamic session = CreateSession();
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic found = searcher.Search($"UpdateID='{updateId}'");
            if (found.Updates.Count == 0) return false;
            found.Updates.Item(0).IsHidden = hidden;
            return true;
        }, ct);

    private static dynamic CreateSession()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session") ?? throw new InvalidOperationException("Windows Update Agent ist nicht verfügbar.");
        dynamic session = Activator.CreateInstance(type)!;
        try { session.ClientApplicationID = ClientId; } catch { }
        return session;
    }

    private static T? Safe<T>(Func<T> f)
    {
        try { return f(); } catch { return default; }
    }

    internal static Task<T> RunSta<T>(Func<T> work, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }) { IsBackground = true, Name = "ITDriver-WUA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }
}

public static class WuErrors
{
    public static string Describe(int hresult) => unchecked((uint)hresult) switch
    {
        0x8024402C => "Keine Verbindung zum Update-Server (Proxy/Firewall?).",
        0x80072EE2 => "Zeitüberschreitung bei der Verbindung.",
        0x80072EFD or 0x80072EFE => "Verbindung zum Update-Server nicht möglich.",
        0x8024001E => "Der Dienst wurde beendet.",
        0x80240024 => "Es gibt keine Updates.",
        0x8024000B => "Vorgang wurde abgebrochen.",
        0x80070005 => "Zugriff verweigert – bitte als Administrator starten.",
        0x8024002E => "Windows Update ist per Richtlinie deaktiviert.",
        0x80240438 => "Windows Update ist durch eine Richtlinie blockiert (z. B. WSUS).",
        _ => "Unbekannter Fehler.",
    };
}
