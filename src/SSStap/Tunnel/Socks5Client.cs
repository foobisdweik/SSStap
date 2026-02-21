using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SSStap.Tunnel;

/// <summary>
/// SOCKS5 client implementation for TCP CONNECT and UDP ASSOCIATE.
/// RFC 1928 (SOCKS5), RFC 1929 (auth).
/// </summary>
public sealed class Socks5Client : ISocks5Client
{
    private const byte Version = 5;
    private const byte CmdConnect = 1;
    private const byte CmdUdpAssociate = 3;
    private const byte AtypIPv4 = 1;
    private const byte AtypDomain = 3;
    private const byte AtypIPv6 = 4;
    private const byte AuthNone = 0;
    private const byte AuthUserPass = 2;

    public string Host { get; }
    public int Port { get; }
    public string? Username { get; }
    public string? Password { get; }

    public Socks5Client(string host, int port, string? username = null, string? password = null)
    {
        Host = host;
        Port = port;
        Username = username;
        Password = password;
    }

    public async Task<Stream> ConnectTcpAsync(IPAddress targetAddress, int targetPort, string? hostname = null, CancellationToken ct = default)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(Host, Port, ct);

            var stream = client.GetStream();
            await NegotiateAuthAsync(stream, ct);

            var request = BuildConnectRequest(targetAddress, targetPort, hostname);
            await stream.WriteAsync(request, ct);

            await ReadConnectReplyAsync(stream, ct);
            return stream;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<UdpRelayInfo> OpenUdpAssociateAsync(CancellationToken ct = default)
    {
        var client = new TcpClient();
        await client.ConnectAsync(Host, Port, ct);
        var stream = client.GetStream();

        var methods = !string.IsNullOrEmpty(Username) || !string.IsNullOrEmpty(Password)
            ? new[] { AuthNone, AuthUserPass }
            : new[] { AuthNone };
        var greeting = new byte[2 + methods.Length];
        greeting[0] = Version;
        greeting[1] = (byte)methods.Length;
        Array.Copy(methods, 0, greeting, 2, methods.Length);
        await stream.WriteAsync(greeting, ct);

        var resp = new byte[2];
        await ReadExactlyAsync(stream, resp, ct);
        if (resp[0] != Version)
            throw new InvalidOperationException($"SOCKS5 version mismatch: {resp[0]}");
        if (resp[1] == AuthUserPass)
            await SendUserPassAuthAsync(stream, ct);
        else if (resp[1] != AuthNone)
            throw new InvalidOperationException($"SOCKS5 auth not supported: {resp[1]}");

        var request = new byte[10];
        request[0] = Version;
        request[1] = CmdUdpAssociate;
        request[2] = 0;
        request[3] = AtypIPv4;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8), 0);
        await stream.WriteAsync(request, ct);

        var rep = new byte[4];
        await ReadExactlyAsync(stream, rep, ct);
        if (rep[1] != 0)
            throw new InvalidOperationException($"SOCKS5 UDP ASSOCIATE failed: {rep[1]}");

        byte atyp = rep[3];
        byte[] bindAddress;
        ushort bindPort;
        if (atyp == AtypIPv4)
        {
            var addr = new byte[6];
            await ReadExactlyAsync(stream, addr, ct);
            bindAddress = addr;
            bindPort = BinaryPrimitives.ReadUInt16BigEndian(addr.AsSpan(4));
        }
        else
        {
            throw new NotSupportedException("IPv6 SOCKS5 relay not yet supported");
        }

        var relayIp = new IPAddress(bindAddress.AsSpan(0, 4));
        var relayEp = new IPEndPoint(relayIp, bindPort);

        return new UdpRelayInfo(relayEp, bindAddress) { ControlConnection = client };
    }

    private async Task SendUserPassAuthAsync(Stream stream, CancellationToken ct)
    {
        var user = Encoding.UTF8.GetBytes(Username ?? "");
        var pass = Encoding.UTF8.GetBytes(Password ?? "");
        var msg = new byte[1 + 1 + user.Length + 1 + pass.Length];
        msg[0] = 1;
        msg[1] = (byte)user.Length;
        user.CopyTo(msg, 2);
        msg[2 + user.Length] = (byte)pass.Length;
        pass.CopyTo(msg, 3 + user.Length);
        await stream.WriteAsync(msg, ct);

        var authResp = new byte[2];
        await ReadExactlyAsync(stream, authResp, ct);
        if (authResp[1] != 0)
            throw new InvalidOperationException("SOCKS5 username/password auth failed");
    }

    private async Task NegotiateAuthAsync(Stream stream, CancellationToken ct)
    {
        bool useAuth = !string.IsNullOrEmpty(Username) || !string.IsNullOrEmpty(Password);
        var methods = useAuth ? new[] { AuthNone, AuthUserPass } : new[] { AuthNone };
        var greeting = new byte[2 + methods.Length];
        greeting[0] = Version;
        greeting[1] = (byte)methods.Length;
        Array.Copy(methods, 0, greeting, 2, methods.Length);
        await stream.WriteAsync(greeting, ct);

        var response = new byte[2];
        await ReadExactlyAsync(stream, response, ct);
        if (response[0] != Version)
            throw new InvalidOperationException($"SOCKS5 version mismatch: {response[0]}");

        if (response[1] == AuthUserPass && useAuth)
        {
            await SendUserPassAuthAsync(stream, ct);
            return;
        }

        if (response[1] != AuthNone)
            throw new InvalidOperationException($"SOCKS5 auth not supported: {response[1]}");
    }

    private static byte[] BuildConnectRequest(IPAddress targetAddress, int targetPort, string? hostname)
    {
        if (!string.IsNullOrEmpty(hostname))
        {
            return BuildDomainConnectRequest(targetPort, hostname);
        }

        var addrBytes = targetAddress.GetAddressBytes();
        if (addrBytes.Length != 4)
            throw new ArgumentException("IPv4 only", nameof(targetAddress));

        var request = new byte[4 + 4 + 2];
        request[0] = Version;
        request[1] = CmdConnect;
        request[2] = 0;
        request[3] = AtypIPv4;
        addrBytes.CopyTo(request, 4);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8), (ushort)targetPort);
        return request;
    }

    private static byte[] BuildDomainConnectRequest(int targetPort, string hostname)
    {
        if (hostname.Length > 255)
            throw new ArgumentException("Hostname length must be <= 255", nameof(hostname));
        if (!hostname.All(c => c <= 0x7F))
            throw new ArgumentException("Hostname must contain only ASCII characters", nameof(hostname));

        var hostBytes = Encoding.ASCII.GetBytes(hostname);
        var request = new byte[4 + 1 + hostBytes.Length + 2];
        request[0] = Version;
        request[1] = CmdConnect;
        request[2] = 0;
        request[3] = AtypDomain;
        request[4] = (byte)hostBytes.Length;
        hostBytes.CopyTo(request, 5);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(5 + hostBytes.Length), (ushort)targetPort);
        return request;
    }

    private static async Task ReadConnectReplyAsync(Stream stream, CancellationToken ct)
    {
        var rep = new byte[4];
        await ReadExactlyAsync(stream, rep, ct);
        if (rep[0] != Version)
            throw new InvalidOperationException($"SOCKS5 reply version mismatch: {rep[0]}");
        if (rep[1] != 0)
            throw new InvalidOperationException($"SOCKS5 connect failed: {rep[1]}");

        await ReadBindAddressAsync(stream, rep[3], ct);
    }

    private static async Task ReadBindAddressAsync(Stream stream, byte atyp, CancellationToken ct)
    {
        if (atyp == AtypIPv4)
        {
            var bindAddr = new byte[4 + 2];
            await ReadExactlyAsync(stream, bindAddr, ct);
            return;
        }

        if (atyp == AtypIPv6)
        {
            var bindAddr = new byte[16 + 2];
            await ReadExactlyAsync(stream, bindAddr, ct);
            return;
        }

        if (atyp == AtypDomain)
        {
            var len = new byte[1];
            await ReadExactlyAsync(stream, len, ct);
            var bindAddr = new byte[len[0] + 2];
            await ReadExactlyAsync(stream, bindAddr, ct);
            return;
        }

        throw new InvalidOperationException($"SOCKS5 reply address type not supported: {atyp}");
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.Slice(read), ct);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
    }
}
