using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSStap.Models;
using SSStap.Native;
using SSStap.Routing;
using SSStap.Services;
using SSStap.Tunnel;

namespace SSStap.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigService _configService = new();
    private readonly ProxyTesterService _proxyTester = new();

    // Active tunnel state — all null when disconnected
    private WintunSession? _wintunSession;
    private RouteManager? _routeManager;
    private TunnelEngine? _tunnelEngine;
    private CancellationTokenSource? _connectCts;

    private CancellationTokenSource? _testCts;

    [ObservableProperty] private ObservableCollection<ProxyConfig> _proxyConfigs = new();
    [ObservableProperty] private ProxyConfig? _selectedProxy;
    [ObservableProperty] private int _selectedProxyIndex;
    [ObservableProperty] private ProxyMode _selectedMode;
    [ObservableProperty] private int _selectedModeIndex;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _isTestRunning;
    [ObservableProperty] private ObservableCollection<LogEntry> _logEntries = new();

    public MainViewModel()
    {
        LoadConfig();
    }

    // ── Proxy / mode selection sync ──────────────────────────────────────────

    partial void OnSelectedProxyChanged(ProxyConfig? value)
    {
        if (value != null)
        {
            var idx = ProxyConfigs.IndexOf(value);
            if (idx >= 0 && idx != SelectedProxyIndex)
                SelectedProxyIndex = idx;
        }
    }

    partial void OnSelectedProxyIndexChanged(int value)
    {
        if (value >= 0 && value < ProxyConfigs.Count)
            SelectedProxy = ProxyConfigs[value];
    }

    partial void OnSelectedModeIndexChanged(int value)
    {
        SelectedMode = (ProxyMode)Math.Clamp(value, 0, 3);
    }

    // ── Config persistence ───────────────────────────────────────────────────

    [RelayCommand]
    private void LoadConfig()
    {
        var proxyList = _configService.LoadProxyList();
        ProxyConfigs.Clear();
        foreach (var config in proxyList.Configs)
            ProxyConfigs.Add(config);

        var appConfig = _configService.LoadAppConfig();
        SelectedModeIndex = Math.Clamp(appConfig.LastProxyModeIndex, 0, 3);
        SelectedMode = (ProxyMode)SelectedModeIndex;
        var idx = Math.Clamp(proxyList.IdInUse, 0, Math.Max(0, ProxyConfigs.Count - 1));
        SelectedProxyIndex = idx;
        SelectedProxy = ProxyConfigs.Count > 0 ? ProxyConfigs[idx] : null;
    }

    [RelayCommand]
    private void SaveConfig()
    {
        var proxyList = new ProxyListJson
        {
            Configs = ProxyConfigs.ToList(),
            IdInUse = SelectedProxyIndex
        };
        _configService.SaveProxyList(proxyList);

        var appConfig = _configService.LoadAppConfig();
        appConfig.LastProxyModeIndex = SelectedModeIndex;
        appConfig.LastUsedNodeId = SelectedProxyIndex;
        _configService.SaveAppConfig(appConfig);
    }

    // ── Proxy management ─────────────────────────────────────────────────────

    [RelayCommand]
    private void AddSocks5Proxy()
    {
        var dialog = new Dialogs.AddSocks5Dialog();
        if (dialog.ShowDialog() == true && dialog.ResultConfig is { } config)
        {
            config.Type = (int)ProxyType.SOCKS5;
            if (string.IsNullOrEmpty(config.Id)) config.Id = Guid.NewGuid().ToString();
            ProxyConfigs.Add(config);
            SelectedProxyIndex = ProxyConfigs.Count - 1;
            SaveConfig();
        }
    }

    [RelayCommand]
    private void AddSsProxy()
    {
        var dialog = new Dialogs.AddSsDialog();
        if (dialog.ShowDialog() == true && dialog.ResultConfig is { } config)
        {
            config.Type = (int)ProxyType.Shadowsocks;
            if (string.IsNullOrEmpty(config.Id)) config.Id = Guid.NewGuid().ToString();
            ProxyConfigs.Add(config);
            SelectedProxyIndex = ProxyConfigs.Count - 1;
            SaveConfig();
        }
    }

    [RelayCommand]
    private void AddFromLink()
    {
        var dialog = new Dialogs.AddFromLinkDialog();
        if (dialog.ShowDialog() == true && dialog.ResultConfig is { } config)
        {
            if (string.IsNullOrEmpty(config.Id)) config.Id = Guid.NewGuid().ToString();
            ProxyConfigs.Add(config);
            SelectedProxyIndex = ProxyConfigs.Count - 1;
            SaveConfig();
        }
    }

    // ── Connect / Disconnect ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task Connect()
    {
        var proxy = SelectedProxy
            ?? (SelectedProxyIndex >= 0 && SelectedProxyIndex < ProxyConfigs.Count
                ? ProxyConfigs[SelectedProxyIndex] : null);

        if (proxy == null)
        {
            StatusText = "Please select a proxy";
            return;
        }

        // Always tear down cleanly before a new connect attempt
        await TearDownAsync();

        try
        {
            // Step 1 — Create Wintun adapter ──────────────────────────────────
            StatusText = "Creating Wintun interface...";
            Log("Creating Wintun adapter...", LogSeverity.Info);

            var session = WintunSession.Create("SSStap", "Wintun");
            if (session == null)
            {
                StatusText = "Failed to create Wintun interface. Try running fix-wintun-driver.ps1 as Admin.";
                Log("WintunSession.Create returned null — driver issue or insufficient privileges.", LogSeverity.Error);
                return;
            }
            _wintunSession = session;
            Log($"Wintun adapter created: {session.AdapterName}", LogSeverity.Info);

            // Step 2 — Assign tunnel IP (10.10.10.1/24) ───────────────────────
            StatusText = "Configuring tunnel IP...";
            var tunnelIp   = IPAddress.Parse("10.10.10.1");
            var tunnelMask = IPAddress.Parse("255.255.255.0");

            if (!session.SetAdapterIp(tunnelIp, tunnelMask))
            {
                StatusText = "Failed to set adapter IP. SSStap must run as Administrator.";
                Log("SetAdapterIp failed — run as Administrator.", LogSeverity.Error);
                await TearDownAsync();
                return;
            }
            Log($"Tunnel IP set: {tunnelIp}/{tunnelMask}", LogSeverity.Info);

            // Step 3 — Resolve Wintun interface index ─────────────────────────
            var wintunIfIndex = AdapterSetup.GetInterfaceIndexByAdapterName(session.AdapterName);
            if (wintunIfIndex == null)
            {
                StatusText = "Could not find Wintun adapter in network interface list.";
                Log($"GetInterfaceIndexByAdapterName returned null for '{session.AdapterName}'.", LogSeverity.Error);
                await TearDownAsync();
                return;
            }
            Log($"Wintun interface index: {wintunIfIndex}", LogSeverity.Info);

            // Step 4 — Resolve physical adapter index ─────────────────────────
            var physicalIfIndex = AdapterSetup.GetDefaultGatewayInterfaceIndex();
            Log($"Physical (default-gateway) interface index: {physicalIfIndex?.ToString() ?? "not found"}", LogSeverity.Info);

            // Step 5 — Resolve proxy server to an IP address ──────────────────
            // Required to install a /32 host route on the physical adapter before
            // the Wintun default route, preventing the routing loop where traffic
            // to the proxy server routes through Wintun and back to itself.
            IPAddress? proxyServerIp = null;
            if (!IPAddress.TryParse(proxy.Server, out proxyServerIp))
            {
                // Proxy server specified as hostname — resolve to IP before routing setup.
                StatusText = "Resolving proxy server address...";
                try
                {
                    var addrs = await Dns.GetHostAddressesAsync(proxy.Server);
                    proxyServerIp = addrs.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                    ?? addrs.FirstOrDefault();
                    if (proxyServerIp == null)
                        Log($"Warning: could not resolve '{proxy.Server}' to an IP address — proxy host route will not be installed. Routing loop protection is inactive.", LogSeverity.Warning);
                    else
                        Log($"Resolved proxy server '{proxy.Server}' → {proxyServerIp}", LogSeverity.Info);
                }
                catch (Exception ex)
                {
                    Log($"Warning: DNS resolution of '{proxy.Server}' failed ({ex.Message}) — proxy host route will not be installed. Routing loop protection is inactive.", LogSeverity.Warning);
                }
            }
            else
            {
                Log($"Proxy server IP: {proxyServerIp}", LogSeverity.Info);
            }

            // Step 6 — Apply routing table entries ────────────────────────────
            StatusText = "Applying routes...";
            var routingMode = (RoutingMode)(int)SelectedMode; // ProxyMode values are intentionally identical to RoutingMode
            _routeManager = new RouteManager();
            _connectCts = new CancellationTokenSource();

            var routesOk = await _routeManager.ApplyRoutesAsync(
                routingMode,
                wintunIfIndex.Value,
                physicalIfIndex,
                proxyServerIp,
                _connectCts.Token);

            if (!routesOk)
            {
                StatusText = "Failed to apply routes. SSStap must run as Administrator.";
                Log($"ApplyRoutesAsync failed for mode={routingMode}.", LogSeverity.Error);
                await TearDownAsync();
                return;
            }
            Log($"Routes applied (mode={routingMode}).", LogSeverity.Info);

            // Step 7 — Build SOCKS5 client ────────────────────────────────────
            var socks5 = new Socks5Client(
                proxy.Server,
                proxy.ServerPort,
                string.IsNullOrWhiteSpace(proxy.Username) ? null : proxy.Username,
                string.IsNullOrWhiteSpace(proxy.Password) ? null : proxy.Password);

            // Step 8 — Start tunnel engine ────────────────────────────────────
            StatusText = $"Starting tunnel to {proxy.DisplayName}...";
            var packetSource = new WintunPacketSource(session);
            _tunnelEngine = new TunnelEngine(packetSource, socks5, _routeManager, routingMode);
            _tunnelEngine.Start();

            IsConnected = true;
            StatusText = $"Connected — {proxy.DisplayName}";
            Log($"Tunnel running. Proxy: {proxy.Server}:{proxy.ServerPort}", LogSeverity.Info);
            SaveConfig();
        }
        catch (DllNotFoundException ex)
        {
            StatusText = $"wintun.dll not found: {ex.Message}";
            Log($"DllNotFoundException: {ex.Message}", LogSeverity.Error);
            await TearDownAsync();
        }
        catch (EntryPointNotFoundException)
        {
            StatusText = "wintun.dll is outdated — replace with amd64 build from https://www.wintun.net/";
            Log("EntryPointNotFoundException: WintunGetAdapterLuid missing from wintun.dll.", LogSeverity.Error);
            await TearDownAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Connect failed: {ex.Message}";
            Log($"Unhandled connect exception: {ex}", LogSeverity.Error);
            await TearDownAsync();
        }
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        await TearDownAsync();
        IsConnected = false;
        StatusText = "Disconnected";
        Log("Disconnected.", LogSeverity.Info);
        SaveConfig();
    }

    private async Task TearDownAsync()
    {
        _connectCts?.Cancel();

        if (_tunnelEngine != null)
        {
            try { await _tunnelEngine.StopAsync(); } catch { /* best-effort */ }
            _tunnelEngine = null;
        }

        if (_routeManager != null)
        {
            _routeManager.RemoveAllRoutes();
            _routeManager = null;
        }

        _wintunSession?.Dispose();
        _wintunSession = null;

        _connectCts?.Dispose();
        _connectCts = null;
    }

    // ── Proxy test ───────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanTestProxy))]
    private async Task TestProxy()
    {
        var proxy = SelectedProxy
            ?? (SelectedProxyIndex >= 0 && SelectedProxyIndex < ProxyConfigs.Count
                ? ProxyConfigs[SelectedProxyIndex] : null);

        if (proxy == null)
        {
            StatusText = "Please select a proxy";
            return;
        }

        _testCts?.Cancel();
        _testCts = new CancellationTokenSource();
        IsTestRunning = true;
        LogEntries.Clear();
        StatusText = "Testing proxy...";

        var progress = new Progress<LogEntry>(e =>
            Application.Current?.Dispatcher.Invoke(() => LogEntries.Add(e)));

        try
        {
            await _proxyTester.RunProxyTestAsync(
                proxy.Server, proxy.ServerPort,
                string.IsNullOrWhiteSpace(proxy.Username) ? null : proxy.Username,
                string.IsNullOrWhiteSpace(proxy.Password) ? null : proxy.Password,
                progress, _testCts.Token);
            StatusText = "Proxy test complete";
        }
        catch (OperationCanceledException) { StatusText = "Proxy test cancelled"; }
        catch (Exception ex)
        {
            StatusText = $"Proxy test failed: {ex.Message}";
            Application.Current?.Dispatcher.Invoke(() =>
                LogEntries.Add(new LogEntry(DateTime.Now.ToString("HH:mm:ss"), $"[-] {ex.Message}", LogSeverity.Error)));
        }
        finally
        {
            IsTestRunning = false;
            _testCts?.Dispose();
            _testCts = null;
        }
    }

    private bool CanTestProxy() => !IsTestRunning;

    [RelayCommand]
    private void ClearLog() => LogEntries.Clear();

    partial void OnIsTestRunningChanged(bool value) => TestProxyCommand.NotifyCanExecuteChanged();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void Log(string message, LogSeverity severity)
    {
        Application.Current?.Dispatcher.Invoke(() =>
            LogEntries.Add(new LogEntry(DateTime.Now.ToString("HH:mm:ss"), message, severity)));
    }

    public void Dispose()
    {
        _testCts?.Cancel();
        _testCts?.Dispose();
        // Run on the thread-pool to avoid a UI-thread deadlock when continuations
        // inside TearDownAsync try to resume on the synchronization context.
        Task.Run(TearDownAsync).GetAwaiter().GetResult();
    }
}

public enum ProxyMode
{
    Global      = 0,
    ChinaIpOnly = 1,
    SkipChinaIp = 2,
    BrowserOnly = 3,
}
