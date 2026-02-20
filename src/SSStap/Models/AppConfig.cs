namespace SSStap.Models;

/// <summary>
/// Settings from config/config.ini (basic section)
/// </summary>
public class AppConfig
{
    public int Startup { get; set; }
    public int AutomaticallyEstablishConnection { get; set; }
    public int AutomaticHideWindow { get; set; }
    public int DnsType { get; set; }
    public int ListStyle { get; set; }
    public int ProxyEngine { get; set; }
    public int EnableSysWideProxy { get; set; }
    public int UseOnlinePac { get; set; }
    public int GlobalMode { get; set; }
    public string OnlinePacUrl { get; set; } = "";
    public string UserExtendPacFile { get; set; } = "";
    public int RememberUser { get; set; }
    public int AutoLogin { get; set; }
    public int AppEnabled { get; set; } = 1;
    public int DefaultPortIndex { get; set; } = -1;
    public string TestUrl { get; set; } = "http://global.bing.com";
    public string LocalConnectionName { get; set; } = "";
    public string LocalConnectionGuid { get; set; } = "";
    public string LocalConnectionIp { get; set; } = "";
    public string LocalConnectionMask { get; set; } = "";
    public string LocalConnectionGateway { get; set; } = "";
    public string LocalConnectionPrimaryDns { get; set; } = "";
    public string LocalConnectionSecondDns { get; set; } = "";
    public string LocalConnectionDhcpNameServer { get; set; } = "";
    public string TapConnectionName { get; set; } = "";
    public string TapConnectionGuid { get; set; } = "";
    public int TapConnectionIndex { get; set; }
    public int LocalConnectionIndex { get; set; }
    public int TapAdapterConfiged { get; set; }
    public int ReduceTCPDelayedACK { get; set; } = 1;
    public int DontProxyUDP { get; set; }
    public string LocalConnectionShortestRNextHop { get; set; } = "";
    public int LocalConnectionShortestRDirectHop { get; set; }
    public int LocalConnectionShortestRIfIndex { get; set; }
    public int RegenerateIVForSSUDP { get; set; }
    public int LastProxyModeIndex { get; set; }
    public int IsFirstRunApp { get; set; }
    public int DelayedConnect { get; set; } = 10;
    public int LastUsedNodeId { get; set; }
}
