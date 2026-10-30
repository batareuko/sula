using System.Globalization;
using System.Text;
using AionMeter.Core.Combat;

namespace AionMeter.Core.Storage;

/// <summary>1 HP: a saved fight for a spreadsheet (CSV, one row per player) and its comparison with an earlier try.</summary>
public static class FightExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Semicolon-separated (Excel in most European locales opens it as columns), UTF-8 with a BOM.</summary>
    public static string Csv(EncounterSnapshot s)
    {
        var seconds = Math.Max(1, s.CombatMs) / 1000.0;
        var sb = new StringBuilder("﻿");
        sb.AppendLine("Fight;Started;Seconds;Result;Place;Player;Class;Server;Damage;DPS;Share %;Hits;APS;Crit %;Max hit;Healing;HPS;Taken;Deaths");
        var place = 0;
        foreach (var c in s.Combatants.OrderByDescending(c => c.Damage))
        {
            if (!c.IsUnknownSummons) place++;
            sb.AppendJoin(';',
                Cell(s.Title), s.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", Inv), seconds.ToString("0.0", Inv), s.Reason,
                c.IsUnknownSummons ? "" : place.ToString(Inv), Cell(c.Name), c.Class, c.ServerId,
                c.Damage, c.Dps.ToString("0", Inv), (c.Share * 100).ToString("0.0", Inv), c.Hits, (c.Hits / seconds).ToString("0.00", Inv),
                (c.CritRate * 100).ToString("0.0", Inv), c.MaxHit, c.Healing, (c.Healing / seconds).ToString("0", Inv), c.DamageTaken, c.Deaths);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Cell(string text) =>
        text.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

    /// <summary>A fight against an earlier one on the same boss (or monster): change in party DPS, in length, in your DPS.</summary>
    public sealed record Comparison(HistoryEntry Before, double PartyDpsChange, long CombatMsChange, double? SelfDpsChange);

    /// <summary>The latest earlier fight on the same target (killed, when this one was a kill); null when there is none.</summary>
    public static Comparison? CompareWithPrevious(HistoryEntry fight, IEnumerable<HistoryEntry> history)
    {
        var code = fight.BossCode != 0 ? fight.BossCode : fight.TargetCode;
        if (code == 0) return null;
        var before = history
            .Where(e => e.StartedAt < fight.StartedAt && (e.BossCode != 0 ? e.BossCode : e.TargetCode) == code)
            .Where(e => fight.Reason != EncounterEndReason.Kill || e.Reason == EncounterEndReason.Kill)
            .MaxBy(e => e.StartedAt);
        if (before is null || before.PartyDps <= 0) return null;
        double? self = before.SelfDps > 0 && fight.SelfDps > 0 ? fight.SelfDps / before.SelfDps - 1 : null;
        return new Comparison(before, fight.PartyDps / before.PartyDps - 1, fight.CombatMs - before.CombatMs, self);
    }
}
