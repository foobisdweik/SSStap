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
    public string OnlinePacUrl { get; set; } = "\