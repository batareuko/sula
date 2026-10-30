using AionMeter.Core.Events;

namespace AionMeter.Core.Combat;

/// <summary>1 HP: a buff or debuff on an actor as the game last sent it.</summary>
/// <param name="EndMs">When it runs out; 0 for one that lasts until removed.</param>
public sealed record ActiveBuff(uint TargetId, int Code, string Name, bool Debuff, uint CasterId, long StartMs, long EndMs, int InstanceId)
{
    public long DurationMs => EndMs > 0 ? EndMs - StartMs : 0;

    public long LeftMs(long nowMs) => EndMs > 0 ? Math.Max(0, EndMs - nowMs) : long.MaxValue;
}

/// <summary>1 HP: the buffs on every actor the game reports, until they run out or are removed.</summary>
internal sealed class BuffBook
{
    private readonly Dictionary<(uint Target, int Key), ActiveBuff> _buffs = new();

    public void Apply(BuffEvent e, string name, bool debuff)
    {
        var key = (e.TargetId, e.InstanceId != 0 ? e.InstanceId : e.Code);
        if (e.Removing)
        {
            _buffs.Remove(key);
            return;
        }
        _buffs[key] = new ActiveBuff(e.TargetId, e.Code, name, debuff, e.CasterId, e.TimeMs,
            e.DurationMs > 0 ? e.TimeMs + e.DurationMs : 0, e.InstanceId);
    }

    public List<ActiveBuff> On(uint target, long nowMs)
    {
        Expire(nowMs);
        return _buffs.Values.Where(b => b.TargetId == target).OrderBy(b => b.LeftMs(nowMs)).ToList();
    }

    private void Expire(long nowMs)
    {
        foreach (var key in _buffs.Where(kv => kv.Value.EndMs > 0 && kv.Value.EndMs + 1_000 < nowMs).Select(kv => kv.Key).ToList())
            _buffs.Remove(key);
    }

    /// <summary>A zone change: ids are handed out again.</summary>
    public void Clear() => _buffs.Clear();
}

/// <summary>1 HP: a party member's skill on cooldown.</summary>
/// <summary>
/// 1 HP: one row of the party window. <paramref name="Hp"/> is -1 until the game sent it; <paramref name="HpAtMs"/> is when.
/// </summary>
public sealed record PartyFrameMember(string Name, Events.GameClass Class, bool IsSelf, uint? ActorId, long Hp, long MaxHp, long HpAtMs,
    IReadOnlyList<ActiveBuff> Buffs, IReadOnlyList<PartyCooldown> Cooldowns)
{
    public double HpFraction => MaxHp > 0 && Hp >= 0 ? Math.Clamp((double)Hp / MaxHp, 0, 1) : -1;
}

public sealed record PartyCooldown(string Player, string Skill, long LeftMs, long TotalMs, int SkillCode = 0);
