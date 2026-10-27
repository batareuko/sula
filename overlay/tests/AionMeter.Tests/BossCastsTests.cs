using AionMeter.Core.Events;
using AionMeter.Core.OneHp;

namespace AionMeter.Tests;

public class BossCastsTests
{
    private static BossCasts Casts() => new(code => code is >= 1_000_000 and < 2_000_000 && code != 1000010);

    [Fact]
    public void Learns_the_gap_and_says_what_comes_next()
    {
        var c = Casts();
        c.Observe(new NpcSeenEvent(0, 7, 2300471));
        foreach (var at in new long[] { 1_000, 11_000, 21_000 }) c.Observe(new CastEvent(at, 7, 1, 1000720));
        c.Observe(new CastEvent(22_000, 7, 1, 1000010)); // a plain attack is not a mechanic
        c.Observe(new CastEvent(22_000, 1, 7, 1000720)); // a player is not a monster

        Assert.Equal([1000720], c.Recent(7, 20_000).Select(x => x.SkillCode));
        var next = Assert.Single(c.Next(7, 25_000));
        Assert.Equal(31_000, next.DueMs);
        Assert.Empty(c.Next(7, 37_000)); // overdue by more than half a gap
    }

    [Fact]
    public void Keeps_the_rhythm_for_the_next_fight()
    {
        var c = Casts();
        c.Observe(new NpcSeenEvent(0, 7, 2300471));
        foreach (var at in new long[] { 0, 8_000, 16_000 }) c.Observe(new CastEvent(at, 7, 1, 1000650));
        var saved = c.Export();

        var next = Casts();
        next.Import(saved);
        next.Observe(new NpcSeenEvent(100_000, 9, 2300471)); // the same boss, another fight
        next.Observe(new CastEvent(101_000, 9, 1, 1000650));
        Assert.Equal(109_000, Assert.Single(next.Next(9, 102_000)).DueMs);

        next.Observe(new DeathEvent(110_000, 9));
        Assert.Empty(next.Recent(9, 0));
    }
}
