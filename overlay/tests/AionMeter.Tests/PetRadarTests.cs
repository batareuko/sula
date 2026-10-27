using AionMeter.Core.Events;
using AionMeter.Core.OneHp;

namespace AionMeter.Tests;

public class PetRadarTests
{
    private static PetRadar Radar() => new([
        (new PetRadar.Soul(1066, "Papis", "Fera", "Gentle Papis", [("Verteron", 12)]), [2100001, 2100002]),
        (new PetRadar.Soul(1026, "Krall Warrior", "Cogni", "Krall Test Subject", [("Verteron", 52)]), [2100010]),
    ]);

    [Fact]
    public void Lists_soul_monsters_in_view_until_they_die()
    {
        var r = Radar();
        r.Observe(new NpcSeenEvent(1000, 1, 2100001));
        r.Observe(new NpcSeenEvent(1000, 2, 2100002));
        r.Observe(new NpcSeenEvent(1000, 3, 2100010));
        r.Observe(new NpcSeenEvent(1000, 4, 2999999)); // no soul
        var list = r.InView(2000);
        Assert.Equal(["Papis", "Krall Warrior"], list.Select(n => n.Soul.Pet));
        Assert.Equal(2, list[0].Count);

        r.Observe(new DeathEvent(3000, 1));
        Assert.Equal(1, r.InView(3000)[0].Count);
        Assert.Equal(["Krall Warrior"], r.InView(3000, new HashSet<int> { 1066 }).Select(n => n.Soul.Pet));
    }

    [Fact]
    public void Forgets_on_zone_change_and_after_a_while()
    {
        var r = Radar();
        r.Observe(new NpcSeenEvent(0, 1, 2100001));
        Assert.Single(r.InView(PetRadar.ForgetAfterMs));
        Assert.Empty(r.InView(PetRadar.ForgetAfterMs + 1));

        r.Observe(new NpcSeenEvent(0, 2, 2100010));
        r.Observe(new ZoneChangedEvent(10, 1110, "Altgard", false));
        Assert.Empty(r.InView(20));
    }

    [Fact]
    public void Shipped_table_loads()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "pet_souls.json");
        var r = PetRadar.Load(path);
        Assert.True(r.All.Count >= 150);
        Assert.Equal("Krall Warrior", r.All.First(s => s.PetId == 1026).Pet);
    }
}
