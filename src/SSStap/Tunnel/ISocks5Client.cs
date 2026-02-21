using System.IO;
using System.Net;
using System.Net.Sockets;

namespace SSStap.Tunnel;

/// <summary>
/// SOCKS5 client interface for TCP CONNECT and UDP ASSOCIATE.
/// </summary>
public interface ISocks5Client
{
    /// <summary>Establishes a TCP connection through SOCKS5 CONNECT to the target.</summary>
    Task<Stream> ConnectTcpAsync(IPAddress targetAddress, int targetPort, string? hostname = null, CancellationToken ct = default);

    /// <summary>Opens UDP ASSOCIATE and returns the relay endpoint for sending encapsulated UDP datagrams.</summary>
    Task<UdpRelayInfo> OpenUdpAssociateAsync(CancellationToken ct = default);
}

/// <summary>Info returned from UDP ASSOCIATE. BindAddress is the 4-byte IP + 2-byte port for SOCKS5 UDP envelope. Dispose to close the TCP control connection and UDP socket.</summary>
public class UdpRelayInfo(IPEndPoint relayEndPoint, byte[] bindAddress) : IDisposable
{
    public IPEndPoint RelayEndPoint { get; } = relayEndPoint;
    public byte[] BindAddress { get; } = bindAddress;
    internal TcpClient? ControlConnection { get; set; }
    private bool _disposed;

    /// <summary>UDP socket used to send/receive datagrams to the SOCKS5 relay endpoint.</summary>
    public UdpClient? UdpSocket { get; set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            UdpSocket?.Dispose();
            UdpSocket = null;
            ControlConnection?.Dispose();
            ControlConnection = null;
        }

        _disposed = true;
    }
}
