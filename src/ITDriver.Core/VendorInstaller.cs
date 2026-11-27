using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ITDriver.Core;

/// <summary>Vendor download servers ITDriver accepts, with the publisher their installers must be signed by.</summary>
public static class VendorInstallerHosts
{
    private static readonly Dictionary<string, string?> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["drivers.amd.com"] = "Advanced Micro Devices",
        ["dlcdnets.asus.com"] = null, // ZIPs with INF drivers; pnputil checks the driver signatures
    };

    public static bool IsTrusted(string url, out string? requiredPublisher)
    {
        requiredPublisher = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        return Hosts.TryGetValue(uri.Host, out requiredPublisher);
    }
}

/// <summary>
/// Installs vendor packages without wizards: downloads only from the vendor's own servers, checks the
/// digital signature (AMD installers must be validly signed by AMD), then runs the installer silently
/// or adds the INF drivers with pnputil.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VendorInstaller
{
    private readonly HttpClient _http;

    public VendorInstaller(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36 ITDriver/" + SelfUpdater.CurrentVersion);
    }

    /// <summary>Silent switches of the vendor installers.</summary>
    internal static string SilentArguments(VendorInstallKind kind) => kind switch
    {
        VendorInstallKind.AmdChipset => "/S",       // AMD Chipset Software: unattended install
        VendorInstallKind.AmdAdrenalin => "",       // Adrenalin has no documented silent switch: AMD's own installer opens and ITDriver waits for it
        _ => "",
    };

    public async Task<InstallOutcome> InstallAsync(DriverUpdate update, IProgress<string>? progress, CancellationToken ct = default)
    {
        var outcome = new InstallOutcome { UpdateId = update.UpdateId, Title = update.Title };
        if (update.DownloadUrl is null || !VendorInstallerHosts.IsTrusted(update.DownloadUrl, out var publisher))
        {
            outcome.Message = "Keine vertrauenswürdige Download-Adresse des Herstellers.";
            return outcome;
        }
        var work = Path.Combine(Path.GetTempPath(), "ITDriver-Vendor", Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(work);
            var uri = new Uri(update.DownloadUrl);
            var file = Path.Combine(work, Path.GetFileName(uri.LocalPath));
            progress?.Report("Wird heruntergeladen …");
            using (var req = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                // AMD only serves downloads that come from its own support pages.
                if (uri.Host.EndsWith("amd.com", StringComparison.OrdinalIgnoreCase)) req.Headers.Referrer = new Uri("https://www.amd.com/");
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                if (resp.Content.Headers.ContentType?.MediaType is "text/html") throw new InvalidOperationException("Der Hersteller-Server hat statt der Datei eine Webseite geliefert.");
                await using var fs = File.Create(file);
                await resp.Content.CopyToAsync(fs, ct);
            }

            if (update.VendorInstall is VendorInstallKind.AmdChipset or VendorInstallKind.AmdAdrenalin)
            {
                progress?.Report("Prüfe digitale Signatur …");
                var signer = Authenticode.VerifiedSigner(file);
                if (signer is null || publisher is null || !signer.Contains(publisher, StringComparison.OrdinalIgnoreCase))
                {
                    outcome.Message = "Signaturprüfung fehlgeschlagen – Installation abgebrochen" + (signer is null ? "." : $" (signiert von {signer}).");
                    return outcome;
                }
                progress?.Report("Wird installiert …");
                var r = await ProcessRunner.RunAsync(file, SilentArguments(update.VendorInstall), ct, TimeSpan.FromMinutes(45));
                Log.Write($"{update.Title}: Installer beendet mit Code {r.ExitCode}");
                outcome.Success = r.ExitCode is 0 or 3010 or 1641;
                outcome.RebootRequired = r.ExitCode is 3010 or 1641 || update.VendorInstall == VendorInstallKind.AmdChipset;
                outcome.Message = outcome.Success
                    ? (outcome.RebootRequired ? "Installiert – Neustart empfohlen." : "Erfolgreich installiert.")
                    : $"Installer meldet Code {r.ExitCode}.";
                return outcome;
            }

            // INF driver package (ZIP): extract and let pnputil install every matching driver.
            progress?.Report("Wird entpackt …");
            var extract = Path.Combine(work, "files");
            ZipFile.ExtractToDirectory(file, extract);
            if (!Directory.EnumerateFiles(extract, "*.inf", SearchOption.AllDirectories).Any())
            {
                outcome.Message = "Das Paket enthält keinen INF-Treiber – bitte über die Herstellerseite installieren.";
                return outcome;
            }
            progress?.Report("Wird installiert …");
            var p = await ProcessRunner.RunAsync("pnputil.exe", $"/add-driver {ProcessRunner.Quote(Path.Combine(extract, "*.inf"))} /subdirs /install", ct);
            Log.Write($"pnputil {update.Title}: {p.Combined.Trim()}");
            outcome.Success = p.ExitCode is 0 or 3010;
            outcome.RebootRequired = p.ExitCode == 3010;
            outcome.Message = p.ExitCode switch
            {
                0 => "Erfolgreich installiert.",
                3010 => "Installiert – Neustart erforderlich.",
                259 => "Treiber hinzugefügt, Windows behält aber den bisherigen (passender für dieses Gerät).",
                _ => $"Installation fehlgeschlagen (pnputil Code {p.ExitCode}).",
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { outcome.Message = "Fehler: " + ex.Message; }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
        return outcome;
    }
}

/// <summary>Authenticode check through WinVerifyTrust (signature valid, chain trusted, not revoked).</summary>
[SupportedOSPlatform("windows")]
internal static class Authenticode
{
    /// <summary>Subject of the signing certificate when the file has a valid, trusted signature; otherwise null.</summary>
    public static string? VerifiedSigner(string file)
    {
        if (!Verify(file)) return null;
#pragma warning disable SYSLIB0057
        try { return System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file).Subject; }
        catch { return null; }
#pragma warning restore SYSLIB0057
    }

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;          // 2 = WTD_UI_NONE
        public uint fdwRevocationChecks; // 0 = WTD_REVOKE_NONE (chain revocation via dwProvFlags)
        public uint dwUnionChoice;       // 1 = WTD_CHOICE_FILE
        public IntPtr pFile;
        public uint dwStateAction;       // 0 = ignore
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;         // 0x80 = WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WinTrustData data);

    private static bool Verify(string file)
    {
        var fileInfo = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = file };
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,
                dwUnionChoice = 1,
                pFile = pFile,
                dwProvFlags = 0x80,
            };
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }
}
