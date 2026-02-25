namespace SSStap.Models;

public record ProxyStatus(
    int TcpActive,
    int UdpActive,
    int TcpTotal,
    int QueueDrops,
    int ThermalState,    // 0=nominal 1=fair 2=serious 3=critical
    int MemoryPressure,  // 0=normal 1=warning 2=critical
    int ProxyPort,
    double Uptime
);
