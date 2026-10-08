using AionMeter.Core.Capture;
using AionMeter.Core.OneHp;

namespace AionMeter.Tests;

/// <summary>1 HP additions: Global schedule, ping statistics, game-stream loss counters.</summary>
public class OneHpTests
{
    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    [Fact]
    public void RiftOpensEveryThreeHoursForTenMinutes()
    {
        var open = GlobalSchedule.Rift(Utc(2026, 10, 7, 9, 5));
        Assert.True(open.IsOpen);
        Assert.Equal(Utc(2026, 10, 7, 9, 10), open.Closes);

        var closed = GlobalSchedule.Rift(Utc(2026, 10, 7, 9, 10));
        Assert.False(closed.IsOpen);
        Assert.Equal(Utc(2026, 10, 7, 12, 0), closed.Opens);

        Assert.Equal(Utc(2026, 10, 8, 0, 0), GlobalSchedule.Rift(Utc(2026, 10, 7, 22, 30)).Opens);
    }

    [Fact]
    public void RiftUsesUtcWhateverTheOffset()
    {
        var kyiv = new DateTimeOffset(2026, 10, 7, 12, 5, 0, TimeSpan.FromHours(3)); // 09:05 UTC
        Assert.True(GlobalSchedule.Rift(kyiv).IsOpen);
    }

    [Fact]
    public void DailyResetAtSevenUtc()
    {
        Assert.Equal(Utc(2026, 10, 7, 7, 0), GlobalSchedule.NextDailyReset(Utc(2026, 10, 7, 6, 59)));
        Assert.Equal(Utc(2026, 10, 8, 7, 0), GlobalSchedule.NextDailyReset(Utc(2026, 10, 7, 7, 0)));
    }

    [Fact]
    public void WeeklyResetOnWednesday()
    {
        // 2026-10-07 is a Wednesday
        Assert.Equal(DayOfWeek.Wednesday, Utc(2026, 10, 7, 0, 0).DayOfWeek);
        Assert.Equal(Utc(2026, 10, 7, 7, 0), GlobalSchedule.NextWeeklyReset(Utc(2026, 10, 5, 12, 0)));
        Assert.Equal(Utc(2026, 10, 14, 7, 0), GlobalSchedule.NextWeeklyReset(Utc(2026, 10, 7, 7, 0)));
        Assert.Equal(Utc(2026, 10, 14, 7, 0), GlobalSchedule.NextWeeklyReset(Utc(2026, 10, 8, 1, 0)));
    }

    [Fact]
    public void NetStatsWindowLossAndJitter()
    {
        var s = new NetStats(10_000);
        s.Add(0, 100); // falls out of the window below
        s.Add(5_000, 10);
        s.Add(6_000, null);
        s.Add(7_000, 14);
        s.Add(12_000, 12);
        var sum = s.Summary();
        Assert.Equal(4, sum.Samples);
        Assert.Equal(1, sum.Lost);
        Assert.Equal(25, sum.LossPct);
        Assert.Equal(12, sum.AvgMs);
        Assert.Equal(3, sum.JitterMs); // |14-10| and |12-14| → 3
        Assert.Equal(12, sum.LastMs);
    }

    [Fact]
    public void ReassemblerCountsHolesForLostSegments()
    {
        var counters = new StreamCounters();
        var got = new List<byte>();
        var r = new TcpReassembler(d => got.AddRange(d.ToArray()), () => { }, counters);
        r.Start(1000);
        r.Push(1000, new byte[] { 1, 2 }, 0);
        r.Push(1004, new byte[] { 5, 6 }, 10); // 1002..1003 lost: a hole
        r.Push(1006, new byte[] { 7 }, 11);    // still the same hole, not a new one
        r.Push(1002, new byte[] { 3, 4 }, 50); // the re-sent segment fills it
        r.Push(1002, new byte[] { 3, 4 }, 60); // a duplicate
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, got.ToArray());
        Assert.Equal(5, counters.Segments);
        Assert.Equal(1, counters.Holes);
        Assert.Equal(1, counters.Duplicates);
        Assert.Equal(0, counters.Gaps);
    }

    [Fact]
    public void StreamLossWindowNeedsTrafficAndSurvivesRestart()
    {
        var w = new StreamLossWindow(60_000);
        w.Add(0, 0, 0);
        w.Add(1_000, 20, 1);
        Assert.Null(w.Current().LossPct); // 20 segments: too little to judge
        w.Add(2_000, 400, 6);
        Assert.Equal(1.5, w.Current().LossPct);
        w.Add(3_000, 10, 0); // capture restarted, counters from zero
        Assert.Null(w.Current().LossPct);
    }

    [Fact]
    public void GradeFollowsStreamLossFirst()
    {
        var quiet = new NetSummary(0, 0, 0, null, null, null, null, null);
        var noPing = new NetSummary(60, 60, 100, null, null, null, null, null); // server ignores ICMP
        Assert.Equal(NetQuality.Unknown, NetGrade.Of(null, quiet));
        Assert.Equal(NetQuality.Good, NetGrade.Of(0.1, noPing));
        Assert.Equal(NetQuality.Fair, NetGrade.Of(0.8, noPing));
        Assert.Equal(NetQuality.Bad, NetGrade.Of(2.5, noPing));
        var shaky = new NetSummary(60, 0, 0, 280, 250, 400, 45, 300);
        Assert.Equal(NetQuality.Fair, NetGrade.Of(null, shaky));
    }
}
