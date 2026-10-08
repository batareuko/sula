using AionMeter.Core.Capture;
using AionMeter.Core.OneHp;

namespace AionMeter.Tests;

/// <summary>1 HP additions: Global schedule, ping statistics, game-stream loss counters.</summary>
public class OneHpTests
{
    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    [Fact]
    public void RiftOpensEveryThreeHoursForTenMinutes()
    {
        var open = GlobalSchedule.Rift(Utc(2026, 10, 7, 9, 5));
        Assert.True(open.IsOpen);
        Assert.Equal(Utc(2026, 10, 7, 9, 10), open.Closes);

        var closed = GlobalSchedule.Rift(Utc(2026, 10, 7, 9, 10));
        Assert.False(closed.IsOpen);
        Assert.Equal(Utc(2026, 10, 7, 12, 0), closed.Opens);

        Assert.Equal(Utc(2026, 10, 8, 0, 0), GlobalSchedule.Rift(Utc(2026, 10, 7, 22, 30)).Opens);
    }

    [Fact]
    public void RiftUsesUtcWhateverTheOffset()
    {
        var kyiv = new DateTimeOffset(2026, 10, 7, 12, 5, 0, TimeSpan.FromHours(3)); // 09:05 UTC
        Assert.True(GlobalSchedule.Rift(kyiv).IsOpen);
    }

    [Fact]
    public void DailyResetAtSevenUtc()
    {
        Assert.Equal(Utc(2026, 10, 7, 7, 0), GlobalSchedule.NextDailyReset(Utc(2026, 10, 7, 6, 59)));
        Assert.Equal(Utc(2026, 10, 8, 7, 0), GlobalSchedule.NextDailyReset(Utc(2026, 10, 7, 7, 0)));
    }

    [Fact]
    public void WeeklyResetOnWednesday()
    {
        // 2026-10-07 is a Wednesday
        Assert.Equal(DayOfWeek.Wednesday, Utc(2026, 10, 7, 0, 0).DayOfWeek);
        Assert.Equal(Utc(2026, 10, 7, 7, 0), GlobalSchedule.NextWeeklyReset(Utc(2026, 10, 5, 12, 0)));
        Assert.Equal(Utc(2026, 10, 14, 7, 0), GlobalSchedule.NextWeeklyReset(Utc(2026, 10, 7, 7, 0)));
        Assert.Equal(Utc(2026, 10, 14, 7, 0), GlobalSchedule.NextWeeklyReset(Utc(2026, 10, 8, 1, 0)));
    }

    [Fact]
    public void NetStatsWindowLossAndJitter()
    {
        var s = new NetStats(10_000);
        s.Add(0, 100); // falls out of the window below
        s.Add(5_000, 10);
        s.Add(6_000, null);
        s.Add(7_000, 14);
        s.Add(12_000, 12);
        var sum = s.Summary();
        Assert.Equal(4, sum.Samples);
        Assert.Equal(1, sum.Lost);
        Assert.Equal(25, sum.LossPct);
        Assert.Equal(12, sum.AvgMs);
        Assert.Equal(3, sum.JitterMs); // |14-10| and |12-14| → 3
        Assert.Equal(12, sum.LastMs);
    }

    [Fact]
    public void ReassemblerCountsHolesForLostSegments()
    {
        var counters = new StreamCounters();
        var got = new List<byte>();
        var r = new TcpReassembler(d => got.AddRange(d.ToArray()), () => { }, counters);
        r.Start(1000);
        r.Push(1000, new byte[] { 1, 2 }, 0);
        r.Push(1004, new byte[] { 5, 6 }, 10); // 1002..1003 lost: a hole
        r.Push(1006, new byte[] { 7 }, 11);    // still the same hole, not a new one
        r.Push(1002, new byte[] { 3, 4 }, 50); // the re-sent segment fills it
        r.Push(1002, new byte[] { 3, 4 }, 60); // a duplicate
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, got.ToArray());
        Assert.Equal(5, counters.Segments);
        Assert.Equal(1, counters.Holes);
        Assert.Equal(1, counters.Duplicates);
        Assert.Equal(0, counters.Gaps);
    }

    [Fact]
    public void StreamLossWindowNeedsTrafficAndSurvivesRestart()
    {
        var w = new StreamLossWindow(60_000);
        w.Add(0, 0, 0);
        w.Add(1_000, 20, 1);
        Assert.Null(w.Current().LossPct); // 20 segments: too little to judge
        w.Add(2_000, 400, 6);
        Assert.Equal(1.5, w.Current().LossPct);
        w.Add(3_000, 10, 0); // capture restarted, counters from zero
        Assert.Null(w.Current().LossPct);
    }

    [Fact]
    public void GradeFollowsStreamLossFirst()
    {
        var quiet = new NetSummary(0, 0, 0, null, null, null, null, null);
        var noPing = new NetSummary(60, 60, 100, null, null, null, null, null); // server ignores ICMP
        Assert.Equal(NetQuality.Unknown, NetGrade.Of(null, quiet));
        Assert.Equal(NetQuality.Good, NetGrade.Of(0.1, noPing));
        Assert.Equal(NetQuality.Fair, NetGrade.Of(0.8, noPing));
        Assert.Equal(NetQuality.Bad, NetGrade.Of(2.5, noPing));
        var shaky = new NetSummary(60, 0, 0, 280, 250, 400, 45, 300);
        Assert.Equal(NetQuality.Fair, NetGrade.Of(null, shaky));
    }
}

public class PersonalRecordTests
{
    private static Core.Storage.HistoryEntry E(string file, int boss, Core.Combat.EncounterEndReason r, string self, double dps) =>
        new(file, "Boss", DateTimeOffset.UnixEpoch, 60_000, r, dps * 4, (long)(dps * 240), "Zone", boss, boss, 4, self,
            Core.Events.GameClass.Cleric, dps, 1);

    private static Core.Combat.EncounterSnapshot Snap(int boss, double selfDps, Core.Combat.EncounterEndReason reason) =>
        new(Guid.NewGuid(), "Boss", "Zone", DateTimeOffset.UnixEpoch, 60_000, 60_000, false, reason, 100, 100,
            new Core.Combat.BossSnapshot(1, boss, "Boss", 0, 1000),
            [new Core.Combat.CombatantSnapshot(7, "Whelps", Core.Events.GameClass.Cleric, true, 100, selfDps, 1, 10, 0.2, 50, 0)]);

    private static readonly Core.Storage.HistoryEntry[] History =
    [
        E("a", 500, Core.Combat.EncounterEndReason.Kill, "Whelps", 1000),
        E("b", 500, Core.Combat.EncounterEndReason.Kill, "Whelps", 1200),
        E("c", 500, Core.Combat.EncounterEndReason.Wipe, "Whelps", 5000), // a wipe is not a record
        E("d", 500, Core.Combat.EncounterEndReason.Kill, "Alt", 9000),    // another character
        E("e", 600, Core.Combat.EncounterEndReason.Kill, "Whelps", 7000), // another boss
    ];

    [Fact]
    public void BestIsHighestKillOfThisCharacterOnThisBoss()
    {
        Assert.Equal("b", Core.OneHp.PersonalRecords.Best(History, 500, "Whelps")!.FileName);
        Assert.Equal("a", Core.OneHp.PersonalRecords.Best(History, 500, "Whelps", excludeFile: "b")!.FileName);
        Assert.Null(Core.OneHp.PersonalRecords.Best(History, 0, "Whelps"));
    }

    [Fact]
    public void CompareGivesDeltaAndNewRecord()
    {
        var live = Core.OneHp.PersonalRecords.Compare(History, Snap(500, 1080, Core.Combat.EncounterEndReason.None))!;
        Assert.Equal(-10, live.DeltaPct);
        Assert.False(live.IsNewRecord);
        var beat = Core.OneHp.PersonalRecords.Compare(History, Snap(500, 1500, Core.Combat.EncounterEndReason.Kill))!;
        Assert.Equal(25, beat.DeltaPct);
        Assert.True(beat.IsNewRecord);
        Assert.Null(Core.OneHp.PersonalRecords.Compare(History, Snap(777, 1500, Core.Combat.EncounterEndReason.Kill)));
    }
}

public class GuildDataTests
{
    [Fact]
    public void ParseGearPicksTheServerFromMatches()
    {
        const string json = """{"matches":[{"name":"Whelps","serverId":2308,"itemLevel":1765,"combatPower":79823},{"name":"Whelps","serverId":1501,"itemLevel":2155,"combatPower":86209}]}""";
        Assert.Equal(new Core.OneHp.GearInfo(2155, 86209), Core.OneHp.GuildData.ParseGear(json, 1501));
        Assert.Null(Core.OneHp.GuildData.ParseGear(json, 9999));
        Assert.Null(Core.OneHp.GuildData.ParseGear(json, 0)); // two matches, server unknown: no guess
        const string single = """{"character":{"name":"Sula","serverId":2303,"itemLevel":1401,"combatPower":72283},"game":{}}""";
        Assert.Equal(1401, Core.OneHp.GuildData.ParseGear(single, 0)!.ItemLevel);
        Assert.Null(Core.OneHp.GuildData.ParseGear("""{"error":"not_found"}""", 1501));
    }

    private static Core.Combat.EncounterSnapshot Fight(Core.Combat.EncounterEndReason reason, string selfName, long combatMs = 120_000) =>
        new(Guid.NewGuid(), "Kernon", "Fire Temple", new DateTimeOffset(2026, 10, 8, 18, 0, 0, TimeSpan.Zero), combatMs, combatMs, false, reason,
            300_000, 2500, new Core.Combat.BossSnapshot(9, 2100050, "Kernon", 0, 1_000_000),
            [
                new Core.Combat.CombatantSnapshot(1, "Tank", Core.Events.GameClass.Templar, false, 200_000, 1666, 0.66, 50, 0.1, 9000, 0, 1501),
                new Core.Combat.CombatantSnapshot(2, selfName, Core.Events.GameClass.Cleric, true, 100_000, 833.3, 0.33, 30, 0.2, 5000, 0, 1501),
                new Core.Combat.CombatantSnapshot(Core.Combat.Combatant.UnknownSummonsId, "", default, false, 1, 0, 0, 1, 0, 1, 0),
            ]);

    [Fact]
    public void RecordHasOnlyYourOwnNumbersAndOnlyForKills()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Core.OneHp.GuildData.BuildRecord(Fight(Core.Combat.EncounterEndReason.Kill, "Whelps")));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var r = doc.RootElement;
        Assert.Equal("Whelps", r.GetProperty("character").GetString());
        Assert.Equal("Cleric", r.GetProperty("class").GetString());
        Assert.Equal(2100050, r.GetProperty("bossCode").GetInt32());
        Assert.Equal(1501, r.GetProperty("serverId").GetInt32());
        Assert.Equal(2, r.GetProperty("place").GetInt32());
        Assert.Equal(2, r.GetProperty("partySize").GetInt32()); // the unknown-summons row is not a player
        Assert.DoesNotContain("Tank", json);
        Assert.Null(Core.OneHp.GuildData.BuildRecord(Fight(Core.Combat.EncounterEndReason.Wipe, "Whelps")));
        Assert.Null(Core.OneHp.GuildData.BuildRecord(Fight(Core.Combat.EncounterEndReason.Kill, "#4242")));
        Assert.Null(Core.OneHp.GuildData.BuildRecord(Fight(Core.Combat.EncounterEndReason.Kill, "Whelps", combatMs: 3_000)));
    }
}
