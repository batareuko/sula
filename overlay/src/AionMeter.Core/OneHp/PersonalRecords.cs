using AionMeter.Core.Combat;
using AionMeter.Core.Storage;

namespace AionMeter.Core.OneHp;

/// <summary>1 HP: your best result on a boss, from the saved fight history (kills only, your character only).</summary>
public static class PersonalRecords
{
    public sealed record Comparison(HistoryEntry Best, double CurrentDps, double DeltaPct, bool IsNewRecord);

    /// <summary>The kill of <paramref name="bossCode"/> with your highest DPS; <paramref name="excludeFile"/> skips the fight being shown.</summary>
    public static HistoryEntry? Best(IEnumerable<HistoryEntry> history, int bossCode, string? selfName, string? excludeFile = null) =>
        bossCode == 0
            ? null
            : history
                .Where(e => e.BossCode == bossCode && e.Reason == EncounterEndReason.Kill && e.SelfDps > 0 &&
                            (string.IsNullOrEmpty(selfName) || string.Equals(e.SelfName, selfName, StringComparison.Ordinal)) &&
                            e.FileName != excludeFile)
                .MaxBy(e => e.SelfDps);

    /// <summary>Current DPS against the record; null when there is no record yet or you dealt no damage.</summary>
    public static Comparison? Compare(IEnumerable<HistoryEntry> history, EncounterSnapshot snap, string? excludeFile = null)
    {
        var self = snap.Combatants.FirstOrDefault(c => c.IsSelf);
        var bossCode = snap.Boss?.NpcCode ?? 0;
        if (self is null || self.Dps <= 0 || bossCode == 0) return null;
        var best = Best(history, bossCode, self.Name, excludeFile);
        if (best is null) return null;
        var delta = (self.Dps - best.SelfDps) / best.SelfDps * 100;
        return new Comparison(best, self.Dps, Math.Round(delta, 1), snap.Reason == EncounterEndReason.Kill && self.Dps > best.SelfDps);
    }
}
