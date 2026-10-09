namespace AionMeter.Core.Combat;

public enum TargetMode
{
    /// <summary>Count damage to bosses only; falls back to everything while no boss is engaged.</summary>
    BossOnly,
    All,
}

public sealed class MeterOptions
{
    public TargetMode TargetMode { get; set; } = TargetMode.BossOnly;

    /// <summary>
    /// 1 HP: in the open world list only the local player and their party (the 02 97 roster); players around who are
    /// not in it stay out of the rows, the totals and the places. Instances hold only the party, so they are never
    /// filtered. Off by default here; the overlay turns it on (AppSettings.PartyOnly).
    /// </summary>
    public bool PartyOnly { get; set; }

    /// <summary>End the segment after this long without outgoing damage (no boss alive).</summary>
    public int IdleTimeoutMs { get; set; } = 10_000;

    /// <summary>Longer timeout while a boss is still alive — covers invulnerable phases and cut-scenes.</summary>
    public int BossIdleTimeoutMs { get; set; } = 30_000;

    /// <summary>1 HP: a pause this long on a training dummy ends the run, so the next hit starts the next one.</summary>
    public int DummyIdleTimeoutMs { get; set; } = 5_000;

    /// <summary>Single hits above this are treated as parse errors and dropped.</summary>
    public long MaxSingleHit { get; set; } = 50_000_000;

    /// <summary>Segments shorter than this are not saved to history.</summary>
    public int MinSavedFightMs { get; set; } = 5_000;

    /// <summary>How many finished segments to keep in memory for the segment picker.</summary>
    public int MaxSegments { get; set; } = 30;
}

/// <summary>1 HP: the party filter at a glance — off, not possible yet (who you are is not known), an instance (no
/// filter needed), on your own, or with these party members.</summary>
public enum PartyFilterState { Off, SelfUnknown, Instance, Solo, Party }

public sealed record PartyFilterStatus(PartyFilterState State, IReadOnlyList<string> Party);
