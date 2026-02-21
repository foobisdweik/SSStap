using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace SSStap.Tunnel;

/// <summary>
/// Intercepts DNS responses and caches IP to hostname mappings using answer TTL expiry.
/// </summary>
public sealed class DnsInterceptCache
{
    private readonly ConcurrentDictionary<IPAddress, CacheEntry> _ipToHostname = new();

    /// <summary>
    /// Parses a DNS response payload and stores A/AAAA answer IP mappings for the first question hostname.
    /// </summary>
    public void InterceptDnsResponse(ReadOnlySpan<byte> dnsPayload)
    {
        if (dnsPayload.Length < 12)
            return;

        var flags = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(2, 2));
        var isResponse = (flags & 0x8000) != 0;
        if (!isResponse)
            return;

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(4, 2));
        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(6, 2));
        if (questionCount == 0 || answerCount == 0)
            return;

        var offset = 12;
        string? hostname = null;
        for (var i = 0; i < questionCount; i++)
        {
            if (!TryReadName(dnsPayload, ref offset, out var questionHostname))
                return;

            if (i == 0 && !string.IsNullOrEmpty(questionHostname))
                hostname = questionHostname;

            if (offset + 4 > dnsPayload.Length)
                return;
            offset += 4; // QTYPE + QCLASS
        }

        if (string.IsNullOrEmpty(hostname))
            return;

        for (var i = 0; i < answerCount; i++)
        {
            if (!TryReadName(dnsPayload, ref offset, out _))
                return;

            if (offset + 10 > dnsPayload.Length)
                return;

            var type = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(offset, 2));
            var dnsClass = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(offset + 2, 2));
            var ttlSeconds = BinaryPrimitives.ReadUInt32BigEndian(dnsPayload.Slice(offset + 4, 4));
            var rdLength = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(offset + 8, 2));
            offset += 10;

            if (offset + rdLength > dnsPayload.Length)
                return;

            if (dnsClass == 1)
            {
                if (type == 1 && rdLength == 4)
                {
                    CacheMapping(new IPAddress(dnsPayload.Slice(offset, 4)), hostname, ttlSeconds);
                }
                else if (type == 28 && rdLength == 16)
                {
                    CacheMapping(new IPAddress(dnsPayload.Slice(offset, 16)), hostname, ttlSeconds);
                }
            }

            offset += rdLength;
        }
    }

    /// <summary>
    /// Attempts to resolve a destination IP address to a hostname from cache.
    /// </summary>
    public bool TryGetHostname(IPAddress address, out string hostname)
    {
        hostname = string.Empty;
        if (!_ipToHostname.TryGetValue(address, out var entry))
            return false;

        if (entry.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _ipToHostname.TryRemove(address, out _);
            return false;
        }

        hostname = entry.Hostname;
        return true;
    }

    private void CacheMapping(IPAddress address, string hostname, uint ttlSeconds)
    {
        var ttl = Math.Max(1, (long)ttlSeconds);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(ttl);
        _ipToHostname[address] = new CacheEntry(hostname, expiresAt);
    }

    private static bool TryReadName(ReadOnlySpan<byte> payload, ref int offset, out string name)
    {
        name = string.Empty;
        if (offset < 0 || offset >= payload.Length)
            return false;

        var current = offset;
        var consumedOffset = offset;
        var jumped = false;
        var jumps = 0;
        var builder = new StringBuilder();

        while (current < payload.Length)
        {
            var len = payload[current];

            if ((len & 0xC0) == 0xC0)
            {
                if (current + 1 >= payload.Length)
                    return false;

                var pointer = ((len & 0x3F) << 8) | payload[current + 1];
                if (pointer >= payload.Length)
                    return false;

                if (!jumped)
                {
                    consumedOffset = current + 2;
                    jumped = true;
                }

                current = pointer;
                jumps++;
                if (jumps > payload.Length)
                    return false;
                continue;
            }

            if (len == 0)
            {
                if (!jumped)
                    consumedOffset = current + 1;

                offset = consumedOffset;
                name = builder.ToString();
                return true;
            }

            if ((len & 0xC0) != 0)
                return false;

            current++;
            if (current + len > payload.Length)
                return false;

            if (builder.Length > 0)
                builder.Append('.');

            builder.Append(Encoding.ASCII.GetString(payload.Slice(current, len)));
            current += len;

            if (!jumped)
                consumedOffset = current;
        }

        return false;
    }

    private readonly record struct CacheEntry(string Hostname, DateTimeOffset ExpiresAtUtc);
}