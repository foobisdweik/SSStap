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
        if (!TryReadResponseHeader(dnsPayload, out var questionCount, out var answerCount))
            return;

        var offset = 12;
        if (!TryReadQuestionSection(dnsPayload, questionCount, ref offset, out var hostname))
            return;

        ParseAnswerSection(dnsPayload, answerCount, ref offset, hostname);
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

    private static bool TryReadResponseHeader(ReadOnlySpan<byte> dnsPayload, out ushort questionCount, out ushort answerCount)
    {
        questionCount = 0;
        answerCount = 0;

        if (dnsPayload.Length < 12)
            return false;

        var flags = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(2, 2));
        if ((flags & 0x8000) == 0)
            return false;

        questionCount = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(4, 2));
        answerCount = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(6, 2));
        return questionCount > 0 && answerCount > 0;
    }

    private static bool TryReadQuestionSection(ReadOnlySpan<byte> dnsPayload, ushort questionCount, ref int offset, out string hostname)
    {
        hostname = string.Empty;

        for (var i = 0; i < questionCount; i++)
        {
            if (!TryReadName(dnsPayload, ref offset, out var questionHostname))
                return false;

            if (i == 0 && !string.IsNullOrEmpty(questionHostname))
                hostname = questionHostname;

            if (!TrySkipBytes(dnsPayload, ref offset, 4))
                return false;
        }

        return !string.IsNullOrEmpty(hostname);
    }

    private void ParseAnswerSection(ReadOnlySpan<byte> dnsPayload, ushort answerCount, ref int offset, string hostname)
    {
        for (var i = 0; i < answerCount; i++)
        {
            if (!TryReadName(dnsPayload, ref offset, out _))
                return;

            if (!TryReadAnswerRecordHeader(dnsPayload, ref offset, out var type, out var dnsClass, out var ttlSeconds, out var rdLength))
                return;

            if (offset + rdLength > dnsPayload.Length)
                return;

            TryCacheAnswer(dnsPayload.Slice(offset, rdLength), hostname, type, dnsClass, ttlSeconds, rdLength);
            offset += rdLength;
        }
    }

    private static bool TryReadAnswerRecordHeader(
        ReadOnlySpan<byte> dnsPayload,
        ref int offset,
        out ushort type,
        out ushort dnsClass,
        out uint ttlSeconds,
        out ushort rdLength)
    {
        type = 0;
        dnsClass = 0;
        ttlSeconds = 0;
        rdLength = 0;

        if (offset + 10 > dnsPayload.Length)
            return false;

        type = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(offset, 2));
        dnsClass = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(offset + 2, 2));
        ttlSeconds = BinaryPrimitives.ReadUInt32BigEndian(dnsPayload.Slice(offset + 4, 4));
        rdLength = BinaryPrimitives.ReadUInt16BigEndian(dnsPayload.Slice(offset + 8, 2));
        offset += 10;
        return true;
    }

    private void TryCacheAnswer(
        ReadOnlySpan<byte> rdata,
        string hostname,
        ushort type,
        ushort dnsClass,
        uint ttlSeconds,
        ushort rdLength)
    {
        if (dnsClass != 1)
            return;

        if (type == 1 && rdLength == 4)
        {
            CacheMapping(new IPAddress(rdata), hostname, ttlSeconds);
            return;
        }

        if (type == 28 && rdLength == 16)
            CacheMapping(new IPAddress(rdata), hostname, ttlSeconds);
    }

    private static bool TrySkipBytes(ReadOnlySpan<byte> payload, ref int offset, int count)
    {
        if (offset + count > payload.Length)
            return false;

        offset += count;
        return true;
    }

    private static bool TryReadName(ReadOnlySpan<byte> payload, ref int offset, out string name)
    {
        name = string.Empty;
        if (offset < 0 || offset >= payload.Length)
            return false;

        var current = offset;
        var consumedOffset = offset;
        var jumped = false;
        var remainingJumps = payload.Length;
        var builder = new StringBuilder();

        while (current < payload.Length)
        {
            if (!TryConsumeNameToken(payload, ref current, ref consumedOffset, ref jumped, ref remainingJumps, builder, out var completed))
                return false;

            if (!completed)
                continue;

            offset = consumedOffset;
            name = builder.ToString();
            return true;
        }

        return false;
    }

    private static bool TryConsumeNameToken(
        ReadOnlySpan<byte> payload,
        ref int current,
        ref int consumedOffset,
        ref bool jumped,
        ref int remainingJumps,
        StringBuilder builder,
        out bool completed)
    {
        completed = false;

        if (!TryReadLabelLength(payload, current, out var len))
            return false;

        if (IsPointerLabel(len))
            return TryFollowPointer(payload, ref current, ref consumedOffset, ref jumped, ref remainingJumps);

        if (len == 0)
        {
            if (!jumped)
                consumedOffset = current + 1;
            completed = true;
            return true;
        }

        if (!TryAppendLabel(payload, ref current, len, builder))
            return false;

        if (!jumped)
            consumedOffset = current;

        return true;
    }

    private static bool TryReadLabelLength(ReadOnlySpan<byte> payload, int current, out byte length)
    {
        length = 0;
        if (current < 0 || current >= payload.Length)
            return false;

        length = payload[current];
        return true;
    }

    private static bool IsPointerLabel(byte length) => (length & 0xC0) == 0xC0;

    private static bool TryFollowPointer(
        ReadOnlySpan<byte> payload,
        ref int current,
        ref int consumedOffset,
        ref bool jumped,
        ref int remainingJumps)
    {
        if (current + 1 >= payload.Length)
            return false;

        if (remainingJumps <= 0)
            return false;

        var pointer = ((payload[current] & 0x3F) << 8) | payload[current + 1];
        if (pointer >= payload.Length)
            return false;

        if (!jumped)
        {
            consumedOffset = current + 2;
            jumped = true;
        }

        current = pointer;
        remainingJumps--;
        return true;
    }

    private static bool TryAppendLabel(ReadOnlySpan<byte> payload, ref int current, byte length, StringBuilder builder)
    {
        if ((length & 0xC0) != 0)
            return false;

        current++;
        if (current + length > payload.Length)
            return false;

        if (builder.Length > 0)
            builder.Append('.');

        builder.Append(Encoding.ASCII.GetString(payload.Slice(current, length)));
        current += length;
        return true;
    }

    private readonly record struct CacheEntry(string Hostname, DateTimeOffset ExpiresAtUtc);
}