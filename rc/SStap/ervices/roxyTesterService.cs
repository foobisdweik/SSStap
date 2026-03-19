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

public enum LogSeverity { Info, Success, Warning, Error, Section }

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
        static string Ts() => DateTime.Now.ToString("\H:mm:ss"\;

        progress.Report(new LogEntry(Ts(), "\======================================="\ LogSeverity.Section));
        progress.Report(new LogEntry(Ts(), "\SOCKS5 Proxy Tester"\ LogSeverity.Section));
        progress.Report(new LogEntry(Ts(), $"\Proxy: {proxyHost}:{proxyPort}"\ LogSeverity.Section));
        progress.Report(new LogEntry(Ts(), "\======================================="\ LogSeverity.Section));

        var stats = new[] { ("\CP (1.1.1.1)"\ false, 0L), ("\CP (httpbin.org)"\ false, 0L), ("\DP (8.8.8.8:53)"\ false, 0L) };

        // --- TCP Test 1: 1.1.1.1:80 (bypasses DNS) ---
        progress.Report(new LogEntry(Ts(), "\\\
\-- TCP Test 1: 1.1.1.1:80 (bypasses DNS) ---"\ LogSeverity.Info));
        var (ok1, ms1) = await TestTcpFlowAsync(proxyHost, proxyPort, username, password,
            targetHost: null, targetAddr: IPAddress.Parse("\.1.1.1"\, 80,
            progress, Ts, ct);
        stats[0] = ("\CP (1.1.1.1)"\ ok1, ms1);

        // --- TCP Test 2: httpbin.org:80 (domain name) ---
        progress.Report(new LogEntry(Ts(), "\\\
\-- TCP Test 2: httpbin.org:80 (domain) ---"\ LogSeverity.Info));
        var (ok2, ms2) = await TestTcpFlowAsync(proxyHost, proxyPort, username, password,
            targetHost: "\ttpbin.org"\ targetAddr: null, 80,
            progress, Ts, ct);
        stats[1] = ("\CP (httpbin.org)"\ ok2, ms2);

        // --- UDP Test: DNS to 8.8.8.8:53 ---
        progress.Report(new LogEntry(Ts(), "\\\
\-- UDP Test (DNS to 8.8.8.8:53) ---"\ LogSeverity.Info));
        var (ok3, ms3) = await TestUdpFlowAsync(proxyHost, proxyPort, username, password, progress, Ts, ct);
        stats[2] = ("\DP (8.8.8.8:53)"\ ok3, ms3);

        // --- Summary ---
        progress.Report(new LogEntry(Ts(), "\\\
\-- Summary ---"\ LogSeverity.Section));
        foreach (var (name, ok, ms) in stats)
        {
            var status = ok ? $"\+] {ms}ms"\: "\-] Failed"\
            progress.Report(new LogEntry(Ts(), $"\ {name}: {status}"\ ok ? LogSeverity.Success : LogSeverity.Error));
        }
        progress.Report(new LogEntry(Ts(), "\======================================="\ LogSeverity.Section));
    }

    private async Task<(bool Ok, long Ms)> TestTcpFlowAsync(
        string proxyHost, int proxyPort, string? username, string? password,
        string? targetHost, IPAddress? targetAddr, int targetPort,
        IProgress<LogEntry> progress, Func<string> ts, CancellationToken ct)
    {
        var displayTarget = targetAddr != null
            ? $"\targetAddr}:{targetPort}"\
            : $"\targetHost}:{targetPort}"\

        progress.Report(new LogEntry(ts(), $"\*] Connecting to proxy..."\ LogSeverity.Info));

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
                var user = Encoding.UTF8.GetBytes(username ?? "\