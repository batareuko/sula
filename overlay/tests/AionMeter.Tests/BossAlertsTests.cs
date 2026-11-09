using AionMeter.Core.OneHp;
using Xunit;

namespace AionMeter.Tests;

/// <summary>1 HP: big mechanic warnings (data/boss_alerts.json).</summary>
public class BossAlertsTests
{
    private const string Json = """
    { "bosses": { "Eternal Ludra": [
      { "id": "pull", "icon": "orb", "pull": true, "uk": { "title": "PULL", "do": "d" } },
      { "id": "bingo1", "icon": "safe", "hp": 50, "skills": ["Ludra's Trial"], "uk": { "title": "BINGO", "do": "d", "then": "next" } },
      { "id": "bingo2", "icon": "safe", "hp": 25, "skills": ["Ludra's Trial"], "uk": { "title": "BINGO 2", "do": "d" } },
      { "id": "enrage", "icon": "enrage", "at": 600, "en": { "title": "ENRAGE", "do": "d" } },
      { "id": "orbs", "icon": "orb", "skills": ["Red Orb"], "soonSec": 6, "uk": { "title": "ORBS", "do": "d" } }
    ] } }
    """;

    private static readonly Guid FightId = Guid.NewGuid();

    private static BossAlerts Make() => BossAlerts.Load(Json, [(2301014, "Eternal Ludra"), (5, "Someone")], "uk", "en");

    private static BossAlerts.Fight F(double? hp, long elapsedSec, (string, long)[]? casts = null, (string, long)[]? upcoming = null) =>
        new(FightId, 2301014, hp, elapsedSec * 1000, 1_000_000 + elapsedSec * 1000, casts ?? [], upcoming ?? []);

    [Fact]
    public void Loads_by_english_name_with_a_language_fallback()
    {
        var a = Make();
        Assert.True(a.Knows(2301014));
        Assert.False(a.Knows(5));
        Assert.Equal("ENRAGE", a.For(2301014).Single(x => x.Id == "enrage").Title); // no "uk": the English text
    }

    [Fact]
    public void Pull_alert_fires_once_at_the_start()
    {
        var a = Make();
        var (shown, fired) = a.Tick(F(100, 1));
        Assert.Equal("pull", Assert.Single(fired).Id);
        Assert.Equal(BossAlerts.Stage.Now, shown[0].Stage);
        Assert.Empty(a.Tick(F(100, 3)).Fired);
        Assert.DoesNotContain(a.Tick(F(100, 12)).Shown, b => b.Alert.Id == "pull"); // gone after ShowMs
    }

    [Fact]
    public void Hp_alert_is_soon_a_few_percent_early_then_now_once()
    {
        var a = Make();
        a.Tick(F(100, 1));
        Assert.DoesNotContain(a.Tick(F(60, 30)).Shown, b => b.Alert.Id == "bingo1");
        var soon = a.Tick(F(53, 40)).Shown.Single(b => b.Alert.Id == "bingo1");
        Assert.Equal(BossAlerts.Stage.Soon, soon.Stage);
        var (shown, fired) = a.Tick(F(49.5, 45));
        Assert.Contains(fired, x => x.Id == "bingo1");
        Assert.Equal(BossAlerts.Stage.Now, shown.Single(b => b.Alert.Id == "bingo1").Stage);
        Assert.Empty(a.Tick(F(48, 47)).Fired);
        Assert.DoesNotContain(a.Tick(F(45, 60)).Shown, b => b.Alert.Id == "bingo1");
    }

    [Fact]
    public void The_skill_fires_the_alert_only_near_its_own_hp_mark()
    {
        var a = Make();
        a.Tick(F(100, 1));
        var fired = a.Tick(F(52, 40, casts: [("Ludra's Trial", 1_000_000 + 40_000)])).Fired;
        Assert.Contains(fired, x => x.Id == "bingo1");      // the cast came before the HP mark
        Assert.DoesNotContain(fired, x => x.Id == "bingo2"); // 52 % is far from 25 %
    }

    [Fact]
    public void Clock_alert_counts_down_then_fires()
    {
        var a = Make();
        a.Tick(F(100, 1));
        var soon = a.Tick(F(90, 595)).Shown.Single(b => b.Alert.Id == "enrage");
        Assert.Equal(BossAlerts.Stage.Soon, soon.Stage);
        Assert.Equal(5, soon.LeftSec);
        Assert.Contains(a.Tick(F(90, 600)).Fired, x => x.Id == "enrage");
    }

    [Fact]
    public void A_skill_alert_warns_from_the_learnt_gap_and_comes_back_with_every_cast()
    {
        var a = Make();
        a.Tick(F(100, 1));
        var soon = a.Tick(F(90, 30, upcoming: [("Red Orb", 1_000_000 + 34_000)])).Shown.Single(b => b.Alert.Id == "orbs");
        Assert.Equal((BossAlerts.Stage.Soon, 4.0), (soon.Stage, soon.LeftSec));
        Assert.Contains(a.Tick(F(90, 34, casts: [("Red Orb", 1_000_000 + 34_000)])).Fired, x => x.Id == "orbs");
        Assert.Empty(a.Tick(F(90, 36, casts: [("Red Orb", 1_000_000 + 34_000)])).Fired);
        Assert.Contains(a.Tick(F(90, 70, casts: [("Red Orb", 1_000_000 + 70_000)])).Fired, x => x.Id == "orbs");
    }

    private static string DataDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "data", "boss_alerts.json"))) return Path.Combine(dir.FullName, "data");
        throw new FileNotFoundException("boss_alerts.json");
    }

    [Fact]
    public void Bundled_file_names_real_bosses()
    {
        var path = DataDir();
        var npcs = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "npcs", "en.json"))).RootElement.EnumerateObject()
            .Select(n => (int.Parse(n.Name), n.Value.GetProperty("name").GetString() ?? "")).ToList();
        var a = BossAlerts.Load(File.ReadAllText(Path.Combine(path, "boss_alerts.json")), npcs, "uk");
        foreach (var code in new[] { 2301006, 2301007, 2301008, 2301014 }) Assert.True(a.Knows(code), code.ToString());
        var skills = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "skills", "en.json"))).RootElement.EnumerateObject()
            .Select(s => s.Value.GetString()).ToHashSet();
        foreach (var code in new[] { 2301006, 2301007, 2301008, 2301014 })
            foreach (var al in a.For(code))
            {
                Assert.False(string.IsNullOrWhiteSpace(al.Title));
                foreach (var s in al.Skills) Assert.Contains(s, skills);
            }
    }
}
