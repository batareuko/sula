using AionMeter.Core.Events;

namespace AionMeter.Core.Combat;

/// <summary>
/// 1 HP: skill analysis of one player in one fight, worked out from the hits the meter kept (live and in history).
/// The game sends hits, not casts: a use is a run of direct hits of the same skill with less than
/// <see cref="UseGapMs"/> between them, so a multi-hit skill counts once — an estimate, said so on the page.
/// </summary>
public static class SkillAnalysis
{
    public const int UseGapMs = 1_200;

    /// <param name="Uses">Estimated uses (direct hits grouped); 0 for damage over time only.</param>
    /// <param name="BestUse">The biggest single use (its hits summed).</param>
    /// <param name="PerSecond">Damage of this skill per second of the fight (the row's timeline).</param>
    public sealed record SkillUse(int SkillCode, long Damage, double Share, int Uses, long PerUse, long BestUse, double? CastsPerMinute,
        IReadOnlyList<long> PerSecond);

    /// <param name="ActiveMs">Seconds in which the player dealt damage.</param>
    /// <param name="ActiveDps">Damage per active second.</param>
    /// <param name="Peak5">Best 5-second DPS; <paramref name="Peak5At"/> is when that window began (s from the start).</param>
    public sealed record Result(long ActiveMs, double ActiveDps, double Peak5, int Peak5At, double Peak10, int Peak10At,
        IReadOnlyList<SkillUse> Skills);

    public static Result Of(CombatantDetail d)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(Math.Max(d.CombatMs, d.PerSecond.Count * 1000L) / 1000.0));
        var active = d.PerSecond.Count(v => v > 0);
        var (peak5, at5) = Peak(d.PerSecond, 5);
        var (peak10, at10) = Peak(d.PerSecond, 10);

        var total = Math.Max(1, d.Casts.Sum(c => c.Damage));
        var minutes = d.CombatMs / 60_000.0;
        var skills = d.Casts.GroupBy(c => c.SkillCode)
            .Select(g =>
            {
                var perSecond = new long[seconds];
                var uses = new List<long>();
                var lastDirect = int.MinValue / 2;
                foreach (var hit in g.OrderBy(c => c.OffsetMs))
                {
                    var s = Math.Clamp(hit.OffsetMs / 1000, 0, seconds - 1);
                    perSecond[s] += hit.Damage;
                    if ((hit.Flags & HitFlags.Dot) != 0) continue;
                    if (hit.OffsetMs - lastDirect >= UseGapMs || uses.Count == 0) uses.Add(0);
                    uses[^1] += hit.Damage;
                    lastDirect = hit.OffsetMs;
                }
                var damage = g.Sum(c => c.Damage);
                return new SkillUse(g.Key, damage, (double)damage / total, uses.Count,
                    uses.Count > 0 ? damage / uses.Count : 0, uses.Count > 0 ? uses.Max() : 0,
                    uses.Count > 0 && minutes >= 0.25 ? uses.Count / minutes : null, perSecond);
            })
            .OrderByDescending(s => s.Damage)
            .ToList();
        return new Result(active * 1000L, active > 0 ? d.Damage / (double)active : 0, peak5, at5, peak10, at10, skills);
    }

    /// <summary>Best average over <paramref name="window"/> seconds, and the second it starts at.</summary>
    private static (double Dps, int At) Peak(IReadOnlyList<long> perSecond, int window)
    {
        if (perSecond.Count == 0) return (0, 0);
        var w = Math.Min(window, perSecond.Count);
        long sum = 0;
        for (var i = 0; i < w; i++) sum += perSecond[i];
        long best = sum;
        var at = 0;
        for (var i = w; i < perSecond.Count; i++)
        {
            sum += perSecond[i] - perSecond[i - w];
            if (sum > best) { best = sum; at = i - w + 1; }
        }
        return ((double)best / w, at);
    }
}
