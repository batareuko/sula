using AionMeter.Core.Events;
using AionMeter.Core.OneHp;

namespace AionMeter.Tests;

public sealed class DeathNoteTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "aionmeter-deathnote-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    private static DeathNote.Death Kill(int minute, string killer, int server = 2305, GameClass cls = GameClass.Unknown, long power = 0, string zone = "Lower Abyss") =>
        new(new DateTimeOffset(2026, 11, 2, 18, minute, 0, TimeSpan.Zero), "Celesta", 1304, killer, server, cls, power, 0, zone);

    [Fact]
    public void ThoseWhoKilledYouTwiceOrMoreAreNemeses()
    {
        var note = new DeathNote(_file);
        Assert.True(note.Add(Kill(1, "Miranda", cls: GameClass.Assassin, power: 210_000)));
        Assert.False(note.Add(Kill(1, "Miranda"))); // the same kill told twice
        Assert.True(note.Add(Kill(5, "Miranda", zone: "Middle Abyss")));
        Assert.True(note.Add(Kill(9, "Miranda", power: 230_000)));
        Assert.True(note.Add(Kill(7, "Borg")));
        Assert.True(note.Add(Kill(8, "Miranda", server: 2306))); // another Miranda, other server

        var n = Assert.Single(note.Nemeses());
        Assert.Equal("Miranda", n.Name);
        Assert.Equal(3, n.Kills);
        Assert.Equal(GameClass.Assassin, n.Class);
        Assert.Equal(230_000, n.Power);
        Assert.Equal("Lower Abyss", n.Zones[0]);

        // kept on disk
        Assert.Equal(5, new DeathNote(_file).All.Count);
    }

    [Fact]
    public void LaterNumbersFillTheRows()
    {
        var note = new DeathNote(null);
        note.Add(Kill(1, "Miranda"));
        note.Add(Kill(2, "Miranda"));
        note.Update("miranda", 2305, GameClass.Assassin, 0, 2650);
        var n = Assert.Single(note.Nemeses());
        Assert.Equal(GameClass.Assassin, n.Class);
        Assert.Equal(2650, n.ItemLevel);
    }
}

public class EarningsTests
{
    [Fact]
    public void GainsAndSpendsBySessionHourAndDay()
    {
        var start = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var e = new Earnings(null, start);
        e.Observe("Celesta", ResourceEvent.Kinah, 25_000_000, start);              // the login list: baseline only
        e.Observe("Celesta", ResourceEvent.Kinah, 25_050_000, start.AddMinutes(10)); // +50 000
        e.Observe("Celesta", ResourceEvent.Kinah, 25_042_274, start.AddMinutes(20)); // teleport −7 726
        e.Observe("Celesta", ResourceEvent.Kinah, 25_142_274, start.AddMinutes(90)); // +100 000
        e.Observe("Celesta", ResourceEvent.AbyssPoints, 130_000, start);
        e.Observe("Celesta", ResourceEvent.AbyssPoints, 131_500, start.AddMinutes(95));

        var s = e.Sums("Celesta", start.AddMinutes(100));
        var kinah = s.First(x => x.Id == ResourceEvent.Kinah);
        Assert.Equal(25_142_274, kinah.Balance);
        Assert.Equal(150_000, kinah.Session);
        Assert.Equal(100_000, kinah.Hour);
        Assert.Equal(150_000, kinah.Today);
        Assert.Equal(7_726, kinah.SpentToday);
        Assert.Equal(90_000, kinah.PerHour, 0); // 150 000 in 100 minutes
        Assert.Equal(1_500, s.First(x => x.Id == ResourceEvent.AbyssPoints).Session);
        Assert.Empty(e.Sums("Other", start.AddMinutes(100)));
    }
}

public class WalletPacketTests
{
    [Fact]
    public void KinahComesInTheResourceUpdatesWideField()
    {
        // from the 2026-10-31 Abyss recording: Kinah 25 114 052
        var bytes = Convert.FromHexString("000103000000c4357f010000000003");
        var events = new List<GameEvent>();
        new AionMeter.Core.Protocol.PacketParser(AionMeter.Core.Game.GameData.Empty, events.Add).Handle(AionMeter.Core.Protocol.Opcodes.ResourceUpdate, bytes);
        var r = Assert.IsType<ResourceEvent>(Assert.Single(events));
        Assert.Equal(ResourceEvent.Kinah, r.Id);
        Assert.Equal(25_114_052, r.Amount);
    }
}
