using System.Text.Json;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.OneHp;

namespace AionMeter.Tests;

/// <summary>1 HP: the JSON the /state endpoint serves (Core/OneHp/CharacterState.cs).</summary>
public class CharacterStateTests
{
    private const uint Me = 100, Mate = 200;

    private static GameData Data()
    {
        var data = GameData.Empty;
        data.Buffs[111] = ("Light of Protection", false);
        data.Buffs[222] = ("Poison", true);
        data.Skills[17430020] = "Bolt";
        return data;
    }

    private static CombatTracker Tracker()
    {
        var t = new CombatTracker(Data(), new MeterOptions());
        t.Process(new SelfIdentifiedEvent(0, Me, "Batareyka", 1304, GameClass.Cleric));
        t.Process(new PlayerSeenEvent(0, Mate, "SaitoHiraga", 1304, GameClass.Templar));
        t.Process(new PartyRosterEvent(0, ["Batareyka", "SaitoHiraga"], Complete: true));
        t.Process(new NpcHpEvent(1_000, Me, 18_450, 22_206, IsNpc: false));
        t.Process(new NpcHpEvent(1_000, Mate, 15_000, 21_000, IsNpc: false));
        t.Process(new BuffEvent(1_000, Me, 0, 111, 0, 0, Removing: false));
        t.Process(new BuffEvent(1_000, Me, 0, 222, 30_000, 0, Removing: false));
        t.Process(new SkillCooldownEvent(1_000, Me, 17430020, 40_000));
        return t;
    }

    [Fact]
    public void No_self_yet_is_not_ok()
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions());
        Assert.Equal("""{"ok":false}""", CharacterState.Json(t, 1_000));
    }

    [Fact]
    public void Self_has_name_class_hp_buffs_and_skill_cooldowns()
    {
        using var doc = JsonDocument.Parse(CharacterState.Json(Tracker(), 11_000));
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(11_000, root.GetProperty("ts").GetInt64());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("target").ValueKind);

        var me = root.GetProperty("me");
        Assert.Equal("Batareyka", me.GetProperty("name").GetString());
        Assert.Equal("cleric", me.GetProperty("cls").GetString());
        Assert.True(me.GetProperty("alive").GetBoolean());
        Assert.Equal(18_450, me.GetProperty("hp").GetInt64());
        Assert.Equal(22_206, me.GetProperty("hpMax").GetInt64());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("mp").ValueKind);
        Assert.Equal(JsonValueKind.Null, me.GetProperty("mpMax").ValueKind);
        Assert.Equal(JsonValueKind.Null, me.GetProperty("casting").ValueKind);
        Assert.False(me.GetProperty("inCombat").GetBoolean());

        var buffs = me.GetProperty("buffs");
        Assert.Equal(1, buffs.GetArrayLength());
        Assert.Equal("Light of Protection", buffs[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, buffs[0].GetProperty("remainMs").ValueKind); // until removed
        Assert.Equal(JsonValueKind.Null, buffs[0].GetProperty("stacks").ValueKind);

        var debuffs = me.GetProperty("debuffs");
        Assert.Equal(1, debuffs.GetArrayLength());
        Assert.Equal("Poison", debuffs[0].GetProperty("name").GetString());
        Assert.Equal(20_000, debuffs[0].GetProperty("remainMs").GetInt64()); // 30 000 ms from 1 000 → 20 000 left at 11 000
        Assert.Equal(JsonValueKind.Null, debuffs[0].GetProperty("dispellable").ValueKind);

        var skills = me.GetProperty("skills");
        Assert.Equal(1, skills.GetArrayLength());
        Assert.Equal(17430020, skills[0].GetProperty("id").GetInt32());
        Assert.Equal("Bolt", skills[0].GetProperty("name").GetString());
        Assert.Equal(30_000, skills[0].GetProperty("cdRemainMs").GetInt64());
        Assert.Equal(40_000, skills[0].GetProperty("cdTotalMs").GetInt64());
        Assert.Equal(JsonValueKind.Null, skills[0].GetProperty("charges").ValueKind);
        Assert.Equal(JsonValueKind.Null, skills[0].GetProperty("chargesMax").ValueKind);
        Assert.Equal(JsonValueKind.Null, skills[0].GetProperty("toggleOn").ValueKind);
    }

    [Fact]
    public void Party_lists_members_without_self()
    {
        using var doc = JsonDocument.Parse(CharacterState.Json(Tracker(), 11_000));
        var party = doc.RootElement.GetProperty("party");
        Assert.Equal(1, party.GetArrayLength());

        var mate = party[0];
        Assert.Equal("SaitoHiraga", mate.GetProperty("name").GetString());
        Assert.Equal("templar", mate.GetProperty("cls").GetString());
        Assert.True(mate.GetProperty("alive").GetBoolean());
        Assert.Equal(15_000, mate.GetProperty("hp").GetInt64());
        Assert.Equal(21_000, mate.GetProperty("hpMax").GetInt64());
        Assert.Equal(JsonValueKind.Null, mate.GetProperty("inRange").ValueKind);
        Assert.Equal(JsonValueKind.Null, mate.GetProperty("distance").ValueKind);
        Assert.Equal(0, mate.GetProperty("debuffs").GetArrayLength());
    }

    [Fact]
    public void Unknown_values_are_null_and_a_dead_player_is_not_alive()
    {
        var t = Tracker();
        t.Process(new DeathEvent(2_000, Mate));
        using var doc = JsonDocument.Parse(CharacterState.Json(t, 11_000));
        Assert.False(doc.RootElement.GetProperty("party")[0].GetProperty("alive").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("party")[0].GetProperty("hp").GetInt64());
    }
}
