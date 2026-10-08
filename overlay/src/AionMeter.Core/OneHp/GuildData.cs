using System.Text.Json;
using AionMeter.Core.Combat;

namespace AionMeter.Core.OneHp;

public sealed record GearInfo(int ItemLevel, long CombatPower);

/// <summary>1 HP: data exchanged with guide.sulaslova.com (pure functions, used by the app's OneHpCloud).</summary>
public static class GuildData
{
    /// <summary>
    /// aion-lookup answers { character, game } for a single match or { matches: [...] } for several:
    /// pick the one on the player's server (any, when the server is unknown and there is only one).
    /// </summary>
    public static GearInfo? ParseGear(string json, int serverId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var candidates = new List<JsonElement>();
        if (root.TryGetProperty("character", out var single) && single.ValueKind == JsonValueKind.Object) candidates.Add(single);
        if (root.TryGetProperty("matches", out var list) && list.ValueKind == JsonValueKind.Array) candidates.AddRange(list.EnumerateArray());
        static int Int(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.TryGetInt32(out var n) ? n : 0;
        static long Long(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.TryGetInt64(out var n) ? n : 0;
        var pick = serverId != 0
            ? candidates.Where(c => Int(c, "serverId") == serverId).Cast<JsonElement?>().FirstOrDefault()
            : candidates.Count == 1 ? candidates[0] : null;
        return pick is { } c2 && Int(c2, "itemLevel") > 0 ? new GearInfo(Int(c2, "itemLevel"), Long(c2, "combatPower")) : null;
    }

    /// <summary>The 1 HP payload of a finished fight: only the local player's own numbers, only for a killed boss.</summary>
    public static object? BuildRecord(EncounterSnapshot s)
    {
        if (s.Reason != EncounterEndReason.Kill || s.Boss is not { NpcCode: > 0 } boss) return null;
        var players = s.Combatants.Where(c => !c.IsUnknownSummons).ToList();
        var self = players.FirstOrDefault(c => c.IsSelf);
        if (self is null || self.Damage <= 0 || self.Name.StartsWith('#') || s.CombatMs < 5_000) return null;
        return new
        {
            character = self.Name,
            @class = self.Class.ToString(),
            serverId = self.ServerId,
            bossCode = boss.NpcCode,
            bossName = string.IsNullOrWhiteSpace(boss.Name) ? s.Title : boss.Name,
            zone = s.Zone,
            dps = Math.Round(self.Dps, 1),
            damage = self.Damage,
            durationMs = s.CombatMs,
            partySize = players.Count,
            place = players.IndexOf(self) + 1,
            foughtAt = s.StartedAt.ToUniversalTime().ToString("o"),
        };
    }
}
