using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ITDriver.Core;

/// <summary>Minimal INotifyPropertyChanged base so models can be bound directly in the UI.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A Plug-and-Play device with the driver currently bound to it.</summary>
public sealed class DeviceInfo
{
    public string Name { get; set; } = "";
    public string? Class { get; set; }
    public string? Manufacturer { get; set; }
    public string InstanceId { get; set; } = "";
    public string[] HardwareIds { get; set; } = Array.Empty<string>();
    public string[] CompatibleIds { get; set; } = Array.Empty<string>();
    public string? Status { get; set; }
    public int ErrorCode { get; set; }
    public bool Present { get; set; } = true;
    public string? DriverVersion { get; set; }
    public DateTime? DriverDate { get; set; }
    public string? DriverProvider { get; set; }
    public string? InfName { get; set; }
    public bool? IsSigned { get; set; }

    [JsonIgnore] public bool HasProblem => ErrorCode != 0;
    [JsonIgnore] public bool IsMissingDriver => ErrorCode == 28 || ErrorCode == 1 || (ErrorCode != 0 && string.IsNullOrEmpty(InfName));
    [JsonIgnore] public string ProblemText => DeviceProblems.Describe(ErrorCode);
    [JsonIgnore] public HardwareId? PrimaryId => HardwareIds.Select(HardwareId.TryParse).FirstOrDefault(h => h != null);
    [JsonIgnore] public VendorInfo? Vendor => VendorCatalog.Find(this);
    [JsonIgnore] public string VendorName => Vendor?.Name ?? Manufacturer ?? "";
    [JsonIgnore] public bool UsesGenericMicrosoftDriver =>
        string.Equals(DriverProvider, "Microsoft", StringComparison.OrdinalIgnoreCase) && Vendor is { IsMicrosoft: false };
    /// <summary>Software/virtual device (ROOT\..., SWD\..., app-installed adapters) rather than physical hardware.</summary>
    [JsonIgnore] public bool IsVirtual => !HardwareIds.Concat(CompatibleIds).Select(HardwareId.TryParse).Any(h => h is { IsPhysicalBus: true });
    [JsonIgnore] public int? DriverAgeDays => DriverDate is { } d ? (int)(DateTime.Today - d.Date).TotalDays : null;
    [JsonIgnore] public string CatalogSearchUrl => MicrosoftCatalog.SearchUrl(this);
}

public enum UpdateSource { WindowsUpdate, Catalog, Vendor }

/// <summary>A driver update offered by a legitimate source.</summary>
public sealed class DriverUpdate : ObservableObject
{
    private bool _isSelected = true;
    private string _state = "";

    public string UpdateId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? DriverClass { get; set; }
    public string? DriverModel { get; set; }
    public string? Provider { get; set; }
    public DateTime? DriverDate { get; set; }
    public string? HardwareId { get; set; }
    public long SizeBytes { get; set; }
    public UpdateSource Source { get; set; } = UpdateSource.WindowsUpdate;
    public string? InfoUrl { get; set; }
    public string? NewVersion { get; set; }

    /// <summary>The local device this update targets, if it could be matched.</summary>
    public string? DeviceInstanceId { get; set; }
    public string? DeviceName { get; set; }
    public string? InstalledVersion { get; set; }
    public DateTime? InstalledDate { get; set; }
    public string? InstalledInf { get; set; }
    public bool IsMissingDriver { get; set; }

    [JsonIgnore] public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    [JsonIgnore] public string State { get => _state; set => Set(ref _state, value); }
    [JsonIgnore] public string SizeText => SizeBytes <= 0 ? "" : SizeBytes >= 1 << 20 ? $"{SizeBytes / 1048576.0:0.#} MB" : $"{SizeBytes / 1024.0:0} KB";
    [JsonIgnore] public string DisplayName => DeviceName ?? DriverModel ?? Title;
    [JsonIgnore] public string SourceText => Source switch
    {
        UpdateSource.WindowsUpdate => "Windows Update",
        UpdateSource.Catalog => "Microsoft Update-Katalog",
        _ => Provider ?? "Hersteller",
    };
    [JsonIgnore] public string KindText => IsMissingDriver ? "Fehlt" : string.IsNullOrEmpty(InstalledVersion) ? "Neu" : "Veraltet";
}

/// <summary>A driver package in the Windows driver store (oemNN.inf).</summary>
public sealed class DriverPackage
{
    public string PublishedName { get; set; } = "";   // oem12.inf
    public string OriginalFileName { get; set; } = ""; // full path into FileRepository
    public string? ProviderName { get; set; }
    public string? ClassName { get; set; }
    public string? Version { get; set; }
    public DateTime? Date { get; set; }
    public bool BootCritical { get; set; }
    public bool Inbox { get; set; }

    [JsonIgnore] public string InfFileName => Path.GetFileName(OriginalFileName.Replace('\\', '/'));
    [JsonIgnore] public string? RepositoryFolder
    {
        get
        {
            var normalized = OriginalFileName.Replace('/', '\\');
            var idx = normalized.LastIndexOf('\\');
            return idx > 0 ? normalized[..idx] : null;
        }
    }
}

public sealed class SystemSummary
{
    public string OS { get; set; } = "";
    public string OSBuild { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string Board { get; set; } = "";
    public string Bios { get; set; } = "";
    public string Cpu { get; set; } = "";
    public double RamGB { get; set; }
    public List<string> Gpus { get; set; } = new();
}

public sealed class InstallOutcome
{
    public string UpdateId { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Success { get; set; }
    public bool RebootRequired { get; set; }
    public string Message { get; set; } = "";
}
