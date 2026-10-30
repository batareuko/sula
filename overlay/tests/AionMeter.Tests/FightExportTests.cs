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

public class WorldMapsTests
{
    private static string DataFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "data", name);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void LoadsTheSixMapsWithTheirPlaces()
    {
        var maps = AionMeter.Core.OneHp.WorldMaps.Load(DataFile("world_maps.json"));
        Assert.Equal(6, maps.All.Count);
        var v = maps.Find("verteron")!;
        Assert.True(v.Layers["bosses"].Count > 10);
        Assert.True(v.Layers["dungeons"].Count > 10);
        Assert.All(v.Layers["bosses"].SelectMany(p => p.Points), p => Assert.InRange(p.X, 0, AionMeter.Core.OneHp.WorldMaps.Units));
        Assert.Equal(v.Metres, v.MetresBetween((0, 0), (10_000, 0)), 3);
    }
}

public class WhereaboutsTests
{
    [Fact]
    public void AfterATeleportYouAreAmongWhatCameIntoView()
    {
        var radar = new AionMeter.Core.OneHp.PetRadar([]);
        radar.Observe(new AionMeter.Core.Events.ZoneChangedEvent(1_000, 1010, "Open world", false, IsTeleport: true));
        Assert.Null(radar.Where(1_500));
        radar.Observe(new AionMeter.Core.Events.NpcSeenEvent(1_200, 1, 2000001, 100, X: -61_000, Y: 84_000));
        radar.Observe(new AionMeter.Core.Events.NpcSeenEvent(1_300, 2, 2000001, 100, X: -62_000, Y: 85_000));
        radar.Observe(new AionMeter.Core.Events.NpcSeenEvent(1_400, 3, 2000001, 100, X: -61_500, Y: 84_500));
        radar.Observe(new AionMeter.Core.Events.NpcSeenEvent(20_000, 4, 2000001, 100, X: 50_000, Y: 50_000)); // later: not the arrival
        var w = radar.Where(30_000); // the view of the last 20 s has one monster only: the arrival counts
        Assert.NotNull(w);
        Assert.Equal(1010, w!.MapId);
        Assert.Equal(-61_500, w.X);
        Assert.Equal(84_500, w.Y);
        Assert.False(w.Exact);
        Assert.Null(radar.Where(400_000)); // too old
    }

    [Fact]
    public void VerteronCalibrationPutsAKibeliskOnItsMark()
    {
        string Data(string name)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "data", name))) return Path.Combine(dir.FullName, "data", name);
            throw new FileNotFoundException(name);
        }
        var maps = AionMeter.Core.OneHp.WorldMaps.Load(Data("world_maps.json"), Data("map_calibration.json"));
        var v = maps.ForGameMap(1010)!;
        Assert.Equal("verteron", v.Slug);
        // the NPC at Dawn Legion Base's Kibelisk in the 2026-10-30 recording
        var at = v.ToMap(-111_039, -98_733)!.Value;
        var nearest = v.Layers["kibelisks"].SelectMany(k => k.Points.Select(p => (k.Name, D: v.MetresBetween(p, at)))).MinBy(x => x.D);
        Assert.Equal("Dawn Legion Base", nearest.Name);
        Assert.True(nearest.D < 30);
    }
}

public class WhereaboutsMidGameTests
{
    [Fact]
    public void StartedMidGameTheFieldBossListNamesTheMap()
    {
        var radar = new AionMeter.Core.OneHp.PetRadar([]);
        for (var i = 0; i < 3; i++)
            radar.Observe(new AionMeter.Core.Events.NpcSeenEvent(1_000 + i, (uint)i + 1, 2000001, 100, X: 1_000 + i, Y: 2_000 + i));
        Assert.Null(radar.Where(2_000)); // no map yet
        radar.Observe(new AionMeter.Core.Events.FieldBossListEvent(2_500, 1010, 0, [], []));
        var w = radar.Where(3_000);
        Assert.Equal(1010, w!.MapId);
        Assert.Equal(1_001, w.X);
        Assert.Null(radar.Where(60_000)); // the view is old and there was no teleport
    }
}
