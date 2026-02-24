// Merged SOCKS5 proxy tester + multi-protocol diagnostics
// Based on socks_tester.ps1 and full_spectrum_packet_transceiver.ps1

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SSStap.Services;

public record LogEntry(string Timestamp, string Message, LogSeverity Severity);

public enum LogSeverity { Info, Warning, Success, Error, Section }

/// <summary>
/// Tests proxy connectivity via SOCKS5 (TCP + UDP) and optionally direct multi-protocol ping.
/// </summary>
public class ProxyTesterService
{
    private const byte Socks5Ver = 5;
    private const byte Socks5AuthNone = 0;
    private const byte Socks5CmdConnect = 1;
    private const byte Socks5CmdUdpAssociate = 3;
    private const byte Socks5AtypIPv4 = 1;
    private const byte Socks5AtypDomain = 3;

    public async Task RunProxyTestAsync(
        string proxyHost,
        int proxyPort,
        string? username,
        string? password,
        IProgress<LogEntry> progress,
        CancellationToken ct = default)
    {
        static string Ts() => DateTime.Now.ToString("HH:mm:ss");

        progress.Report(new LogEntry(Ts(), "========================================", LogSeverity.Section));
        progress.Report(new LogEntry(Ts(), " SOCKS5 Proxy Tester", LogSeverity.Section));
        progress.Report(new LogEntry(Ts(), $" Proxy: {proxyHost}:{proxyPort}", LogSeverity.Section));
        progress.Report(new LogEntry(Ts(), "========================================", LogSeverity.Section));

        var stats = new[] { ("TCP (1.1.1.1)", false, 0L), ("TCP (httpbin.org)", false, 0L), ("UDP (8.8.8.8:53)", false, 0L) };

        // --- TCP Test 1: 1.1.1.1:80 (bypasses DNS) ---
        progress.Report(new LogEntry(Ts(), "\n--- TCP Test 1: 1.1.1.1:80 (bypasses DNS) ---", LogSeverity.Info));
        var (ok1, ms1) = await TestTcpFlowAsync(proxyHost, proxyPort, username, password,
            targetHost: null, targetAddr: IPAddress.Parse("1.1.1.1"), 80,
            progress, Ts, ct);
        stats[0] = ("TCP (1.1.1.1)", ok1, ms1);

        // --- TCP Test 2: httpbin.org:80 (domain name) ---
        progress.Report(new LogEntry(Ts(), "\n--- TCP Test 2: httpbin.org:80 (domain) ---", LogSeverity.Info));
        var (ok2, ms2) = await TestTcpFlowAsync(proxyHost, proxyPort, username, password,
            targetHost: "httpbin.org", targetAddr: null, 80,
            progress, Ts, ct);
        stats[1] = ("TCP (httpbin.org)", ok2, ms2);

        // --- UDP Test: DNS to 8.8.8.8:53 ---
        progress.Report(new LogEntry(Ts(), "\n--- UDP Test (DNS to 8.8.8.8:53) ---", LogSeverity.Info));
        var (ok3, ms3) = await TestUdpFlowAsync(proxyHost, proxyPort, username, password, progress, Ts, ct);
        stats[2] = ("UDP (8.8.8.8:53)", ok3, ms3);

        // --- Summary ---
        progress.Report(new LogEntry(Ts(), "\n--- Summary ---", LogSeverity.Section));
        foreach (var (name, ok, ms) in stats)
        {
            var status = ok ? $"[+] {ms}ms" : "[-] Failed";
            progress.Report(new LogEntry(Ts(), $"  {name}: {status}", ok ? LogSeverity.Success : LogSeverity.Error));
        }
        progress.Report(new LogEntry(Ts(), "========================================", LogSeverity.Section));
    }

    private async Task<(bool Ok, long Ms)> TestTcpFlowAsync(
        string proxyHost, int proxyPort, string? username, string? password,
        string? targetHost, IPAddress? targetAddr, int targetPort,
        IProgress<LogEntry> progress, Func<string> ts, CancellationToken ct)
    {
        var displayTarget = targetAddr != null
            ? $"{targetAddr}:{targetPort}"
            : $"{targetHost}:{targetPort}";

        progress.Report(new LogEntry(ts(), $"[*] Connecting to proxy...", LogSeverity.Info));

        TcpClient? tcpClient = null;
        try
        {
            tcpClient = new TcpClient();
            tcpClient.ReceiveTimeout = 15000;
            tcpClient.SendTimeout = 10000;

            await tcpClient.ConnectAsync(proxyHost, proxyPort, ct);
            var stream = tcpClient.GetStream();

            // Greeting
            var useAuth = !string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password);
            var methods = useAuth ? new[] { Socks5AuthNone, (byte)2 } : new[] { Socks5AuthNone };
            var greeting = new byte[] { Socks5Ver, (byte)methods.Length }.Concat(methods).ToArray();
            await stream.WriteAsync(greeting, ct);

            var resp = new byte[2];
            await ReadExactlyAsync(stream, resp, ct);

            if (resp[1] == 2 && useAuth)
            {
                var user = Encoding.UTF8.GetBytes(username ?? "");
                var pass = Encoding.UTF8.GetBytes(password ?? "");
                var authMsg = new byte[1 + 1 + user.Length + 1 + pass.Length];
                authMsg[0] = 1;
                authMsg[1] = (byte)user.Length;
                user.CopyTo(authMsg, 2);
                authMsg[2 + user.Length] = (byte)pass.Length;
                pass.CopyTo(authMsg, 3 + user.Length);
                await stream.WriteAsync(authMsg, ct);
                var authResp = new byte[2];
                await ReadExactlyAsync(stream, authResp, ct);
                if (authResp[1] != 0)
                    throw new InvalidOperationException("SOCKS5 auth failed");
            }
            else if (resp[1] != Socks5AuthNone)
                throw new InvalidOperationException($"SOCKS5 auth not supported: {resp[1]}");

            // CONNECT request
            byte[] req;
            if (targetAddr != null)
            {
                var addr = targetAddr.GetAddressBytes();
                req = new byte[] { Socks5Ver, Socks5CmdConnect, 0, Socks5AtypIPv4 }
                    .Concat(addr)
                    .Concat(new byte[] { (byte)(targetPort >> 8), (byte)(targetPort & 0xFF) })
                    .ToArray();
            }
            else
            {
                var hostBytes = Encoding.ASCII.GetBytes(targetHost!);
                req = new byte[] { Socks5Ver, Socks5CmdConnect, 0, Socks5AtypDomain, (byte)hostBytes.Length }
                    .Concat(hostBytes)
                    .Concat(new byte[] { (byte)(targetPort >> 8), (byte)(targetPort & 0xFF) })
                    .ToArray();
            }
            var sw = Stopwatch.StartNew();
            await stream.WriteAsync(req, ct);

            var replyHeader = new byte[4];
            await ReadExactlyAsync(stream, replyHeader, ct);
            if (replyHeader[1] != 0)
                throw new InvalidOperationException($"SOCKS5 connect failed: 0x{replyHeader[1]:X2}");

            var boundLen = replyHeader[3] == 1 ? 6 : replyHeader[3] == 3 ? replyHeader[4] + 2 : 18;
            var boundAddr = new byte[boundLen];
            await ReadExactlyAsync(stream, boundAddr, ct);

            sw.Stop();
            progress.Report(new LogEntry(ts(), $"[+] Tunnel established in {sw.ElapsedMilliseconds}ms", LogSeverity.Success));

            // HTTP request
            var hostHeader = targetHost ?? displayTarget;
            var httpReq = Encoding.ASCII.GetBytes($"GET / HTTP/1.1\r\nHost: {hostHeader}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(httpReq, ct);

            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, ct);
            var response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

            if (response.Contains("HTTP/") && response.Contains("200"))
            {
                progress.Report(new LogEntry(ts(), $"[+] TCP SUCCESS! HTTP 200 OK ({bytesRead} bytes)", LogSeverity.Success));
                return (true, sw.ElapsedMilliseconds);
            }
            progress.Report(new LogEntry(ts(), $"[?] Received {bytesRead} bytes", LogSeverity.Info));
            return (bytesRead > 0, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            progress.Report(new LogEntry(ts(), $"[-] TCP ERROR: {ex.Message}", LogSeverity.Error));
            return (false, 0);
        }
        finally
        {
            tcpClient?.Dispose();
        }
    }

    private async Task<(bool Ok, long Ms)> TestUdpFlowAsync(
        string proxyHost, int proxyPort, string? username, string? password,
        IProgress<LogEntry> progress, Func<string> ts, CancellationToken ct)
    {
        TcpClient? controlClient = null;
        UdpClient? udpClient = null;
        try
        {
            controlClient = new TcpClient();
            await controlClient.ConnectAsync(proxyHost, proxyPort, ct);
            var stream = controlClient.GetStream();

            var greeting = new byte[] { Socks5Ver, 1, Socks5AuthNone };
            await stream.WriteAsync(greeting, ct);
            var resp = new byte[2];
            await ReadExactlyAsync(stream, resp, ct);
            if (resp[1] == 2 && (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password)))
            {
                var user = Encoding.UTF8.GetBytes(username ?? "");
                var pass = Encoding.UTF8.GetBytes(password ?? "");
                var authMsg = new byte[1 + 1 + user.Length + 1 + pass.Length];
                authMsg[0] = 1;
                authMsg[1] = (byte)user.Length;
                user.CopyTo(authMsg, 2);
                authMsg[2 + user.Length] = (byte)pass.Length;
                pass.CopyTo(authMsg, 3 + user.Length);
                await stream.WriteAsync(authMsg, ct);
                var authResp = new byte[2];
                await ReadExactlyAsync(stream, authResp, ct);
                if (authResp[1] != 0) throw new InvalidOperationException("SOCKS5 auth failed");
            }

            progress.Report(new LogEntry(ts(), "[*] Requesting UDP ASSOCIATE...", LogSeverity.Info));
            var udpReq = new byte[] { Socks5Ver, Socks5CmdUdpAssociate, 0, Socks5AtypIPv4, 0, 0, 0, 0, 0, 0 };
            await stream.WriteAsync(udpReq, ct);

            var reply = new byte[10];
            await ReadExactlyAsync(stream, reply, ct);
            if (reply[1] != 0)
                throw new InvalidOperationException($"UDP ASSOCIATE failed: 0x{reply[1]:X2}");

            var bindIp = $"{reply[4]}.{reply[5]}.{reply[6]}.{reply[7]}";
            var bindPort = BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(8));
            progress.Report(new LogEntry(ts(), $"[+] Proxy UDP relay at {bindIp}:{bindPort}", LogSeverity.Success));

            udpClient = new UdpClient();
            udpClient.Connect(bindIp, bindPort);
            udpClient.Client.ReceiveTimeout = 5000;

            var socksUdpHeader = new byte[] { 0, 0, 0, 1, 8, 8, 8, 8, 0x00, 0x35 };
            var dnsPayload = new byte[] {
                0xA1, 0xB2, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0,
                0x06, 0x67, 0x6f, 0x6f, 0x67, 0x6c, 0x65, 0x03, 0x63, 0x6f, 0x6d, 0,
                0, 1, 0, 1
            };
            var fullDatagram = socksUdpHeader.Concat(dnsPayload).ToArray();

            progress.Report(new LogEntry(ts(), "[*] Sending DNS query to 8.8.8.8:53...", LogSeverity.Info));
            await udpClient.SendAsync(fullDatagram, ct);

            var sw = Stopwatch.StartNew();
            var result = await udpClient.ReceiveAsync(ct);
            sw.Stop();

            progress.Report(new LogEntry(ts(), $"[+] Received {result.Buffer.Length} bytes in {sw.ElapsedMilliseconds}ms", LogSeverity.Success));
            if (result.Buffer.Length >= 2 && result.Buffer[0] == 0 && result.Buffer[1] == 0)
                progress.Report(new LogEntry(ts(), "[+] UDP SUCCESS!", LogSeverity.Success));
            return (true, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            progress.Report(new LogEntry(ts(), $"[-] UDP ERROR: {ex.Message}", LogSeverity.Error));
            return (false, 0);
        }
        finally
        {
            udpClient?.Dispose();
            controlClient?.Dispose();
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
    }
}
