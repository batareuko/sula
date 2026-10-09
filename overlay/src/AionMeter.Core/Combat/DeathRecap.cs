using AionMeter.Core.Events;

namespace AionMeter.Core.Combat;

/// <summary>One skill of an attacker in a death recap.</summary>
public sealed record RecapSkill(int SkillCode, string Name, long Damage, int Hits, long MaxHit);

/// <summary>Who hit you in the seconds before you died, with what.</summary>
/// <param name="IsPlayer">A player (with pets and skill effects folded in), not a monster.</param>
public sealed record RecapAttacker(
    uint ActorId, string Name, GameClass Class, int ServerId, bool IsPlayer, long Damage, int Hits, int Crits, long MaxHit,
    IReadOnlyList<RecapSkill> Skills);

/// <summary>
/// 1 HP: your death — who killed you (the server's kill notice, else whoever dealt the most), and everyone who hit you
/// in the <see cref="DeathRecaps.WindowMs"/> before it.
/// </summary>
/// <param name="FirstHitMs">The first hit of the window: <c>DeathMs - FirstHitMs</c> is how long it took.</param>
public sealed record DeathRecap(
    Guid Id, long DeathMs, long FirstHitMs, string VictimName, uint? KillerId, string KillerName, GameClass KillerClass,
    int KillerServer, bool ByPlayer, long TotalDamage, IReadOnlyList<RecapAttacker> Attackers)
{
    public long DurationMs => Math.Max(0, DeathMs - FirstHitMs);
}

/// <summary>Keeps the hits you take and turns them into a <see cref="DeathRecap"/> when you die. Not thread-safe:
/// the tracker calls it under its lock.</summary>
internal sealed class DeathRecaps
{
    public const long WindowMs = 15_000;

    private readonly record struct Hit(long TimeMs, uint SourceId, int SkillCode, long Damage, HitFlags Flags, int Scalar);

    private readonly List<Hit> _hits = new();
    private DeathRecap? _last;

    public void OnHit(long timeMs, uint sourceId, int skillCode, long damage, HitFlags flags, int scalar)
    {
        _hits.Add(new Hit(timeMs, sourceId, skillCode, damage, flags, scalar));
        var cutoff = timeMs - WindowMs * 2;
        var stale = 0;
        while (stale < _hits.Count && _hits[stale].TimeMs < cutoff) stale++;
        if (stale > 0) _hits.RemoveRange(0, stale);
    }

    /// <summary>You died (death record or kill notice): the recap, or the same one updated with the killer's name.</summary>
    public DeathRecap? OnDeath(long timeMs, string victim, (uint Id, string Name, int Server)? killer, Func<uint, uint> owner,
        Func<uint, (string Name, GameClass Class, int Server, bool IsPlayer)> who, Func<int, string> skillName)
    {
        // The kill notice and the death record of the same death arrive together: one recap, the notice names the killer.
        if (_last is { } last && Math.Abs(timeMs - last.DeathMs) < 3_000)
        {
            if (killer is not { } k || (last.KillerId == k.Id && last.ByPlayer && last.KillerServer == k.Server && last.KillerName == k.Name))
                return null;
            var named = who(k.Id);
            _last = last with
            {
                KillerId = k.Id, KillerName = k.Name, KillerServer = k.Server, ByPlayer = true,
                KillerClass = last.Attackers.FirstOrDefault(a => a.ActorId == k.Id)?.Class ?? named.Class,
            };
            return _last;
        }

        var window = _hits.Where(h => h.TimeMs <= timeMs + 500 && h.TimeMs >= timeMs - WindowMs).ToList();
        _hits.Clear();
        // Some skill records carry the attacker's power scalar where the damage goes (no scalar of their own; the
        // victim's HP does not move): not damage.
        var scalars = window.Where(h => h.Scalar != 0).Select(h => (long)h.Scalar).ToHashSet();
        window.RemoveAll(h => h.Scalar == 0 && (h.Flags & HitFlags.Dot) == 0 && scalars.Contains(h.Damage));

        // Pets and skill effects: their owner when known, else the one attacker who carries the same power scalar.
        var byScalar = new Dictionary<int, uint>();
        foreach (var h in window)
        {
            var o = owner(h.SourceId);
            if (h.Scalar == 0 || !who(o).IsPlayer) continue;
            byScalar[h.Scalar] = byScalar.TryGetValue(h.Scalar, out var seen) && seen != o ? 0 : o;
        }
        uint Attacker(Hit h)
        {
            var o = owner(h.SourceId);
            if (who(o).IsPlayer) return o;
            return h.Scalar != 0 && byScalar.TryGetValue(h.Scalar, out var p) && p != 0 ? p : o;
        }

        var attackers = window
            .GroupBy(Attacker)
            .Select(g =>
            {
                var (name, cls, server, isPlayer) = who(g.Key);
                if (cls == GameClass.Unknown) // a name from the kill notice comes without a class: the skills tell
                    cls = g.Select(h => Game.GameData.ClassFromSkill(h.SkillCode)).FirstOrDefault(c => c != GameClass.Unknown);
                var skills = g.GroupBy(h => h.SkillCode)
                    .Select(s => new RecapSkill(s.Key, skillName(s.Key), s.Sum(h => h.Damage), s.Count(), s.Max(h => h.Damage)))
                    .OrderByDescending(s => s.Damage).ToList();
                return new RecapAttacker(g.Key, name, cls, server, isPlayer, g.Sum(h => h.Damage), g.Count(),
                    g.Count(h => (h.Flags & HitFlags.Critical) != 0), g.Max(h => h.Damage), skills);
            })
            .OrderByDescending(a => a.Damage).ToList();

        RecapAttacker? top = killer is { } kid ? attackers.FirstOrDefault(a => a.ActorId == kid.Id) : null;
        top ??= attackers.FirstOrDefault(a => a.IsPlayer) ?? attackers.FirstOrDefault();
        var killerInfo = killer is { } kk ? who(kk.Id) : default;
        _last = new DeathRecap(
            Guid.NewGuid(), timeMs, window.Count > 0 ? window.Min(h => h.TimeMs) : timeMs, victim,
            killer?.Id ?? top?.ActorId, killer?.Name ?? top?.Name ?? "?",
            top?.Class ?? killerInfo.Class, killer?.Server ?? top?.ServerId ?? 0,
            killer is not null || top?.IsPlayer == true, window.Sum(h => h.Damage), attackers);
        return _last;
    }
}
