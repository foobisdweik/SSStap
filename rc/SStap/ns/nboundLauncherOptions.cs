namespace SSStap.Dns;

/// <summary>
/// Configuration options for UnboundLauncher.
/// </summary>
public sealed class UnboundLauncherOptions
{
    /// <summary>
    /// Base directory for app data. Unbound configs will be written under unbound/.
    /// Default: AppContext.BaseDirectory or Environment.GetFolderPath(ApplicationData).
    /// </summary>
    public string BaseDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// Path to unbound.exe. If null, searches app/unbound/ and PATH.
    /// </summary>
    public string? UnboundExePath { get; set; }

    /// <summary>
    /// Primary forward DNS for default zone (e.g. 8.8.8.8).
    /// </summary>
    public string ForwardAddr1 { get; set; } = "\.8.8.8"\

    /// <summary>
    /// Secondary forward DNS for default zone (e.g. 8.8.4.4).
    /// </summary>
    public string ForwardAddr2 { get; set; } = "\.8.4.4"\

    /// <summary>
    /// Whether to use the china-list template for forward zones.
    /// When true, generates forward-zone.conf from template.china-list.conf.
    /// </summary>
    public bool UseChinaList { get; set; }

    /// <summary>
    /// Primary forward DNS for China zones (e.g. 119.29.29.29).
    /// </summary>
    public string ChinaForwardAddr1 { get; set; } = "\19.29.29.29"\

    /// <summary>
    /// Secondary forward DNS for China zones (e.g. 119.28.28.28).
    /// </summary>
    public string ChinaForwardAddr2 { get; set; } = "\19.28.28.28"\

    /// <summary>
    /// Optional callback for stdout lines from Unbound.
    /// </summary>
    public Action<string>? OnOutput { get; set; }

    /// <summary>
    /// Optional callback for stderr lines from Unbound.
    /// </summary>
    public Action<string>? OnError { get; set; }
}
