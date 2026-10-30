using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace AionMeter.Tests;

/// <summary>1 HP: the snapshot the local /state endpoint serves (App/Services/StreamServer.cs).</summary>
public class LiveStateTests
{
    private const uint Me = 100, Mate = 101, Boss = 500;

    private static CombatTracker Setup(out CombatTracker tracker)
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions());
        t.Process(new SelfIdentifiedEvent(1_000, Me, "Batareyka", 1304, GameClass.Cleric));
        t.Process(new PlayerSeenEvent(1_000, Mate, "SaitoHiraga", 1304, GameClass.Templar));
        t.Process(new PartyListEvent(1_000,
        [
            new PartyMemberInfo(Me, "Batareyka", 1304, 0),
            new PartyMemberInfo(Mate, "SaitoHiraga", 1304, 1),
        ]));
        tracker = t;
        return t;
    }

    [Fact]
    public void No_identity_means_not_ok()
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions());
        Assert.Null(t.State(1_000));
    }

    [Fact]
    public void Quiet_stream_means_not_ok()
    {
        var t = Setup(out _);
        Assert.Null(t.State(1_000 + 60_000)); // the game's stream went quiet: no heartbeats
    }

    [Fact]
    public void Character_party_and_target_state()
    {
        var t = Setup(out _);
        t.Process(new NpcHpEvent(1_000, Me, 18_450, 22_206, IsNpc: false));
        t.Process(new NpcHpEvent(1_000, Mate, 15_000, 21_000, IsNpc: false));
        t.Process(new MoveEvent(1_000, Me, 0, 0, 0));
        t.Process(new MoveEvent(1_000, Mate, 820, 0, 0));
        t.Process(new NpcSeenEvent(1_000, Boss, 2300243, 120_000, X: 1_250, Y: 0, Z: 0));
        t.Process(new DamageEvent(1_200, Me, Boss, 17010000, 1_000, HitFlags.None));

        var s = t.State(2_000);

        Assert.NotNull(s);
        Assert.Equal("Batareyka", s!.Me.Name);
        Assert.Equal("cleric", s.Me.Class);
        Assert.True(s.Me.Alive);
        Assert.Equal(18_450, s.Me.Hp);
        Assert.Equal(22_206, s.Me.HpMax);
        Assert.True(s.Me.InCombat);
        Assert.Null(s.Me.Mp);
        Assert.Null(s.Me.Casting);

        var mate = Assert.Single(s.Party);
        Assert.Equal("SaitoHiraga", mate.Name);
        Assert.Equal("templar", mate.Class);
        Assert.True(mate.Alive);
        Assert.Equal(15_000, mate.Hp);
        Assert.Equal(21_000, mate.HpMax);
        Assert.Equal(8.2, mate.Distance);
        Assert.Null(mate.InRange);

        Assert.NotNull(s.Target);
        Assert.True(s.Target!.IsEnemy);
        Assert.Equal(12.5, s.Target.Distance);
    }

    [Fact]
    public void Player_vitals_feed_hp_mp_and_alive()
    {
        var t = Setup(out _);
        t.Process(new PlayerVitalsEvent(1_000, Me, 179_000, 123_400));
        t.Process(new PlayerVitalsEvent(1_100, Me, 183_000, null)); // HP rises, MP not resent

        var s = t.State(2_000)!.Me;
        Assert.Equal(183_000, s.Hp);
        Assert.Equal(183_000, s.HpMax); // the highest value seen is the only max the packet implies
        Assert.Equal(123_400, s.Mp);
        Assert.Equal(123_400, s.MpMax);
        Assert.True(s.Alive);
    }

    [Fact]
    public void Cooldown_reports_remaining_total_and_charges()
    {
        var t = Setup(out _);
        t.Process(new SkillCooldownEvent(1_000, Me, 17010000, 8_000, 2));

        var skill = Assert.Single(t.State(3_500)!.Me.Skills);
        Assert.Equal(17010000, skill.Id);
        Assert.Equal(5_500, skill.CdRemainMs); // the game already sent the reduced cooldown
        Assert.Equal(8_000, skill.CdTotalMs);
        Assert.Equal(2, skill.Charges);
        Assert.Null(skill.ChargesMax); // the packet carries no maximum
        Assert.Null(skill.ToggleOn);
    }

    [Fact]
    public void Toggle_is_on_while_the_lasting_buff_is_on()
    {
        var t = Setup(out _);
        t.Process(new CastEvent(1_000, Me, Me, 18050000));
        t.Process(new BuffEvent(1_100, Me, Me, 180, 0, 1, Removing: false));

        var on = t.State(2_000)!.Me;
        Assert.True(on.Skills.Single(k => k.Id == 18050000).ToggleOn);
        Assert.Contains(on.Buffs, b => b.Id == 180 && b.RemainMs == null); // lasts until removed

        t.Process(new BuffEvent(2_500, Me, Me, 180, 0, 1, Removing: true));
        Assert.False(t.State(3_000)!.Me.Skills.Single(k => k.Id == 18050000).ToggleOn);
    }

    [Fact]
    public void Zone_change_clears_positions_and_health_but_keeps_the_party_names()
    {
        var t = Setup(out _);
        t.Process(new NpcHpEvent(1_000, Me, 18_450, 22_206, IsNpc: false));
        t.Process(new MoveEvent(1_000, Me, 0, 0, 0));
        t.Process(new MoveEvent(1_000, Mate, 820, 0, 0));
        t.Process(new ZoneChangedEvent(2_000, 1110, "Open world", IsDungeon: false));

        var s = t.State(2_500)!;
        Assert.Null(s.Me.Hp); // ids are reissued: the old HP belonged to another entity
        Assert.Null(s.Target);
        var mate = Assert.Single(s.Party); // the party itself survives a zone change, without the old ids
        Assert.Equal("SaitoHiraga", mate.Name);
        Assert.Null(mate.Hp);
        Assert.Null(mate.Distance);
    }
}
