using AionMeter.Core.Events;

namespace AionMeter.Core.Combat;

/// <summary>One fight of a dungeon run.</summary>
public sealed record RunFight(string Title, int BossCode, EncounterEndReason Reason, long StartMs, long CombatMs, long TotalDamage);

/// <summary>A player over the whole run: damage, DPS over the time in fights, deaths.</summary>
public sealed record RunMember(string Name, GameClass Class, bool IsSelf, long Damage, double Dps, long Healing, int Deaths);

/// <summary>
/// 1 HP: a dungeon run — from entering the instance to leaving it: its fights, the bosses killed, every death, each
/// member's numbers over the run. <see cref="ClearMs"/> runs from entering to the last boss kill when it was cleared.
/// </summary>
public sealed record DungeonRun(
    Guid Id, int MapId, string Zone, long EnteredMs, long LeftMs, long FirstHitMs, long LastBossKillMs,
    int BossFights, int BossesKilled, int Deaths, IReadOnlyList<RunFight> Fights, IReadOnlyList<RunMember> Members)
{
    /// <summary>A boss died and the last boss fight was a kill.</summary>
    public bool Cleared => BossesKilled > 0 && Fights.LastOrDefault(f => f.BossCode != 0)?.Reason == EncounterEndReason.Kill;

    public long ClearMs => Math.Max(0, (Cleared ? LastBossKillMs : LeftMs) - EnteredMs);
}

/// <summary>Builds the run; the tracker calls it under its lock.</summary>
internal sealed class DungeonRunBuilder(int mapId, string zone, long enteredMs)
{
    private sealed class Member
    {
        public string Name = "";
        public GameClass Class;
        public bool IsSelf;
        public long Damage, Healing, CombatMs;
        public int Deaths;
    }

    private readonly Guid _id = Guid.NewGuid();
    private readonly List<RunFight> _fights = new();
    private readonly Dictionary<string, Member> _members = new(StringComparer.OrdinalIgnoreCase);
    private long _firstHit, _lastBossKill;
    private int _deaths;

    public int MapId => mapId;

    private Member Get(string name)
    {
        if (!_members.TryGetValue(name, out var m)) _members[name] = m = new Member { Name = name };
        return m;
    }

    public void AddFight(Encounter enc, string title)
    {
        if (enc.TotalDamage <= 0) return;
        if (_firstHit == 0 || enc.StartMs < _firstHit) _firstHit = enc.StartMs;
        _fights.Add(new RunFight(title, enc.BossCode, enc.Reason, enc.StartMs, enc.CombatMs, enc.TotalDamage));
        if (enc.BossId is not null && enc.Reason == EncounterEndReason.Kill) _lastBossKill = Math.Max(_lastBossKill, enc.EndMs);
        foreach (var c in enc.Combatants.Values)
        {
            if (c.ActorId == Combatant.UnknownSummonsId || c.Name.StartsWith('#')) continue;
            var m = Get(c.Name);
            if (c.Class != GameClass.Unknown) m.Class = c.Class;
            m.IsSelf |= c.IsSelf;
            m.Damage += c.Total.Damage;
            m.Healing += c.Healing;
            m.CombatMs += enc.CombatMs;
        }
    }

    public void AddDeath(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Get(name).Deaths++;
        _deaths++;
    }

    public DungeonRun Build(long nowMs, bool ended)
    {
        var bossFights = _fights.Where(f => f.BossCode != 0).ToList();
        var members = _members.Values
            .Where(m => m.Damage > 0 || m.Healing > 0 || m.Deaths > 0)
            .Select(m => new RunMember(m.Name, m.Class, m.IsSelf, m.Damage, m.CombatMs > 0 ? m.Damage / (m.CombatMs / 1000.0) : 0, m.Healing, m.Deaths))
            .OrderByDescending(m => m.Damage).ToList();
        return new DungeonRun(_id, mapId, zone, enteredMs, ended ? nowMs : 0, _firstHit, _lastBossKill,
            bossFights.Count, bossFights.Count(f => f.Reason == EncounterEndReason.Kill), _deaths, _fights.ToList(), members);
    }
}
