using System.Net;
using AionMeter.Core.Capture;
using AionMeter.Core.Game;
using AionMeter.Core.OneHp;

namespace AionMeter.Tests;

/// <summary>1 HP: lag the stream-hole count does not see — our own packets sent again, and freezes of the server stream.</summary>
public class LagTests
{
    private static readonly IPAddress Server = IPAddress.Parse("193.202.112.99"), Me = IPAddress.Parse("192.168.1.5");
    private static readonly FlowKey Down = new(Server, 13328, Me, 50000);
    private static readonly FlowKey Up = new(Me, 50000, Server, 13328);
    private static readonly byte[] Heartbeat = [0x0E, 0x00, 0x36, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>A pipeline with the game stream locked; returns the next server sequence number.</summary>
    private static PacketPipeline Locked(out uint seq)
    {
        var p = new PacketPipeline(GameData.Empty, _ => { }) { LockThreshold = 3 };
        seq = 1000;
        for (var i = 0; i < 4; i++)
        {
            p.OnSegment(i * 50, Down, seq, false, false, Heartbeat);
            seq += (uint)Heartbeat.Length;
        }
        Assert.Equal(1, p.GameFlows);
        return p;
    }

    [Fact]
    public void Our_packets_sent_again_are_counted_as_upstream_resends()
    {
        var p = Locked(out _);
        byte[] skill = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        uint seq = 5000;
        p.OnSegment(300, Up, seq, false, false, skill);                 // a skill
        p.OnSegment(310, Up, seq + 10, false, false, skill);            // the next one
        p.OnSegment(520, Up, seq, false, false, skill);                 // the first sent again: lost on the way
        p.OnSegment(530, Up, seq + 20, false, false, skill);
        p.OnSegment(540, Up, seq + 30, false, false, []);               // a bare ACK: not counted
        Assert.Equal(4, p.Health.Upstream);
        Assert.Equal(1, p.Health.UpstreamResends);
        Assert.Equal(1, p.GameFlows); // our direction never becomes a "game stream" of its own
    }

    [Fact]
    public void A_pause_of_half_a_second_in_the_server_stream_is_a_freeze()
    {
        var p = Locked(out var seq);
        void Beat(long t)
        {
            p.OnSegment(t, Down, seq, false, false, Heartbeat);
            seq += (uint)Heartbeat.Length;
        }
        Beat(250);
        Beat(400);   // 150 ms: normal
        Beat(1_200); // 800 ms: a freeze
        Beat(1_300);
        Beat(20_000); // 18.7 s: a loading screen or a disconnect, not lag
        Assert.Equal(1, p.Health.Stalls);
        Assert.Equal(800, p.Health.StallMs);
    }

    [Fact]
    public void The_window_reports_resends_and_freezes_of_the_last_minute()
    {
        var w = new StreamLossWindow(60_000);
        w.Add(0, 0, 0, 0, 0, 0, 0);
        w.Add(30_000, 900, 0, 200, 4, 2, 1_500);
        Assert.Equal(2.0, w.Upstream().ResendPct);
        Assert.Equal((2, 1_500), w.Stalls());
        w.Add(100_000, 2_000, 0, 400, 4, 2, 1_500); // nothing new for a minute
        Assert.Equal(0, w.Upstream().Resends);
        Assert.Equal((0, 0), w.Stalls());
    }

    [Fact]
    public void Freezes_and_upstream_loss_lower_the_grade_even_with_no_stream_holes()
    {
        var noPing = new NetSummary(60, 60, 100, null, null, null, null, null);
        Assert.Equal(NetQuality.Good, NetGrade.Of(0, noPing, 0.1, 0));
        Assert.Equal(NetQuality.Fair, NetGrade.Of(0, noPing, 0.1, 1));
        Assert.Equal(NetQuality.Bad, NetGrade.Of(0, noPing, 0, 3));
        Assert.Equal(NetQuality.Bad, NetGrade.Of(0, noPing, 2.4, 0));
    }
}
