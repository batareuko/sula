namespace AionMeter.Core.Capture;

/// <summary>
/// 1 HP: health of the game's TCP stream as the capture sees it, shared by all reassemblers of the game flows.
/// A hole is a segment that arrived before an earlier one: the earlier segment was lost on the way and the server
/// has to send it again — the packet loss the player feels as lag. Updated on the capture thread, read by the UI.
/// <para>
/// The other direction counts too: <see cref="UpstreamResends"/> are segments this PC sent to the game server again
/// (its first copy, or the server's acknowledgement of it, was lost) — a skill that reaches the server late. And
/// <see cref="Stalls"/> are pauses of half a second or more in the server's stream, which otherwise sends ~19 frames a
/// second: a freeze the player feels even with no packet lost.
/// </para>
/// </summary>
public sealed class StreamCounters
{
    private long _segments, _holes, _duplicates, _gaps, _upstream, _upstreamResends, _stalls, _stallMs;

    public long Segments => Interlocked.Read(ref _segments);
    public long Holes => Interlocked.Read(ref _holes);
    public long Duplicates => Interlocked.Read(ref _duplicates);
    public long Gaps => Interlocked.Read(ref _gaps);
    public long Upstream => Interlocked.Read(ref _upstream);
    public long UpstreamResends => Interlocked.Read(ref _upstreamResends);
    public long Stalls => Interlocked.Read(ref _stalls);
    public long StallMs => Interlocked.Read(ref _stallMs);

    internal void AddSegment() => Interlocked.Increment(ref _segments);
    internal void AddHole() => Interlocked.Increment(ref _holes);
    internal void AddDuplicate() => Interlocked.Increment(ref _duplicates);
    internal void AddGap() => Interlocked.Increment(ref _gaps);
    internal void AddUpstream(bool resend)
    {
        Interlocked.Increment(ref _upstream);
        if (resend) Interlocked.Increment(ref _upstreamResends);
    }
    internal void AddStall(long ms)
    {
        Interlocked.Increment(ref _stalls);
        Interlocked.Add(ref _stallMs, ms);
    }
}
