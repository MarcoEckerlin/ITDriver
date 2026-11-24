using System.Runtime.Versioning;

namespace ITDriver.Core;

/// <summary>Registers a Windows Task Scheduler task that runs a background driver scan.</summary>
[SupportedOSPlatform("windows")]
public sealed class SchedulerService
{
    public const string TaskName = @"ITDriver\Treiber-Scan";

    public async Task<(bool Ok, string Message)> ApplyAsync(AppSettings s, string exePath)
    {
        if (s.Schedule == ScanSchedule.Off)
        {
            var del = await ProcessRunner.RunAsync("schtasks.exe", $"/Delete /TN {ProcessRunner.Quote(TaskName)} /F");
            return (true, del.Ok ? "Geplanter Scan entfernt." : "Kein geplanter Scan vorhanden.");
        }

        var args = BuildCreateArguments(s, exePath);
        var r = await ProcessRunner.RunAsync("schtasks.exe", args);
        Log.Write("schtasks: " + r.Combined.Trim());
        return r.Ok ? (true, "Geplanter Scan gespeichert.") : (false, "Geplanter Scan konnte nicht angelegt werden: " + r.Combined.Trim());
    }

    internal static string BuildCreateArguments(AppSettings s, string exePath)
    {
        var time = TimeOnly.TryParse(s.ScheduleTime, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : new TimeOnly(12, 0);
        var schedule = s.Schedule switch
        {
            ScanSchedule.Daily => "/SC DAILY",
            ScanSchedule.Weekly => $"/SC WEEKLY /D {DayCode(s.ScheduleDay)}",
            ScanSchedule.Monthly => "/SC MONTHLY /D 1",
            _ => throw new ArgumentOutOfRangeException(nameof(s)),
        };
        // /TR needs the quoted exe path inside the quoted command: "\"C:\...\ITDriver.exe\" --background-scan"
        var tr = $"\"\\\"{exePath}\\\" --background-scan\"";
        // /IT = only when the user is logged on, so the notification is visible; /RL HIGHEST = elevated without UAC prompt.
        return $"/Create /F /TN {ProcessRunner.Quote(TaskName)} /TR {tr} {schedule} /ST {time:HH\\:mm} /RL HIGHEST /IT";
    }

    private static string DayCode(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "MON",
        DayOfWeek.Tuesday => "TUE",
        DayOfWeek.Wednesday => "WED",
        DayOfWeek.Thursday => "THU",
        DayOfWeek.Friday => "FRI",
        DayOfWeek.Saturday => "SAT",
        _ => "SUN",
    };
}
