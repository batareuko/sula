namespace AionMeter.Core.Capture;

/// <summary>
/// 1 HP: health of the game's TCP stream as the capture sees it, shared by all reassemblers of the game flows.
/// A hole is a segment that arrived before an earlier one: the earlier segment was lost on the way and the server
/// has to send it again — the packet loss the player feels as lag. Updated on the capture thread, read by the UI.
/// </summary>
public sealed class StreamCounters
{
    private long _segments, _holes, _duplicates, _gaps;

    public long Segments => Interlocked.Read(ref _segments);
    public long Holes => Interlocked.Read(ref _holes);
    public long Duplicates => Interlocked.Read(ref _duplicates);
    public long Gaps => Interlocked.Read(ref _gaps);

    internal void AddSegment() => Interlocked.Increment(ref _segments);
    internal void AddHole() => Interlocked.Increment(ref _holes);
    internal void AddDuplicate() => Interlocked.Increment(ref _duplicates);
    internal void AddGap() => Interlocked.Increment(ref _gaps);
}
