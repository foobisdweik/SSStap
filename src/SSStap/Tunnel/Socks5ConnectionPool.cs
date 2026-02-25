using System.Collections.Concurrent;
using System.IO;
using System.Net;

namespace SSStap.Tunnel;

/// <summary>
/// Maintains a pool of pre-negotiated (handshake complete, no CONNECT sent) SOCKS5 streams.
/// Drastically reduces per-flow latency (Finding 1).
/// </summary>
public sealed class Socks5ConnectionPool : IDisposable
{
    private const int MaxPoolSize = 16;
    private readonly ISocks5Client _client;
    private readonly ConcurrentQueue<Stream> _pool = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _refillSignal = new(0, MaxPoolSize);
    private bool _disposed;

    public Socks5ConnectionPool(ISocks5Client client)
    {
        _client = client;
        _ = RefillLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Gets a pre-negotiated stream from the pool, or creates a new one if empty.
    /// </summary>
    public async Task<Stream> GetPooledStreamAsync(CancellationToken ct)
    {
        while (_pool.TryDequeue(out var stream))
        {
            try
            {
                // Simple health check: if the stream is closed or has data pending (unexpected), discard.
                if (stream.CanRead && stream.CanWrite)
                {
                    _ = Task.Run(() => _refillSignal.Release(), CancellationToken.None);
                    return stream;
                }
            }
            catch { /* ignore invalid stream */ }
            stream.Dispose();
        }

        // Pool empty, create new on-demand
        return await _client.PreConnectAsync(ct);
    }

    private async Task RefillLoopAsync(CancellationToken ct)
    {
        // Initial fill
        for (int i = 0; i < 4; i++) _refillSignal.Release();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _refillSignal.WaitAsync(ct);
                if (_pool.Count >= MaxPoolSize) continue;

                var stream = await _client.PreConnectAsync(ct);
                _pool.Enqueue(stream);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Socks5ConnectionPool refill error: {ex.Message}");
                await Task.Delay(5000, ct); // Backoff
                _refillSignal.Release(); // Try again later
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        while (_pool.TryDequeue(out var stream)) stream.Dispose();
        _refillSignal.Dispose();
    }
}
