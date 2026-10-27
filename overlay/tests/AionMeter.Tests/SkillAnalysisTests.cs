using AionMeter.Core.Combat;
using AionMeter.Core.Events;

namespace AionMeter.Tests;

public class SkillAnalysisTests
{
    private static CombatantDetail Detail(IReadOnlyList<CastRecord> casts, IReadOnlyList<long> perSecond, long combatMs) =>
        new(1, "Sylvaen", GameClass.Elementalist, true, casts.Sum(c => c.Damage), 0, 1, combatMs, 0, 0,
            new HitQuality(casts.Count, 0, null, null, null, null, null, null, 0, null), [], perSecond, casts);

    [Fact]
    public void Groups_hits_into_uses_and_finds_the_peaks()
    {
        CastRecord[] casts =
        [
            new(0, 16010000, 100, HitFlags.None), new(300, 16010000, 100, HitFlags.None), // one use, two hits
            new(5_000, 16010000, 300, HitFlags.None),                                   // a second use
            new(5_500, 16020000, 50, HitFlags.Dot), new(6_500, 16020000, 50, HitFlags.Dot), // damage over time only
        ];
        long[] perSecond = [200, 0, 0, 0, 0, 350, 50, 0, 0, 0];
        var a = SkillAnalysis.Of(Detail(casts, perSecond, 10_000));

        var main = a.Skills[0];
        Assert.Equal(16010000, main.SkillCode);
        Assert.Equal(2, main.Uses);
        Assert.Equal(250, main.PerUse);
        Assert.Equal(300, main.BestUse);
        Assert.Equal(0, a.Skills[1].Uses);
        Assert.Equal(3_000, a.ActiveMs);
        Assert.Equal(400 / 5.0, a.Peak5);
        Assert.Equal(2, a.Peak5At);
    }
}
