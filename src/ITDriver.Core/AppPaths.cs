using System.Text.Json;
using System.Text.Json.Serialization;

namespace ITDriver.Core;

public static class AppPaths
{
    /// <summary>Machine-wide data folder (the app runs elevated, scheduled scans run as SYSTEM/admin).</summary>
    public static string DataRoot { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ITDriver");

    public static string Settings => Path.Combine(DataRoot, "settings.json");
    public static string History => Path.Combine(DataRoot, "history.json");
    public static string LastScan => Path.Combine(DataRoot, "last-scan.json");
    public static string Logs => Path.Combine(DataRoot, "Logs");
    public static string DefaultBackupRoot => Path.Combine(DataRoot, "Backups");

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static T LoadJson<T>(string path, Func<T> fallback)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? fallback();
        }
        catch (Exception ex)
        {
            Log.Write($"Konnte {path} nicht lesen: {ex.Message}");
        }
        return fallback();
    }

    public static void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, overwrite: true);
    }
}

public static class Log
{
    private static readonly object Gate = new();
    public static event Action<string>? Written;

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                File.AppendAllText(Path.Combine(AppPaths.Logs, $"itdriver-{DateTime.Now:yyyyMM}.log"), line + Environment.NewLine);
            }
        }
        catch { /* logging must never crash the app */ }
        Written?.Invoke(line);
    }
}
