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

    public async Task<Stream> ConnectTcpAsync(IPAddress targetAddress, int targetPort, CancellationToken ct = default)
    {
        var client = new TcpClient();
        await client.ConnectAsync(Host, Port, ct);

        var stream = client.GetStream();

        // Greeting: VER(5) NMETHODS(1) METHODS(1)
        bool useAuth = !string.IsNullOrEmpty(Username) || !string.IsNullOrEmpty(Password);
        var methods = useAuth ? new[] { AuthNone, AuthUserPass } : new[] { AuthNone };
        var greeting = new byte[2 + methods.Length];
        greeting[0] = Version;
        greeting[1] = (byte)methods.Length;
        Array.Copy(methods, 0, greeting, 2, methods.Length);
        await stream.WriteAsync(greeting, ct);

        // Server response: VER(5) METHOD(1)
        var resp = new byte[2];
        await ReadExactlyAsync(stream, resp, ct);
        if (resp[0] != Version)
            throw new InvalidOperationException($"SOCKS5 version mismatch: {resp[0]}");

        if (resp[1] == AuthUserPass && useAuth)
        {
            await SendUserPassAuthAsync(stream, ct);
        }
        else if (resp[1] != AuthNone)
        {
            throw new InvalidOperationException($"SOCKS5 auth not supported: {resp[1]}");
        }

        // CONNECT: VER(5) CMD(1) RSV(1) ATYP(1) DST.ADDR DST.PORT
        var addrBytes = targetAddress.GetAddressBytes();
        if (addrBytes.Length != 4)
            throw new ArgumentException("IPv4 only", nameof(targetAddress));

        var request = new byte[4 + 4 + 2]; // 4 + addr + 2 port
        request[0] = Version;
        request[1] = CmdConnect;
        request[2] = 0;
        request[3] = AtypIPv4;
        addrBytes.CopyTo(request, 4);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8), (ushort)targetPort);
        await stream.WriteAsync(request, ct);

        // Response: VER(5) REP(1) RSV(1) ATYP(1) BND.ADDR BND.PORT
        var rep = new byte[4];
        await ReadExactlyAsync(stream, rep, ct);
        if (rep[0] != Version)
            throw new InvalidOperationException($"SOCKS5 reply version mismatch: {rep[0]}");
        if (rep[1] != 0)
            throw new InvalidOperationException($"SOCKS5 connect failed: {rep[1]}");

        int addrLen = rep[3] == AtypIPv4 ? 4 : (rep[3] == 3 ? rep[4] : 16);
        var bindAddr = new byte[1 + addrLen + 2];
        bindAddr[0] = rep[3];
        await ReadExactlyAsync(stream, bindAddr.AsMemory(1, addrLen + 2), ct);

        return stream;
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
