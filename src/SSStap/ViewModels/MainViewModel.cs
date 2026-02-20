using System.Collections.ObjectModel;
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
    private WintunSession? _wintunSession;

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

    public MainViewModel()
    {
        LoadConfig();
    }

    partial void OnSelectedProxyIndexChanged(int value)
    {
        if (value >= 0 && value < ProxyConfigs.Count)
        {
            SelectedProxy = ProxyConfigs[value];
        }
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
        SelectedProxyIndex = Math.Clamp(proxyList.IdInUse, 0, Math.Max(0, ProxyConfigs.Count - 1));
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
        if (SelectedProxy == null)
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
                StatusText = "Failed to create Wintun adapter. Try running as Administrator.";
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
            StatusText = $"Connected to {SelectedProxy.DisplayName} (Wintun ready)";
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

    public void Dispose() => _wintunSession?.Dispose();
}

public enum ProxyMode
{
    Global = 0,
    ChinaIpOnly = 1,
    SkipChinaIp = 2,
    BrowserOnly = 3,
}
