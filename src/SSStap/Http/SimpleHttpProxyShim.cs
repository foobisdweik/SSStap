using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace SSStap.Http;

/// <summary>
/// Lightweight HTTP proxy that forwards requests via SOCKS5.
/// Replaces Privoxy for browser-only mode. Listens on 127.0.0.1:25378 by default.
/// </summary>
public sealed class SimpleHttpProxyShim : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private bool _disposed;

    public int ListenPort { get; }
    public string SocksHost { get; }
    public int SocksPort { get; }
    public bool IsRunning => _listener is not null;

    public event Action<string>? OnLog;

    public SimpleHttpProxyShim(
        int listenPort = 25378,
        string socksHost = "127.0.0.1",
        int socksPort = 16666)
    {
        ListenPort = listenPort;
        SocksHost = socksHost;
        SocksPort = socksPort;
    }

    public void Start()
    {
        if (IsRunning)
            throw new InvalidOperationException("HTTP proxy shim is already running.");

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, ListenPort);
        _listener.Start();
        Log($"Listening on 127.0.0.1:{ListenPort}, forwarding via SOCKS5 to {SocksHost}:{SocksPort}");

        _acceptLoop = RunAcceptLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch (ObjectDisposedException) { }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }
        }

        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Stop() => StopAsync().GetAwaiter().GetResult();

    private async Task RunAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { } listener)
        {
            try
            {
                TcpClient client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Log($"Accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var clientStream = client.GetStream())
        {
            try
            {
                byte[] buffer = new byte[8192];
                int received = await clientStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (received <= 0) return;

                string request = Encoding.ASCII.GetString(buffer, 0, received);

                // Parse HTTP request line (METHOD URL HTTP/x.x)
                var match = Regex.Match(request, @"^(CONNECT|GET|POST|PUT|DELETE|HEAD|PATCH)\s+(\S+)\s+HTTP/[\d.]+", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    await SendErrorResponseAsync(clientStream, 400, "Bad Request").ConfigureAwait(false);
                    return;
                }

                string method = match.Groups[1].Value.ToUpperInvariant();
                string url = match.Groups[2].Value;

                string host;
                int port;

                if (method == "CONNECT")
                {
                    // CONNECT host:port HTTP/1.1
                    var hostMatch = Regex.Match(url, @"^([^:]+):(\d+)$");
                    if (!hostMatch.Success)
                    {
                        await SendErrorResponseAsync(clientStream, 400, "Bad Request").ConfigureAwait(false);
                        return;
                    }
                    host = hostMatch.Groups[1].Value;
                    port = int.Parse(hostMatch.Groups[2].Value);
                }
                else
                {
                    // GET http://host[:port]/path HTTP/1.1
                    if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                        url = url["http://".Length..];
                    else if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        url = url["https://".Length..];

                    int slash = url.IndexOf('/');
                    string authority = slash >= 0 ? url[..slash] : url;
                    int colon = authority.IndexOf(':');
                    host = colon >= 0 ? authority[..colon] : authority;
                    port = colon >= 0 ? int.Parse(authority[(colon + 1)..]) : 80;
                }

                // Connect to target via SOCKS5
                var socksConn = await ConnectViaSocks5Async(host, port, ct).ConfigureAwait(false);
                if (socksConn is null)
                {
                    await SendErrorResponseAsync(clientStream, 502, "Bad Gateway (SOCKS5 failed)").ConfigureAwait(false);
                    return;
                }

                using (socksConn.Value.client)
                {
                    var socksStream = socksConn.Value.stream;
                    if (method == "CONNECT")
                    {
                        // Tunnel: send 200 Connection established, then bidirectional relay
                        await SendConnectOkAsync(clientStream).ConfigureAwait(false);
                        await RelayBidirectionalAsync(clientStream, socksStream, buffer, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        // HTTP: forward request, stream response
                        await socksStream.WriteAsync(buffer.AsMemory(0, received), ct).ConfigureAwait(false);
                        await RelayUnidirectionalAsync(socksStream, clientStream, buffer, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log($"Client error: {ex.Message}");
            }
        }
    }

    private async Task<(Stream stream, TcpClient client)?> ConnectViaSocks5Async(string host, int port, CancellationToken ct)
    {
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(SocksHost, SocksPort, ct).ConfigureAwait(false);
            var stream = tcp.GetStream();

            // SOCKS5 handshake: no auth
            byte[] handshake = [0x05, 0x01, 0x00]; // VER, NMETHODS, NO AUTH
            await stream.WriteAsync(handshake, ct).ConfigureAwait(false);

            byte[] reply = new byte[2];
            int n = await stream.ReadAsync(reply, ct).ConfigureAwait(false);
            if (n < 2 || reply[0] != 0x05 || reply[1] != 0x00)
                return null;

            // CONNECT command with domain name
            byte[] hostBytes = Encoding.ASCII.GetBytes(host);
            byte[] connect = new byte[7 + hostBytes.Length];
            connect[0] = 0x05; // VER
            connect[1] = 0x01; // CONNECT
            connect[2] = 0x00; // RSV
            connect[3] = 0x03; // ATYP domain
            connect[4] = (byte)hostBytes.Length;
            hostBytes.CopyTo(connect, 5);
            connect[5 + hostBytes.Length] = (byte)(port >> 8);
            connect[6 + hostBytes.Length] = (byte)(port & 0xFF);

            await stream.WriteAsync(connect, ct).ConfigureAwait(false);

            byte[] connReply = new byte[10];
            n = await stream.ReadAsync(connReply, ct).ConfigureAwait(false);
            if (n < 4 || connReply[1] != 0x00)
                return null;

            return (stream, tcp);
        }
        catch
        {
            tcp.Dispose();
            return null;
        }
    }

    private static async Task SendConnectOkAsync(Stream clientStream)
    {
        const string response = "HTTP/1.1 200 Connection Established\r\n\r\n";
        await clientStream.WriteAsync(Encoding.ASCII.GetBytes(response)).ConfigureAwait(false);
    }

    private static async Task SendErrorResponseAsync(Stream clientStream, int code, string message)
    {
        string body = $"<html><body><h1>{code} {message}</h1></body></html>";
        string response = $"HTTP/1.1 {code} {message}\r\nContent-Length: {body.Length}\r\n\r\n{body}";
        await clientStream.WriteAsync(Encoding.ASCII.GetBytes(response)).ConfigureAwait(false);
    }

    private static async Task RelayBidirectionalAsync(Stream a, Stream b, byte[] buffer, CancellationToken ct)
    {
        var t1 = RelayOneWayAsync(a, b, buffer, ct);
        var t2 = RelayOneWayAsync(b, a, new byte[buffer.Length], ct);
        await Task.WhenAny(t1, t2).ConfigureAwait(false);
    }

    private static async Task RelayUnidirectionalAsync(Stream from, Stream to, byte[] buffer, CancellationToken ct)
    {
        int n;
        while ((n = await from.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            await to.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
    }

    private static async Task RelayOneWayAsync(Stream from, Stream to, byte[] buffer, CancellationToken ct)
    {
        try
        {
            int n;
            while ((n = await from.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                await to.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        finally
        {
            try { from.Close(); } catch { }
            try { to.Close(); } catch { }
        }
    }

    private void Log(string message) => OnLog?.Invoke(message);

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
