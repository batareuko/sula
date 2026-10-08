namespace AionMeter.Core.OneHp;

public readonly record struct NetSummary(int Samples, int Lost, double LossPct, double? AvgMs, int? MinMs, int? MaxMs, double? JitterMs, int? LastMs)
{
    public bool AnyReply => Samples > Lost;
}

/// <summary>1 HP: ping results over a sliding window (default one minute): loss, average, jitter.</summary>
public sealed class NetStats(long windowMs = 60_000)
{
    private readonly Queue<(long T, int? Rtt)> _samples = new();
    private readonly object _gate = new();

    public void Add(long timeMs, int? rttMs)
    {
        lock (_gate)
        {
            _samples.Enqueue((timeMs, rttMs));
            while (_samples.Count > 0 && timeMs - _samples.Peek().T > windowMs) _samples.Dequeue();
        }
    }

    public void Clear()
    {
        lock (_gate) _samples.Clear();
    }

    public NetSummary Summary()
    {
        (long T, int? Rtt)[] all;
        lock (_gate) all = _samples.ToArray();
        if (all.Length == 0) return new NetSummary(0, 0, 0, null, null, null, null, null);
        var ok = all.Where(s => s.Rtt is not null).Select(s => s.Rtt!.Value).ToArray();
        var lost = all.Length - ok.Length;
        double? jitter = null;
        if (ok.Length > 1)
        {
            double d = 0;
            for (var i = 1; i < ok.Length; i++) d += Math.Abs(ok[i] - ok[i - 1]);
            jitter = Math.Round(d / (ok.Length - 1), 1);
        }
        return new NetSummary(all.Length, lost, Math.Round(100.0 * lost / all.Length, 1),
            ok.Length > 0 ? Math.Round(ok.Average(), 1) : null, ok.Length > 0 ? ok.Min() : null, ok.Length > 0 ? ok.Max() : null,
            jitter, all[^1].Rtt);
    }
}

/// <summary>1 HP: share of lost (re-sent) segments of the game stream over a sliding window, from <see cref="Capture.StreamCounters"/>.</summary>
public sealed class StreamLossWindow(long windowMs = 60_000)
{
    private readonly Queue<(long T, long Segments, long Holes)> _points = new();

    public void Add(long timeMs, long segments, long holes)
    {
        // A capture restart starts the counters over: forget the old points.
        if (_points.Count > 0 && segments < _points.Last().Segments) _points.Clear();
        _points.Enqueue((timeMs, segments, holes));
        while (_points.Count > 1 && timeMs - _points.Peek().T > windowMs) _points.Dequeue();
    }

    /// <summary>Segments and holes inside the window; percent is null until there is enough traffic to judge.</summary>
    public (long Segments, long Holes, double? LossPct) Current(int minSegments = 50)
    {
        if (_points.Count < 2) return (0, 0, null);
        var a = _points.Peek();
        var b = _points.Last();
        var seg = b.Segments - a.Segments;
        var holes = b.Holes - a.Holes;
        return (seg, holes, seg >= minSegments ? Math.Round(100.0 * holes / seg, 2) : null);
    }
}

public enum NetQuality { Unknown, Good, Fair, Bad }

public static class NetGrade
{
    /// <summary>
    /// Bad: ≥ 2 % of the game stream lost (or of pings to the server); fair: ≥ 0.5 % or jitter ≥ 30 ms; otherwise good.
    /// Unknown until there is either game traffic or ping replies from the server.
    /// </summary>
    public static NetQuality Of(double? streamLossPct, NetSummary server)
    {
        var loss = streamLossPct ?? (server.AnyReply && server.Samples >= 10 ? server.LossPct : (double?)null);
        if (loss is null && !server.AnyReply) return NetQuality.Unknown;
        var l = loss ?? 0;
        if (l >= 2) return NetQuality.Bad;
        if (l >= 0.5 || server.JitterMs >= 30) return NetQuality.Fair;
        return NetQuality.Good;
    }
}
