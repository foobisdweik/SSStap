using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSStap.Models;
using SSStap.Native;
using SSStap.Services;

namespace SSStap.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigService _configService = new();
    private readonly ProxyTesterService _proxyTester = new();
    private WintunSession? _wintunSession;
    private CancellationTokenSource? _testCts;

    [ObservableProperty]
    private ObservableCollection<ProxyConfig> _proxyConfigs = new();

    [ObservableProperty]
    private ProxyConfig? _selectedProxy;

    [ObservableProperty]
    private int _selectedProxyIndex;

    [ObservableProperty]
    private ProxyMode _selectedMode;

    [ObservableProperty]
    private int _selectedModeIndex;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isTestRunning;

    [ObservableProperty]
    private ObservableCollection<LogEntry> _logEntries = new();

    public MainViewModel()
    {
        LoadConfig();
    }

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

    [RelayCommand]
    private void LoadConfig()
    {
        var proxyList = _configService.LoadProxyList();
        ProxyConfigs.Clear();
        foreach (var config in proxyList.Configs)
        {
            ProxyConfigs.Add(config);
        }

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

    [RelayCommand]
    private void AddSocks5Proxy()
    {
        var dialog = new Dialogs.AddSocks5Dialog();
        if (dialog.ShowDialog() == true && dialog.ResultConfig is { } config)
        {
            config.Type = (int)ProxyType.SOCKS5;
            if (string.IsNullOrEmpty(config.Id))
                config.Id = Guid.NewGuid().ToString();
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
            if (string.IsNullOrEmpty(config.Id))
                config.Id = Guid.NewGuid().ToString();
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
            if (string.IsNullOrEmpty(config.Id))
                config.Id = Guid.NewGuid().ToString();
            ProxyConfigs.Add(config);
            SelectedProxyIndex = ProxyConfigs.Count - 1;
            SaveConfig();
        }
    }

    [RelayCommand]
    private void Connect()
    {
        var proxy = SelectedProxy ?? (SelectedProxyIndex >= 0 && SelectedProxyIndex < ProxyConfigs.Count ? ProxyConfigs[SelectedProxyIndex] : null);
        if (proxy == null)
        {
            StatusText = "Please select a proxy";
            return;
        }

        try
        {
            StatusText = "Initializing Wintun...";
            var session = WintunSession.Create("SSStap", "Wintun");
            if (session == null)
            {
                StatusText = "Failed: stale Wintun drivers. Right-click fix-wintun-driver.ps1 in the SSStap folder, Run with PowerShell as Admin.";
                return;
            }

            if (!session.SetAdapterIp(IPAddress.Parse("10.10.10.1"), IPAddress.Parse("255.255.255.0")))
            {
                session.Dispose();
                StatusText = "Failed to set adapter IP. Run as Administrator.";
                return;
            }

            _wintunSession?.Dispose();
            _wintunSession = session;
            IsConnected = true;
            StatusText = $"Connected to {proxy.DisplayName} (Wintun ready)";
            SaveConfig();
        }
        catch (DllNotFoundException ex)
        {
            StatusText = $"wintun.dll not found: {ex.Message}";
        }
        catch (EntryPointNotFoundException)
        {
            StatusText = "wintun.dll is outdated. Download amd64 build from https://www.wintun.net/";
        }
        catch (Exception ex)
        {
            StatusText = $"Connect failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _wintunSession?.Dispose();
        _wintunSession = null;
        IsConnected = false;
        StatusText = "Disconnected";
        SaveConfig();
    }

    [RelayCommand(CanExecute = nameof(CanTestProxy))]
    private async Task TestProxy()
    {
        var proxy = SelectedProxy ?? (SelectedProxyIndex >= 0 && SelectedProxyIndex < ProxyConfigs.Count ? ProxyConfigs[SelectedProxyIndex] : null);
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
        {
            Application.Current?.Dispatcher.Invoke(() => LogEntries.Add(e));
        });

        try
        {
            await _proxyTester.RunProxyTestAsync(
                proxy.Server,
                proxy.ServerPort,
                string.IsNullOrWhiteSpace(proxy.Username) ? null : proxy.Username,
                string.IsNullOrWhiteSpace(proxy.Password) ? null : proxy.Password,
                progress,
                _testCts.Token);
            StatusText = "Proxy test complete";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Proxy test cancelled";
        }
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
    private void ClearLog()
    {
        LogEntries.Clear();
    }

    partial void OnIsTestRunningChanged(bool value) => TestProxyCommand.NotifyCanExecuteChanged();

    public void Dispose()
    {
        _testCts?.Cancel();
        _testCts?.Dispose();
        _wintunSession?.Dispose();
    }
}

public enum ProxyMode
{
    Global = 0,
    ChinaIpOnly = 1,
    SkipChinaIp = 2,
    BrowserOnly = 3,
}
