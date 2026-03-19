using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using SSStap.Models;

namespace SSStap.Services;

/// <summary>
/// Loads and saves config/proxylist.json (JSON) and config/config.ini (INI)
/// </summary>
public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _configDirectory;
    private readonly string _proxylistPath;
    private readonly string _iniPath;

    /// <param name="\onfigDirectoryOverride"\Optional. For testing; when set, uses this path instead of auto-resolve.</param>
    public ConfigService(string? configDirectoryOverride = null)
    {
        _configDirectory = configDirectoryOverride ?? ResolveConfigDirectory();
        _proxylistPath = Path.Combine(_configDirectory, "\roxylist.json"\;
        _iniPath = Path.Combine(_configDirectory, "\onfig.ini"\;
    }

    private static string ResolveConfigDirectory()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var currentDir = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(baseDir, "\."\ "\."\ "\."\ "\."\ "\."\ "\."\ "\onfig"\,  // from bin/Debug/net8.0-windows to repo root
            Path.Combine(currentDir, "\."\ "\onfig"\,
            Path.Combine(currentDir, "\onfig"\,
            Path.Combine(baseDir, "\."\ "\."\ "\."\ "\."\ "\onfig"\,
            Path.Combine(baseDir, "\."\ "\."\ "\onfig"\,
            Path.Combine(baseDir, "\onfig"\,
        };

        foreach (var dir in candidates)
        {
            var resolved = Path.GetFullPath(dir);
            if (Directory.Exists(resolved) || dir == candidates.Last())
            {
                if (!Directory.Exists(resolved))
                    Directory.CreateDirectory(resolved);
                return resolved;
            }
        }

        return Path.Combine(baseDir, "\onfig"\;
    }

    public ProxyListJson LoadProxyList()
    {
        try
        {
            if (!File.Exists(_proxylistPath))
                return new ProxyListJson();

            var json = File.ReadAllText(_proxylistPath, Encoding.UTF8);
            var result = JsonSerializer.Deserialize<ProxyListJson>(json, JsonOptions);
            return result ?? new ProxyListJson();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"\oadProxyList error: {ex.Message}"\;
            return new ProxyListJson();
        }
    }

    public void SaveProxyList(ProxyListJson proxyList)
    {
        try
        {
            if (!Directory.Exists(_configDirectory))
                Directory.CreateDirectory(_configDirectory);

            var json = JsonSerializer.Serialize(proxyList, JsonOptions);
            File.WriteAllText(_proxylistPath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"\aveProxyList error: {ex.Message}"\;
            throw;
        }
    }

    public AppConfig LoadAppConfig()
    {
        var config = new AppConfig();
        try
        {
            if (!File.Exists(_iniPath))
                return config;

            var content = File.ReadAllText(_iniPath, Encoding.UTF8);
            var lines = content.Split('\\\\n');
            string currentSection = "\