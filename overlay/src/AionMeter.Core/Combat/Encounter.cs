using AionMeter.Core.Events;

namespace AionMeter.Core.Combat;

public enum EncounterEndReason
{
    None,
    Kill,
    Wipe,
    Idle,
    Manual,
    ZoneChange,
}

public sealed class HitStats
{
    public int Hits;
    public int Crits;
    public int Back;
    public int Front;
    public int Perfect;
    public int Heavy;
    public int Multi;
    public int DotTicks;
    public long Damage;
    public long DotDamage;
    public long Min = long.MaxValue;
    public long Max;

    public void Add(long damage, HitFlags flags)
    {
        Damage += damage;
        if ((flags & HitFlags.Dot) != 0)
        {
            // DoT ticks never crit / land from behind, so keep them out of the hit-quality denominators.
            DotTicks++;
            DotDamage += damage;
            return;
        }
        Hits++;
        if (damage < Min) Min = damage;
        if (damage > Max) Max = damage;
        if ((flags & HitFlags.Critical) != 0) Crits++;
        if ((flags & HitFlags.Back) != 0) Back++;
        if ((flags & HitFlags.Front) != 0) Front++;
        if ((flags & HitFlags.Perfect) != 0) Perfect++;
        if ((flags & HitFlags.Heavy) != 0) Heavy++;
        if ((flags & HitFlags.Multi) != 0) Multi++;
    }
}

public sealed class SkillStats
{
    public required int SkillCode { get; init; }
    public HitStats Stats { get; } = new();
}

/// <summary>One cast / hit in order, for the rotation view.</summary>
public readonly record struct CastRecord(int OffsetMs, int SkillCode, long Damage, HitFlags Flags);

public sealed class Combatant
{
    public const int MaxCasts = 20_000;

    /// <summary>
    /// Pseudo-row collecting pets and skill effects whose owner could not be found. Never a player: it is listed last
    /// and left out of player counts and places.
    /// </summary>
    public const uint UnknownSummonsId = 0xFFFF_FFF0;

    public required uint ActorId { get; init; }
    public string Name { get; set; } = "";
    public GameClass Class { get; set; }
    public bool IsSelf { get; set; }
    public int ServerId { get; set; }

    public HitStats Total { get; } = new();
    public Dictionary<int, SkillStats> Skills { get; } = new();
    /// <summary>Damage per whole second since encounter start.</summary>
    public List<long> PerSecond { get; } = new();
    public List<CastRecord> Casts { get; } = new();
    public int MaxHitSkill { get; set; }

    public long DamageTaken { get; set; }
    public int HitsTaken { get; set; }
    public long Healing { get; set; }
    /// <summary>1 HP: times this player died during the fight.</summary>
    public int Deaths { get; set; }

    /// <summary>1 HP: healing this player received, by healer (actor id → amount, heals).</summary>
    public Dictionary<uint, (long Amount, int Hits)> HealedBy { get; } = new();

    /// <summary>1 HP: damage this player took, by attacker (actor id → amount, hits).</summary>
    public Dictionary<uint, (long Amount, int Hits)> TakenFrom { get; } = new();

    public long FirstHitMs { get; set; }
    public long LastHitMs { get; set; }

    /// <summary>Power scalars seen on this actor's hits; summons share their owner's.</summary>
    public HashSet<int> Scalars { get; } = new();

    /// <summary>Folds a summon that was only identified after it already dealt damage into its owner.</summary>
    public void MergeFrom(Combatant other)
    {
        foreach (var cast in other.Casts)
        {
            Total.Add(cast.Damage, cast.Flags);
            if (!Skills.TryGetValue(cast.SkillCode, out var s))
            {
                s = new SkillStats { SkillCode = cast.SkillCode };
                Skills[cast.SkillCode] = s;
            }
            s.Stats.Add(cast.Damage, cast.Flags);
            if (Casts.Count < MaxCasts) Casts.Add(cast);
        }
        Casts.Sort((a, b) => a.OffsetMs.CompareTo(b.OffsetMs));
        for (var i = 0; i < other.PerSecond.Count; i++)
        {
            while (PerSecond.Count <= i) PerSecond.Add(0);
            PerSecond[i] += other.PerSecond[i];
        }
        if (other.Total.Max > Total.Max) MaxHitSkill = other.MaxHitSkill;
        DamageTaken += other.DamageTaken;
        HitsTaken += other.HitsTaken;
        Healing += other.Healing;
        Deaths += other.Deaths;
        foreach (var (id, v) in other.HealedBy) HealedBy[id] = Add(HealedBy.GetValueOrDefault(id), v.Amount, v.Hits);
        foreach (var (id, v) in other.TakenFrom) TakenFrom[id] = Add(TakenFrom.GetValueOrDefault(id), v.Amount, v.Hits);
        if (FirstHitMs == 0 || (other.FirstHitMs != 0 && other.FirstHitMs < FirstHitMs)) FirstHitMs = other.FirstHitMs;
        LastHitMs = Math.Max(LastHitMs, other.LastHitMs);
    }

    public static (long Amount, int Hits) Add((long Amount, int Hits) total, long amount, int hits = 1) =>
        (total.Amount + amount, total.Hits + hits);

    public void AddDamage(int offsetMs, int skillCode, long damage, HitFlags flags)
    {
        var prevMax = Total.Max;
        Total.Add(damage, flags);
        if (Total.Max > prevMax) MaxHitSkill = skillCode;

        if (!Skills.TryGetValue(skillCode, out var s))
        {
            s = new SkillStats { SkillCode = skillCode };
            Skills[skillCode] = s;
        }
        s.Stats.Add(damage, flags);

        var sec = Math.Max(0, offsetMs / 1000);
        while (PerSecond.Count <= sec) PerSecond.Add(0);
        PerSecond[sec] += damage;

        if (Casts.Count < MaxCasts) Casts.Add(new CastRecord(offsetMs, skillCode, damage, flags));
    }
}

public sealed class Encounter
{
    public Guid Id { get; } = Guid.NewGuid();
    public required long StartMs { get; init; }
    public long LastDamageMs { get; set; }
    public long EndMs { get; set; }
    public bool IsActive => Reason == EncounterEndReason.None;
    public EncounterEndReason Reason { get; set; }
    public string? Zone { get; set; }
    /// <summary>Fought in an instance (dungeon, raid): everyone there is in the party.</summary>
    public bool InDungeon { get; set; }

    public uint? BossId { get; set; }
    public int BossCode { get; set; }
    public string? BossName { get; set; }
    public long BossMaxHp { get; set; }
    /// <summary>See <see cref="Game.NpcInfo.MaxHpKnown"/>.</summary>
    public bool BossMaxHpKnown { get; set; }
    public long BossHp { get; set; } = -1;
    public long BossLowestHp { get; set; } = long.MaxValue;

    /// <summary>1 HP: the first hit on the boss — its pull, from which its enrage counts.</summary>
    public long BossPullMs { get; set; }

    /// <summary>1 HP: damage to the boss by second, the last <see cref="PaceWindowSeconds"/> — the pace for the forecast.</summary>
    public List<(long Second, long Damage)> BossPace { get; } = new();

    public const int PaceWindowSeconds = 30;

    public void AddBossDamage(long timeMs, long damage)
    {
        if (BossPullMs == 0) BossPullMs = timeMs;
        var second = timeMs / 1000;
        if (BossPace.Count > 0 && BossPace[^1].Second == second) BossPace[^1] = (second, BossPace[^1].Damage + damage);
        else BossPace.Add((second, damage));
        BossPace.RemoveAll(p => second - p.Second >= PaceWindowSeconds);
    }

    /// <summary>Damage per second to the boss over the pace window (or since the pull, when shorter).</summary>
    public double BossPaceDps(long nowMs)
    {
        if (BossPace.Count == 0 || BossPullMs == 0) return 0;
        var span = Math.Clamp((nowMs - Math.Max(BossPullMs, nowMs - PaceWindowSeconds * 1000L)) / 1000.0, 1, PaceWindowSeconds);
        return BossPace.Where(p => nowMs / 1000 - p.Second < PaceWindowSeconds).Sum(p => p.Damage) / span;
    }

    /// <summary>Damage dealt to each NPC actor, used to pick the most relevant target as the encounter title.</summary>
    public Dictionary<uint, long> DamageByTarget { get; } = new();

    /// <summary>1 HP: when each target was last hit — a trash fight ends when every target hit lately is dead.</summary>
    public Dictionary<uint, long> LastHitByTarget { get; } = new();
    public Dictionary<uint, Combatant> Combatants { get; } = new();

    public long TotalDamage { get; set; }

    /// <summary>Fight length used for DPS: first hit to last hit, so idle tails never dilute the numbers.</summary>
    public long CombatMs => Math.Max(1_000, LastDamageMs - StartMs);

    /// <summary>Wall-clock timer shown in the overlay header.</summary>
    public long ClockMs(long nowMs) =>
        Math.Max(0, (IsActive ? nowMs : EndMs > 0 ? EndMs : LastDamageMs) - StartMs);
}
