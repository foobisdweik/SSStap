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

    /// <param name="configDirectoryOverride">Optional. For testing; when set, uses this path instead of auto-resolve.</param>
    public ConfigService(string? configDirectoryOverride = null)
    {
        _configDirectory = configDirectoryOverride ?? ResolveConfigDirectory();
        _proxylistPath = Path.Combine(_configDirectory, "proxylist.json");
        _iniPath = Path.Combine(_configDirectory, "config.ini");
    }

    private static string ResolveConfigDirectory()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var currentDir = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "config"),  // from bin/Debug/net8.0-windows to repo root
            Path.Combine(currentDir, "..", "config"),
            Path.Combine(currentDir, "config"),
            Path.Combine(baseDir, "..", "..", "..", "..", "config"),
            Path.Combine(baseDir, "..", "..", "config"),
            Path.Combine(baseDir, "config"),
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

        return Path.Combine(baseDir, "config");
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
            System.Diagnostics.Debug.WriteLine($"LoadProxyList error: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"SaveProxyList error: {ex.Message}");
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
            var lines = content.Split('\n');
            string currentSection = "";

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    currentSection = trimmed[1..^1].Trim().ToLowerInvariant();
                    continue;
                }

                if (currentSection != "basic")
                    continue;

                var eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;

                var key = trimmed[..eq].Trim();
                var value = trimmed[(eq + 1)..].Trim();

                SetConfigValue(config, key, value);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadAppConfig error: {ex.Message}");
        }

        return config;
    }

    public void SaveAppConfig(AppConfig config)
    {
        try
        {
            if (!Directory.Exists(_configDirectory))
                Directory.CreateDirectory(_configDirectory);

            var sb = new StringBuilder();
            sb.AppendLine("[basic]");
            sb.AppendLine($"startup={config.Startup}");
            sb.AppendLine($"AutomaticallyEstablishConnection={config.AutomaticallyEstablishConnection}");
            sb.AppendLine($"AutomaticHideWindow={config.AutomaticHideWindow}");
            sb.AppendLine($"dns_type={config.DnsType}");
            sb.AppendLine($"liststyle={config.ListStyle}");
            sb.AppendLine($"proxyengine={config.ProxyEngine}");
            sb.AppendLine($"bEnableSysWideProxy={config.EnableSysWideProxy}");
            sb.AppendLine($"bUseOnlinePac={config.UseOnlinePac}");
            sb.AppendLine($"bGlobalMode={config.GlobalMode}");
            sb.AppendLine($"strOnlinePacUrl={config.OnlinePacUrl}");
            sb.AppendLine($"strUserExtendPacFile={config.UserExtendPacFile}");
            sb.AppendLine($"rememberuser={config.RememberUser}");
            sb.AppendLine($"autologin={config.AutoLogin}");
            sb.AppendLine($"appenabled={config.AppEnabled}");
            sb.AppendLine($"defaultportindex={config.DefaultPortIndex}");
            sb.AppendLine($"testurl={config.TestUrl}");
            sb.AppendLine($"local_connection_name={config.LocalConnectionName}");
            sb.AppendLine($"local_connection_guid={config.LocalConnectionGuid}");
            sb.AppendLine($"local_connection_ip={config.LocalConnectionIp}");
            sb.AppendLine($"local_connection_mask={config.LocalConnectionMask}");
            sb.AppendLine($"local_connection_gateway={config.LocalConnectionGateway}");
            sb.AppendLine($"local_connection_primary_dns={config.LocalConnectionPrimaryDns}");
            sb.AppendLine($"local_connection_second_dns={config.LocalConnectionSecondDns}");
            sb.AppendLine($"local_connection_DhcpNameServer={config.LocalConnectionDhcpNameServer}");
            sb.AppendLine($"tap_connection_name={config.TapConnectionName}");
            sb.AppendLine($"tap_connection_guid={config.TapConnectionGuid}");
            sb.AppendLine($"tap_connection_index={config.TapConnectionIndex}");
            sb.AppendLine($"local_connection_index={config.LocalConnectionIndex}");
            sb.AppendLine($"tap_adapter_configed={config.TapAdapterConfiged}");
            sb.AppendLine($"bReduceTCPDelayedACK={config.ReduceTCPDelayedACK}");
            sb.AppendLine($"bDontProxyUDP={config.DontProxyUDP}");
            sb.AppendLine($"local_connection_shortest_r_nexthop={config.LocalConnectionShortestRNextHop}");
            sb.AppendLine($"local_connection_shortest_r_directhop={config.LocalConnectionShortestRDirectHop}");
            sb.AppendLine($"local_connection_shortest_r_ifindex={config.LocalConnectionShortestRIfIndex}");
            sb.AppendLine($"bRegenerateIVForSSUDP={config.RegenerateIVForSSUDP}");
            sb.AppendLine($"last_proxymode_index={config.LastProxyModeIndex}");
            sb.AppendLine($"bIsFirstRunApp={config.IsFirstRunApp}");
            sb.AppendLine($"DelayedConnect={config.DelayedConnect}");
            sb.AppendLine($"LastUsedNodeId={config.LastUsedNodeId}");

            File.WriteAllText(_iniPath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveAppConfig error: {ex.Message}");
            throw;
        }
    }

    private static void SetConfigValue(AppConfig config, string key, string value)
    {
        var k = key.ToLowerInvariant();
        switch (k)
        {
            case "startup": config.Startup = ParseInt(value); break;
            case "automaticallyestablishconnection": config.AutomaticallyEstablishConnection = ParseInt(value); break;
            case "automatichidewindow": config.AutomaticHideWindow = ParseInt(value); break;
            case "dns_type": config.DnsType = ParseInt(value); break;
            case "liststyle": config.ListStyle = ParseInt(value); break;
            case "proxyengine": config.ProxyEngine = ParseInt(value); break;
            case "benablesyswideproxy": config.EnableSysWideProxy = ParseInt(value); break;
            case "buseonlinepac": config.UseOnlinePac = ParseInt(value); break;
            case "bglobalmode": config.GlobalMode = ParseInt(value); break;
            case "stronlinepacurl": config.OnlinePacUrl = value; break;
            case "struserextendpacfile": config.UserExtendPacFile = value; break;
            case "rememberuser": config.RememberUser = ParseInt(value); break;
            case "autologin": config.AutoLogin = ParseInt(value); break;
            case "appenabled": config.AppEnabled = ParseInt(value); break;
            case "defaultportindex": config.DefaultPortIndex = ParseInt(value); break;
            case "testurl": config.TestUrl = value; break;
            case "local_connection_name": config.LocalConnectionName = value; break;
            case "local_connection_guid": config.LocalConnectionGuid = value; break;
            case "local_connection_ip": config.LocalConnectionIp = value; break;
            case "local_connection_mask": config.LocalConnectionMask = value; break;
            case "local_connection_gateway": config.LocalConnectionGateway = value; break;
            case "local_connection_primary_dns": config.LocalConnectionPrimaryDns = value; break;
            case "local_connection_second_dns": config.LocalConnectionSecondDns = value; break;
            case "local_connection_dhcpnameserver": config.LocalConnectionDhcpNameServer = value; break;
            case "tap_connection_name": config.TapConnectionName = value; break;
            case "tap_connection_guid": config.TapConnectionGuid = value; break;
            case "tap_connection_index": config.TapConnectionIndex = ParseInt(value); break;
            case "local_connection_index": config.LocalConnectionIndex = ParseInt(value); break;
            case "tap_adapter_configed": config.TapAdapterConfiged = ParseInt(value); break;
            case "breducetcpdelayedack": config.ReduceTCPDelayedACK = ParseInt(value); break;
            case "bdontproxyudp": config.DontProxyUDP = ParseInt(value); break;
            case "local_connection_shortest_r_nexthop": config.LocalConnectionShortestRNextHop = value; break;
            case "local_connection_shortest_r_directhop": config.LocalConnectionShortestRDirectHop = ParseInt(value); break;
            case "local_connection_shortest_r_ifindex": config.LocalConnectionShortestRIfIndex = ParseInt(value); break;
            case "bregenerateivforssudp": config.RegenerateIVForSSUDP = ParseInt(value); break;
            case "last_proxymode_index": config.LastProxyModeIndex = ParseInt(value); break;
            case "bisfirstrunapp": config.IsFirstRunApp = ParseInt(value); break;
            case "delayedconnect": config.DelayedConnect = ParseInt(value); break;
            case "lastusednodeid": config.LastUsedNodeId = ParseInt(value); break;
        }
    }

    private static int ParseInt(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    public string ConfigDirectory => _configDirectory;
}
