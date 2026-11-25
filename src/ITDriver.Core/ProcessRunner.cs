using System.Diagnostics;
using System.Text;

namespace ITDriver.Core;

public sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
    public string Combined => string.IsNullOrWhiteSpace(Error) ? Output : Output + Environment.NewLine + Error;
}

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string file, string args, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromMinutes(15));
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            throw;
        }
        return new ProcessResult(p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Runs a PowerShell script block (UTF-8 output) and returns its stdout.</summary>
    public static Task<ProcessResult> PowerShellAsync(string script, CancellationToken ct = default)
    {
        var full = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; " + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return RunAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", ct);
    }

    /// <summary>Quotes a path for the Windows command line (a trailing backslash would escape the closing quote).</summary>
    public static string Quote(string arg)
    {
        var trimmed = arg.Length > 3 ? arg.TrimEnd('\\') : arg;
        if (trimmed.EndsWith('\\')) trimmed += "."; // "C:\" -> "C:\."
        return "\"" + trimmed.Replace("\"", "") + "\"";
    }
}
