using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Storage;
using Xunit;

namespace AionMeter.Tests;

public class FightExportTests
{
    private static HistoryEntry Entry(int day, int boss, EncounterEndReason reason, double dps, long ms, double self) =>
        new($"f{day}.json", "Boss", new DateTimeOffset(2026, 10, day, 20, 0, 0, TimeSpan.Zero), ms, reason, dps, 1_000_000, null,
            boss, 0, 6, "Me", GameClass.Unknown, self, 1);

    [Fact]
    public void ComparesWithTheLastKillOfTheSameBoss()
    {
        var now = Entry(30, 7, EncounterEndReason.Kill, 12_000, 100_000, 3_300);
        var history = new[]
        {
            Entry(20, 7, EncounterEndReason.Kill, 9_000, 130_000, 2_000),
            Entry(25, 7, EncounterEndReason.Kill, 10_000, 110_000, 3_000), // the latest kill: this one
            Entry(27, 7, EncounterEndReason.Wipe, 5_000, 60_000, 1_000),   // a wipe is no yardstick for a kill
            Entry(28, 8, EncounterEndReason.Kill, 20_000, 50_000, 5_000),  // another boss
            now,
        };
        var cmp = FightExport.CompareWithPrevious(now, history);
        Assert.NotNull(cmp);
        Assert.Equal("f25.json", cmp!.Before.FileName);
        Assert.Equal(0.2, cmp.PartyDpsChange, 6);
        Assert.Equal(-10_000, cmp.CombatMsChange);
        Assert.Equal(0.1, cmp.SelfDpsChange!.Value, 6);
        Assert.Null(FightExport.CompareWithPrevious(history[0], history));
    }

    [Fact]
    public void CsvHasOneRowPerPlayer()
    {
        var s = new EncounterSnapshot(Guid.NewGuid(), "Boss; \"big\"", null, DateTimeOffset.UnixEpoch, 10_000, 10_000, false,
            EncounterEndReason.Kill, 300, 30, null,
            [
                new CombatantSnapshot(2, "Two", GameClass.Unknown, false, 100, 10, 1 / 3.0, 5, 0.2, 40, 0),
                new CombatantSnapshot(1, "One", GameClass.Unknown, true, 200, 20, 2 / 3.0, 20, 0.5, 90, 7) { Healing = 50, Deaths = 1 },
            ]);
        var lines = FightExport.Csv(s).TrimStart('﻿').Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("\"Boss; \"\"big\"\"\";", lines[1]);
        Assert.Contains(";1;One;", lines[1]); // the top damage first
        Assert.Contains(";2.00;", lines[1]); // 20 hits in 10 s
        Assert.EndsWith(";50;5;7;1", lines[1]);
    }
}
