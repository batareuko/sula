namespace AionMeter.Core.Combat;

/// <summary>1 HP: how an opponent compares with you: much stronger (run), stronger, even, weaker; or not known yet.</summary>
public enum ThreatLevel
{
    Unknown,
    Weak,
    Even,
    Caution,
    Danger,
}

/// <summary>
/// 1 HP: the comparison behind a <see cref="ThreatLevel"/> — by gear score when both are known (inspected), else by
/// combat power (sent for everyone in view).
/// </summary>
public sealed record ThreatInfo(ThreatLevel Level, bool ByGear, long Mine, long Theirs)
{
    public long Diff => Theirs - Mine;
}

/// <summary>1 HP: the gaps (theirs minus yours) for each level; Settings → PvP.</summary>
public sealed record ThreatThresholds(int DangerGear = 1000, int CautionGear = 750, int WeakGear = 1000,
    int DangerPower = 10_000, int CautionPower = 7_500, int WeakPower = 10_000);

public static class PvpThreat
{
    /// <summary>Red from <c>Danger</c> above you, orange from <c>Caution</c>, green from <c>Weak</c> below you.</summary>
    public static ThreatInfo Assess(long myGear, long theirGear, long myPower, long theirPower, ThreatThresholds t)
    {
        if (myGear > 0 && theirGear > 0) return Level(true, myGear, theirGear, t.DangerGear, t.CautionGear, t.WeakGear);
        if (myPower > 0 && theirPower > 0) return Level(false, myPower, theirPower, t.DangerPower, t.CautionPower, t.WeakPower);
        return new ThreatInfo(ThreatLevel.Unknown, theirGear > 0, myGear > 0 ? myGear : myPower, theirGear > 0 ? theirGear : theirPower);
    }

    private static ThreatInfo Level(bool byGear, long mine, long theirs, int danger, int caution, int weak)
    {
        var diff = theirs - mine;
        var level = diff >= danger ? ThreatLevel.Danger
            : diff >= caution ? ThreatLevel.Caution
            : diff <= -weak ? ThreatLevel.Weak
            : ThreatLevel.Even;
        return new ThreatInfo(level, byGear, mine, theirs);
    }
}
