using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SSStap.Models;
using SSStap.Tunnel;

namespace SSStap.Services;

public sealed class ProxyHealthMonitor : IDisposable
{
    private const int ControlPort = 9998;
    private static readonly byte[] Magic = { 0x53, 0x53, 0x54, 0x50 }; // "SSTP"

    private readonly UdpClient _udpClient = new();
    private readonly IPAddress _proxyAddress;
    private readonly TunnelEngine _engine;
    private readonly CancellationTokenSource _cts = new();

    private DateTime _lastUpdate = DateTime.MinValue;
    private bool _hasEverConnected = false;

    public event Action<ProxyStatus>? StatusUpdated;

    public ProxyStatus? LastStatus { get; private set; }

    public ProxyHealthMonitor(string proxyHost, TunnelEngine engine)
    {
        _engine = engine;
        
        if (!IPAddress.TryParse(proxyHost, out var addr))
        {
            try { addr = System.Net.Dns.GetHostAddresses(proxyHost).FirstOrDefault() ?? IPAddress.Loopback; }
            catch { addr = IPAddress.Loopback; }
        }
        _proxyAddress = addr;

        _udpClient.Client.ReceiveTimeout = 1000;
        _ = PollLoopAsync(_cts.Token);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var remoteEp = new IPEndPoint(_proxyAddress, ControlPort);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Send magic
                await _udpClient.SendAsync(Magic, Magic.Length, remoteEp);

                // Receive response (with timeout via ReceiveTimeout on socket)
                var result = await _udpClient.ReceiveAsync(ct);
                _lastUpdate = DateTime.UtcNow;

                var json = Encoding.UTF8.GetString(result.Buffer);
                var status = JsonSerializer.Deserialize<ProxyStatus>(json, options);
                if (status != null)
                {
                    LastStatus = status;
                    _hasEverConnected = true;
                    
                    // Logic from Finding 9:
                    // On thermalState >= 2: signal TunnelEngine to stop accepting new flows.
                    _engine.IsThrottled = status.ThermalState >= 2;

                    StatusUpdated?.Invoke(status);
                }
            }
            catch (Exception)
            {
                // Silent catch for timeouts/network errors
            }

            // On 5s silence: call TunnelEngine.FlushAllConnections() immediately.
            if (_hasEverConnected && (DateTime.UtcNow - _lastUpdate).TotalSeconds >= 5)
            {
                _hasEverConnected = false; // Reset so we don't spam flush
                await _engine.FlushAllConnectionsAsync();
            }

            await Task.Delay(2000, ct);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _udpClient.Dispose();
    }
}
