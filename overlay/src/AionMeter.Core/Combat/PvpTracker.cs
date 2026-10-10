using AionMeter.Core.Events;

namespace AionMeter.Core.Combat;

/// <summary>One player you fought: the damage you dealt them and they dealt you, their HP, whether you defeated them.</summary>
/// <param name="MaxHp">The highest HP seen (the game sends current HP only).</param>
public sealed record PvpOpponent(
    uint ActorId, string Name, GameClass Class, int ServerId, long Dealt, long Taken, int HitsDealt, int HitsTaken,
    long Hp, long MaxHp, bool Defeated, bool KilledYou, long LastMs)
{
    public double? HpFraction => MaxHp > 0 && Hp >= 0 ? Math.Clamp((double)Hp / MaxHp, 0, 1) : null;
}

/// <summary>
/// 1 HP: a PvP session — from your first hit on (or from) another player until a minute without any — with every
/// opponent, your kills and deaths and your biggest burst (damage dealt within 3 s).
/// </summary>
public sealed record PvpSession(
    Guid Id, long StartMs, long LastMs, bool Active, int Kills, int Deaths, long Dealt, long Taken, long Burst,
    IReadOnlyList<PvpOpponent> Opponents)
{
    public long DurationMs => Math.Max(0, LastMs - StartMs);
}

/// <summary>Keeps the PvP session; the tracker calls it under its lock.</summary>
internal sealed class PvpTracker
{
    public const long SessionGapMs = 60_000;
    private const long BurstWindowMs = 3_000;

    private sealed class Opp
    {
        public uint Id;
        public long Dealt, Taken, Hp = -1, MaxHp, LastMs;
        public int HitsDealt, HitsTaken;
        public bool Defeated, KilledYou;
        public GameClass Class;
    }

    private Guid _id = Guid.NewGuid();
    private long _start, _last;
    private int _kills, _deaths;
    private long _dealt, _taken, _burst;
    private readonly Dictionary<uint, Opp> _opps = new();
    private readonly Queue<(long T, long D)> _recent = new();

    public bool HasSession => _opps.Count > 0;

    public bool Tracks(uint id) => _opps.ContainsKey(id);

    /// <summary>A session idle for a minute ends: returns it (to save) and starts a new one on the next hit.</summary>
    public bool EndIfIdle(long nowMs) => HasSession && nowMs - _last > SessionGapMs;

    private Opp Get(uint id, long t)
    {
        if (_opps.Count == 0) { _start = t; _id = Guid.NewGuid(); }
        if (!_opps.TryGetValue(id, out var o)) _opps[id] = o = new Opp { Id = id };
        o.LastMs = t;
        if (t > _last) _last = t;
        return o;
    }

    public void Dealt(long t, uint target, long damage, GameClass skillClass)
    {
        var o = Get(target, t);
        o.Dealt += damage;
        o.HitsDealt++;
        _dealt += damage;
        _recent.Enqueue((t, damage));
        while (_recent.Count > 0 && t - _recent.Peek().T > BurstWindowMs) _recent.Dequeue();
        _burst = Math.Max(_burst, _recent.Sum(x => x.D));
    }

    public void Taken(long t, uint source, long damage, GameClass skillClass)
    {
        var o = Get(source, t);
        o.Taken += damage;
        o.HitsTaken++;
        if (o.Class == GameClass.Unknown && skillClass != GameClass.Unknown) o.Class = skillClass;
        _taken += damage;
    }

    public void Hp(uint id, long hp)
    {
        if (!_opps.TryGetValue(id, out var o)) return;
        o.Hp = hp;
        if (hp > o.MaxHp) o.MaxHp = hp;
    }

    public void YouKilled(long t, uint victim)
    {
        var o = Get(victim, t);
        if (!o.Defeated) _kills++;
        o.Defeated = true;
        o.Hp = 0;
    }

    public void KilledYou(long t, uint killer)
    {
        Get(killer, t).KilledYou = true;
        _deaths++;
    }

    public PvpSession? Snapshot(long nowMs, Func<uint, (string Name, GameClass Class, int Server)> who)
    {
        if (_opps.Count == 0) return null;
        var opps = _opps.Values
            .Select(o =>
            {
                var (name, cls, server) = who(o.Id);
                return new PvpOpponent(o.Id, name, cls != GameClass.Unknown ? cls : o.Class, server, o.Dealt, o.Taken, o.HitsDealt,
                    o.HitsTaken, o.Hp, o.MaxHp, o.Defeated, o.KilledYou, o.LastMs);
            })
            .OrderByDescending(o => o.Dealt + o.Taken).ToList();
        return new PvpSession(_id, _start, _last, nowMs - _last <= SessionGapMs, _kills, _deaths, _dealt, _taken, _burst, opps);
    }

    public void Reset()
    {
        _opps.Clear();
        _recent.Clear();
        _kills = _deaths = 0;
        _dealt = _taken = _burst = 0;
        _start = _last = 0;
    }
}
