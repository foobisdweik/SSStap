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
            throw new InvalidOperationException("Unbound is already running.");

        string baseDir = _options.BaseDirectory;
        string unboundDir = Path.Combine(baseDir, "unbound");
        string forwardZoneDir = Path.Combine(unboundDir, "forward-zone");

        Directory.CreateDirectory(unboundDir);
        Directory.CreateDirectory(forwardZoneDir);

        // Resolve template paths (app-relative)
        string appBase = AppContext.BaseDirectory;
        string templateServicePath = Path.Combine(appBase, "unbound", "template-service.conf");
        string templateChinaListPath = Path.Combine(appBase, "unbound", "forward-zone", "template.china-list.conf");

        string serviceConfPath = Path.Combine(unboundDir, "service.conf");
        string forwardZonePath = Path.Combine(forwardZoneDir, "forward-zone.conf");

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
            Arguments = $"-c \"{serviceConfPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = unboundDir
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.Exited += (_, _) =>
        {
            var exitCode = _process?.ExitCode ?? -1;
            ProcessExited?.Invoke(this, exitCode);
        };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
                _options.OnOutput?.Invoke(line);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
                _options.OnError?.Invoke(line);
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    /// <summary>
    /// Stops the Unbound process. Safe to call multiple times.
    /// </summary>
    public void Stop()
    {
        if (_process is null) return;

        try
        {
            if (!_process.HasExited)
                _process.Kill();
        }
        catch (InvalidOperationException) { /* already exited */ }
        catch (Exception ex)
        {
            _options.OnError?.Invoke($"Failed to stop Unbound: {ex.Message}");
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private void GenerateServiceConfig(string templatePath, string outputPath, string directory, string forwardZoneFile)
    {
        string template = File.Exists(templatePath)
            ? File.ReadAllText(templatePath)
            : GetEmbeddedTemplateService();

        string content = template
            .Replace("<@-directory-@>", directory.Replace("\\", "/"))
            .Replace("<@-forward-zone-file-@>", forwardZoneFile.Replace("\\", "/"))
            .Replace("<@-forward-addr1-@>", _options.ForwardAddr1)
            .Replace("<@-forward-addr2-@>", _options.ForwardAddr2);

        File.WriteAllText(outputPath, content);
    }

    private void GenerateForwardZoneConfig(string templatePath, string outputPath)
    {
        string template = File.ReadAllText(templatePath);
        string content = template
            .Replace("<@-forward-addr1-@>", _options.ChinaForwardAddr1)
            .Replace("<@-forward-addr2-@>", _options.ChinaForwardAddr2);

        File.WriteAllText(outputPath, content);
    }

    private void WriteMinimalForwardZone(string outputPath)
    {
        string content = """
            # Minimal forward-zone (default handled by service.conf)
            # No additional zones configured.
            """;
        File.WriteAllText(outputPath, content);
    }

    private static string GetEmbeddedTemplateService()
    {
        return """
            # Unbound configuration file on windows.
            server:
            	verbosity: 0
            	directory: "<@-directory-@>"
            include: "<@-forward-zone-file-@>"
            forward-zone:
            	name: "."
            	forward-addr: <@-forward-addr1-@>
            	forward-addr: <@-forward-addr2-@>
            	forward-first: yes
            	forward-ssl-upstream: no
            """;
    }

    private string ResolveUnboundExe()
    {
        if (!string.IsNullOrEmpty(_options.UnboundExePath) && File.Exists(_options.UnboundExePath))
            return _options.UnboundExePath;

        string appBase = AppContext.BaseDirectory;
        string localUnbound = Path.Combine(appBase, "unbound", "unbound.exe");
        if (File.Exists(localUnbound))
            return localUnbound;

        string? pathExe = FindInPath("unbound.exe");
        if (pathExe is not null)
            return pathExe;

        throw new FileNotFoundException(
            "Unbound executable not found. Set UnboundExePath or place unbound.exe in app/unbound/.");
    }

    private static string? FindInPath(string fileName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string fullPath = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(fullPath))
                return fullPath;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
