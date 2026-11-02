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
