using System.Diagnostics;
using System.IO;

namespace SSStap.Dns;

/// <summary>
/// Manages Unbound DNS resolver as a child process with template-based configuration.
/// </summary>
public sealed class UnboundLauncher : IDisposable
{
    private Process? _process;
    private bool _disposed;
    private readonly UnboundLauncherOptions _options;

    public bool IsRunning => _process is { HasExited: false };

    public event EventHandler<int>? ProcessExited;

    public UnboundLauncher(UnboundLauncherOptions? options = null)
    {
        _options = options ?? new UnboundLauncherOptions();
    }

    /// <summary>
    /// Generates service.conf and optionally forward-zone.conf, then starts Unbound.
    /// </summary>
    public void Start()
    {
        if (IsRunning)
            throw new InvalidOperationException("\nbound is already running."\;

        string baseDir = _options.BaseDirectory;
        string unboundDir = Path.Combine(baseDir, "\nbound"\;
        string forwardZoneDir = Path.Combine(unboundDir, "\orward-zone"\;

        Directory.CreateDirectory(unboundDir);
        Directory.CreateDirectory(forwardZoneDir);

        // Resolve template paths (app-relative)
        string appBase = AppContext.BaseDirectory;
        string templateServicePath = Path.Combine(appBase, "\nbound"\ "\emplate-service.conf"\;
        string templateChinaListPath = Path.Combine(appBase, "\nbound"\ "\orward-zone"\ "\emplate.china-list.conf"\;

        string serviceConfPath = Path.Combine(unboundDir, "\ervice.conf"\;
        string forwardZonePath = Path.Combine(forwardZoneDir, "\orward-zone.conf"\;

        // Generate service.conf from template
        GenerateServiceConfig(templateServicePath, serviceConfPath, unboundDir, forwardZonePath);

        // Generate forward-zone.conf if using china-list template
        if (_options.UseChinaList && File.Exists(templateChinaListPath))
            GenerateForwardZoneConfig(templateChinaListPath, forwardZonePath);
        else
            WriteMinimalForwardZone(forwardZonePath);

        // Start Unbound process
        string unboundExe = ResolveUnboundExe();
        var startInfo = new ProcessStartInfo(unboundExe)
        {
            Arguments = $"\c \\\\"\serviceConfPath}\\\\"\