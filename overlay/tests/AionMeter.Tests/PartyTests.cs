using System.Text;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.Protocol;

namespace AionMeter.Tests;

/// <summary>1 HP: the party roster (02 97) and listing only you and your party in the open world.</summary>
public class PartyTests
{
    private static byte[] Hex(params string[] lines) =>
        string.Join(' ', lines).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16)).ToArray();

    // Real roster bytes (2026-08-15 capture, rearranged), from taengu/A2Tools-DPS-Meter's tests.
    private const string Header = "02 97 a5 f9 08 00 0b 38 35 30 4b 20 e8 bf 9e e5 88 b7 05 63 28 09 00 00 03 85 4b 01 00 00 00 f6 03 1f 02 00 05";
    private const string Slot1 = "0c 01 85 4b 01 00 00 00 f6 03 0f e4 b9 9d e5 b7 9e e4 be 9d e7 84 b6 e5 9c a8 06 00 00 00 32 00 00 00 07 17 00 00 f6 03 f6 03 04 38 39 0d 00 00 00 00 00 00 01 01";
    private static string Member(int slot) => $"0e {slot:x2} 62 64 01 00 00 00 f6 03 02 4d 37 0e 00 00 00 32 00 00 00 5e 16 00 00 f6 03 f6 03 04 6a 03 0d 00 00 00 00 00 00 01 01";
    private static string Vacant(int slot) => $"00 {slot:x2} 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 04 00 00 00 00 00 00 00 00 00 00 00 00";

    [Fact]
    public void Roster_reads_members_past_vacant_slots()
    {
        var data = Hex(Header, Slot1, Vacant(2), Vacant(3), Vacant(4), Member(5), "02 0e 00 36");
        var roster = PartyRoster.TryParse(data, 2);
        Assert.NotNull(roster);
        Assert.Equal([("九州依然在", 1), ("M7", 5)], roster!.Members.Select(m => (m.Name, m.Slot)));
        Assert.True(roster.Complete);
        Assert.Equal(1014, roster.Members[0].ServerId);
        Assert.Equal(50, roster.Members[0].Level);
    }

    [Fact]
    public void Roster_ends_at_the_vacant_slots_after_the_last_member()
    {
        var data = Hex(Header, Slot1, Member(2), Vacant(3), Vacant(4), Vacant(5));
        var roster = PartyRoster.TryParse(data, 2);
        Assert.Equal(["九州依然在", "M7"], roster!.Members.Select(m => m.Name));
        Assert.True(roster.Complete);
    }

    [Fact]
    public void Rosters_are_found_inside_other_bytes_and_the_parser_emits_them()
    {
        var data = Hex("11 22 33", Header, Slot1, Member(2), Vacant(3), Vacant(4), Vacant(5), "44 55");
        Assert.Single(PartyRoster.FindAll(data));

        var events = new List<GameEvent>();
        new PacketParser(GameData.Empty, events.Add).Handle(Opcodes.PartyRoster, data.AsSpan(5));
        var e = Assert.IsType<PartyRosterEvent>(Assert.Single(events));
        Assert.Equal(["九州依然在", "M7"], e.Names);
    }

    [Fact]
    public void Random_bytes_are_not_a_roster() =>
        Assert.Empty(PartyRoster.FindAll(Hex("02 97 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00")));

    // ---------------------------------------------------------------- the filter

    private const uint Me = 100, Mate = 101, Stranger = 102, Boss = 500;

    private static CombatTracker Fight(bool partyOnly, string[]? roster, bool dungeon = false)
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = partyOnly });
        if (dungeon) t.Process(new ZoneChangedEvent(0, 600091, "Ferocious Horn Den", IsDungeon: true));
        t.Process(new SelfIdentifiedEvent(0, Me, "Ilvane", 1304, GameClass.Elementalist));
        t.Process(new PlayerSeenEvent(0, Mate, "Borgrim", 1304, GameClass.Gladiator));
        t.Process(new PlayerSeenEvent(0, Stranger, "Passerby", 1304, GameClass.Ranger));
        t.Process(new NpcSeenEvent(0, Boss, 2000002, 50_000_000));
        if (roster is not null) t.Process(new PartyRosterEvent(0, roster, Complete: true));
        t.Process(new DamageEvent(1_000, Me, Boss, 16040000, 1_000_000, HitFlags.None));
        t.Process(new DamageEvent(2_000, Mate, Boss, 11020000, 3_000_000, HitFlags.None));
        t.Process(new DamageEvent(3_000, Stranger, Boss, 14020000, 6_000_000, HitFlags.None));
        return t;
    }

    private static string[] Names(CombatTracker t) => t.Snapshot(null, 3_000)!.Combatants.Select(c => c.Name).ToArray();

    [Fact]
    public void Open_world_lists_only_you_and_your_party()
    {
        var t = Fight(partyOnly: true, ["Ilvane", "Borgrim"]);
        var s = t.Snapshot(null, 3_000)!;
        Assert.Equal(["Borgrim", "Ilvane"], s.Combatants.Select(c => c.Name));
        Assert.Equal(4_000_000, s.TotalDamage); // the stranger's 6M is out of the total and the shares
        Assert.Equal(0.75, s.Combatants[0].Share, 3);
    }

    [Fact]
    public void On_your_own_only_you_are_listed() =>
        Assert.Equal(["Ilvane"], Names(Fight(partyOnly: true, roster: null)));

    [Fact]
    public void Leaving_the_party_puts_you_on_your_own()
    {
        var t = Fight(partyOnly: true, ["Ilvane", "Borgrim"]);
        t.Process(new PartyRosterEvent(3_000, ["Ilvane"], Complete: true));
        Assert.Equal(["Ilvane"], Names(t));
    }

    [Fact]
    public void A_partial_roster_never_drops_a_member()
    {
        var t = Fight(partyOnly: true, ["Ilvane", "Borgrim"]);
        t.Process(new PartyRosterEvent(3_000, ["Ilvane"], Complete: false));
        Assert.Equal(["Borgrim", "Ilvane"], Names(t));
    }

    [Fact]
    public void Instances_and_the_switch_list_everyone()
    {
        Assert.Equal(3, Names(Fight(partyOnly: true, roster: null, dungeon: true)).Length);
        Assert.Equal(3, Names(Fight(partyOnly: false, ["Ilvane", "Borgrim"])).Length);
    }

    [Fact]
    public void The_party_survives_a_meter_restart()
    {
        var cache = Fight(partyOnly: true, ["Ilvane", "Borgrim"]).ExportCache();
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        t.ImportCache(cache);
        Assert.Equal(["Ilvane", "Borgrim"], t.PartyNames.Order().Reverse());
    }

    [Fact]
    public void A_roster_without_you_is_someone_elses_party()
    {
        var t = Fight(partyOnly: true, ["Ilvane", "Borgrim"]);
        t.Process(new PartyRosterEvent(3_000, ["Passerby", "Stranger"], Complete: true));
        Assert.Equal(["Borgrim", "Ilvane"], Names(t));
        t.Process(new PartyRosterEvent(3_000, ["Passerby"], Complete: false));
        Assert.Equal(["Borgrim", "Ilvane"], Names(t));
    }

    [Fact]
    public void Unnamed_players_and_ownerless_pets_stay_out_even_when_a_party_member_has_not_hit()
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        t.Process(new SelfIdentifiedEvent(0, Me, "Ilvane", 1304, GameClass.Elementalist));
        t.Process(new NpcSeenEvent(0, Boss, 2000002, 50_000_000));
        t.Process(new PartyRosterEvent(0, ["Ilvane", "Healer"], Complete: true)); // the healer never hits
        t.Process(new DamageEvent(1_000, Me, Boss, 16040000, 1_000_000, HitFlags.None));
        t.Process(new DamageEvent(2_000, 777, Boss, 14020000, 6_000_000, HitFlags.None)); // "#777": a stranger not named yet
        Assert.Equal(["Ilvane"], Names(t));
    }

    [Fact]
    public void The_chip_can_tell_what_the_filter_does()
    {
        Assert.Equal(PartyFilterState.Party, Fight(partyOnly: true, ["Ilvane", "Borgrim"]).PartyStatus().State);
        Assert.Equal(PartyFilterState.Solo, Fight(partyOnly: true, roster: null).PartyStatus().State);
        Assert.Equal(PartyFilterState.Off, Fight(partyOnly: false, roster: null).PartyStatus().State);
        Assert.Equal(PartyFilterState.SelfUnknown,
            new CombatTracker(GameData.Empty, new MeterOptions { PartyOnly = true }).PartyStatus().State);
    }

    // ---------------------------------------------------------------- fights of players passing by

    private static CombatTracker World(bool partyOnly = true)
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = partyOnly });
        t.Process(new SelfIdentifiedEvent(0, Me, "Ilvane", 1304, GameClass.Elementalist));
        t.Process(new PlayerSeenEvent(0, Stranger, "Passerby", 1304, GameClass.Ranger));
        t.Process(new NpcSeenEvent(0, Boss, 2000002, 50_000_000));
        return t;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_fight_you_walk_past_is_not_shown_or_saved(bool partyOnly)
    {
        var t = World(partyOnly);
        var saved = new List<FightRecord>();
        t.EncounterFinished += saved.Add;
        t.Process(new DamageEvent(1_000, Stranger, Boss, 14020000, 6_000_000, HitFlags.None));
        t.Process(new DamageEvent(1_500, 777, Boss, 14020000, 1_000_000, HitFlags.None)); // not named yet
        Assert.Null(t.Snapshot(null, 2_000));
        Assert.Null(t.LiveFight());
        Assert.Empty(t.Segments());
        t.Reset();
        Assert.Empty(saved);
    }

    [Fact]
    public void Your_fight_starts_when_you_hit_not_when_the_passer_by_did()
    {
        var t = World(partyOnly: false);
        t.Process(new DamageEvent(1_000, 777, Boss, 14020000, 1_000_000, HitFlags.None));
        t.Process(new DamageEvent(20_000, 777, Boss, 14020000, 1_000_000, HitFlags.None)); // still "fighting" nearby
        t.Process(new DamageEvent(25_000, Me, Boss, 16040000, 2_000_000, HitFlags.None));
        var s = t.Snapshot(null, 25_000)!;
        Assert.Equal(["Ilvane"], s.Combatants.Select(c => c.Name));
        Assert.Equal(25_000, s.StartedAt.ToUnixTimeMilliseconds());
        Assert.NotNull(t.LiveFight());
    }

    [Fact]
    public void A_party_members_fight_is_yours()
    {
        var u = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        u.Process(new SelfIdentifiedEvent(0, Me, "Ilvane", 1304, GameClass.Elementalist));
        u.Process(new PlayerSeenEvent(0, Mate, "Borgrim", 1304, GameClass.Gladiator));
        u.Process(new PartyRosterEvent(0, ["Ilvane", "Borgrim"], Complete: true));
        u.Process(new DamageEvent(1_000, Mate, Boss, 11020000, 3_000_000, HitFlags.None));
        Assert.Equal(["Borgrim"], Names(u));
        Assert.NotNull(u.LiveFight());
    }

    [Fact]
    public void Combat_power_comes_from_the_roster()
    {
        var data = Hex(Header, Slot1, Member(2), Vacant(3), Vacant(4), Vacant(5));
        var events = new List<GameEvent>();
        new PacketParser(GameData.Empty, events.Add).Handle(Opcodes.PartyRoster, data.AsSpan(2));
        var e = Assert.IsType<PartyRosterEvent>(Assert.Single(events));
        Assert.NotNull(e.Powers);
        Assert.All(e.Names, n => Assert.True(e.Powers!.GetValueOrDefault(n) > 0));

        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        t.Process(new SelfIdentifiedEvent(0, Me, "Ilvane", 1304, GameClass.Elementalist));
        t.Process(new PlayerSeenEvent(0, Mate, "Borgrim", 1304, GameClass.Gladiator));
        t.Process(new PartyRosterEvent(0, ["Ilvane", "Borgrim"], true,
            new Dictionary<string, long> { ["Ilvane"] = 41_200, ["Borgrim"] = 38_900 }));
        t.Process(new DamageEvent(1_000, Me, Boss, 16040000, 1_000_000, HitFlags.None));
        t.Process(new DamageEvent(2_000, Mate, Boss, 11020000, 3_000_000, HitFlags.None));
        Assert.Equal(41_200, t.PowerOf("ilvane"));
        Assert.Equal([38_900L, 41_200L], t.Snapshot(null, 2_000)!.Combatants.Select(c => c.Power));

        var u = new CombatTracker(GameData.Empty, new MeterOptions());
        u.ImportCache(t.ExportCache());
        Assert.Equal(38_900, u.PowerOf("Borgrim"));
    }

    // ---------------------------------------------------------------- death recap (PvP)

    [Fact]
    public void Dying_to_a_player_names_the_killer_and_folds_in_their_spirit()
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        var recaps = new List<DeathRecap>();
        t.DeathRecapped += recaps.Add;
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new PlayerSeenEvent(0, Stranger, "Miranda", 0, GameClass.Elementalist));
        t.Process(new DamageEvent(1_000, Stranger, Me, 16040000, 520, HitFlags.Critical, 1, 10910));
        t.Process(new DamageEvent(1_100, Stranger, Me, 16800000, 10910, HitFlags.Critical)); // the scalar where damage goes
        t.Process(new DamageEvent(1_500, 18921, Me, 16000000, 1051, HitFlags.None, 1, 10910)); // her spirit
        t.Process(new DamageEvent(2_000, Stranger, Me, 16140000, 109, HitFlags.Dot));
        t.Process(new DeathEvent(2_400, Me));
        t.Process(new PlayerKilledEvent(2_400, Me, Stranger, "Miranda", 2309));

        var r = recaps[^1];
        Assert.Equal("Miranda", r.KillerName);
        Assert.Equal(2309, r.KillerServer);
        Assert.True(r.ByPlayer);
        var killer = Assert.Single(r.Attackers);
        Assert.Equal(GameClass.Elementalist, killer.Class);
        Assert.Equal(520 + 1051 + 109, killer.Damage);
        Assert.Equal(1_400, r.DurationMs);
        Assert.Same(r, t.LastDeath);
    }

    [Fact]
    public void An_inspected_players_gear_score_is_read()
    {
        // 50 36 header of an inspected player (2026-10-09 Abyss capture)
        var data = Hex("00 00 07 08 41 63 65 52 75 66 66 79 08 00 00 00 02 02 2d 00 00 00 00 00 00 00 7b 06 00 00 05 09 ed 0c 39 da 1a 5f 01 00");
        var events = new List<GameEvent>();
        new PacketParser(GameData.Empty, events.Add).Handle(Opcodes.PlayerGear, data);
        var e = Assert.IsType<PlayerGearEvent>(Assert.Single(events));
        Assert.Equal(("AceRuffy", 2309, 45, 1659), (e.Name, e.ServerId, e.Level, e.GearScore));

        var t = new CombatTracker(GameData.Empty, new MeterOptions());
        t.Process(e);
        Assert.Equal(1659, t.GearOf("aceruffy"));
    }

    [Fact]
    public void Party_power_and_gear_survive_a_restart_through_the_name_cache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"names-{Guid.NewGuid():N}.json");
        try
        {
            var t = Fight(partyOnly: true, null);
            t.Process(new PartyRosterEvent(3_000, ["Ilvane", "Borgrim"], true,
                new Dictionary<string, long> { ["Borgrim"] = 38_900 }, new Dictionary<string, int> { ["Borgrim"] = 1_480 }));
            t.Process(new PlayerGearEvent(3_000, "Passerby", 2309, 45, 1_659));
            new AionMeter.Core.Storage.NameCache(path).Save(t.ExportCache());

            var u = new CombatTracker(GameData.Empty, new MeterOptions { PartyOnly = true });
            u.ImportCache(new AionMeter.Core.Storage.NameCache(path).Load()!);
            Assert.Contains("Borgrim", u.PartyNames);
            Assert.Equal(38_900, u.PowerOf("Borgrim"));
            Assert.Equal(1_480, u.GearOf("Borgrim"));
            Assert.Equal(1_659, u.GearOf("Passerby"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_meter_started_mid_session_filters_by_party_and_finds_you_by_name()
    {
        // Restarted in the Abyss (an update): no login record, the party restored from the cache.
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true })
        {
            KnownSelfName = "Kunogarasu",
        };
        t.Process(new PartyRosterEvent(0, ["Kunogarasu", "HACPAHO"], Complete: true));
        t.Process(new PlayerSeenEvent(0, Mate, "HACPAHO", 1309, GameClass.Ranger));
        t.Process(new PlayerSeenEvent(0, Stranger, "Passerby", 2309, GameClass.Gladiator));
        t.Process(new NpcSeenEvent(0, Boss, 2000002, 50_000_000));
        t.Process(new DamageEvent(1_000, Mate, Boss, 14020000, 1_000_000, HitFlags.None));
        t.Process(new DamageEvent(1_100, Stranger, Boss, 11020000, 2_000_000, HitFlags.None));
        Assert.Equal(["HACPAHO"], Names(t)); // not everyone

        t.Process(new PlayerSeenEvent(1_500, Me, "Kunogarasu", 1309, GameClass.Unknown)); // your name in your own kill
        Assert.Equal("Kunogarasu", t.SelfName);
        t.Process(new DamageEvent(2_000, Me, Boss, 17010000, 500_000, HitFlags.None));
        Assert.Equal(["HACPAHO", "Kunogarasu"], Names(t));
        Assert.Equal(PartyFilterState.Party, t.PartyStatus().State);
    }

    [Fact]
    public void A_party_member_known_late_keeps_all_their_damage()
    {
        // Started mid-session in a party: the roster only comes when the party changes.
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new PlayerSeenEvent(0, Mate, "HACPAHO", 1309, GameClass.Ranger));
        t.Process(new NpcSeenEvent(0, Boss, 2000002, 50_000_000));
        t.Process(new DamageEvent(1_000, Me, Boss, 17010000, 1_000_000, HitFlags.None));
        t.Process(new DamageEvent(1_500, Mate, Boss, 14020000, 3_000_000, HitFlags.None)); // not known as party yet
        Assert.Equal(["Kunogarasu"], Names(t));
        t.Process(new PartyRosterEvent(2_000, ["Kunogarasu", "HACPAHO"], Complete: true));
        t.Process(new DamageEvent(2_500, Mate, Boss, 14020000, 1_000_000, HitFlags.None));
        var mate = t.Snapshot(null, 3_000)!.Combatants.Single(c => c.Name == "HACPAHO");
        Assert.Equal(4_000_000, mate.Damage);
    }

    [Fact]
    public void A_dead_mob_ends_the_fight_and_the_next_one_starts_its_own()
    {
        const uint mob1 = 600, mob2 = 601;
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        t.Process(new SelfIdentifiedEvent(0, Me, "Ilvane", 1304, GameClass.Elementalist));
        t.Process(new NpcSeenEvent(0, mob1, 2000002, 100_000));
        t.Process(new NpcSeenEvent(0, mob2, 2000003, 100_000));
        t.Process(new DamageEvent(1_000, Me, mob1, 16040000, 60_000, HitFlags.None));
        t.Process(new NpcHpEvent(1_000, mob1, 40_000, 100_000));
        t.Process(new NpcHpEvent(2_000, mob1, 0, 100_000));
        Assert.Equal(EncounterEndReason.Kill, t.Segments()[0].Reason);
        t.Process(new DamageEvent(2_500, Me, mob2, 16040000, 30_000, HitFlags.None));
        Assert.Equal(2, t.Segments().Count);
        Assert.True(t.Segments()[0].IsActive);
    }

    [Fact]
    public void While_no_roster_came_the_players_you_heal_are_your_party()
    {
        // A healer who does not hit: the party's fight still shows, with the people you heal.
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new PlayerSeenEvent(0, Mate, "HACPAHO", 1309, GameClass.Ranger));
        t.Process(new PlayerSeenEvent(0, Stranger, "Passerby", 1309, GameClass.Gladiator));
        t.Process(new NpcSeenEvent(0, Boss, 2000002, 50_000_000));
        t.Process(new HealEvent(500, Me, Mate, 17010000, 3_000, HitFlags.None));
        t.Process(new DamageEvent(1_000, Mate, Boss, 14020000, 2_000_000, HitFlags.None));
        t.Process(new DamageEvent(1_200, Stranger, Boss, 11020000, 2_000_000, HitFlags.None));
        Assert.NotNull(t.LiveFight());
        Assert.Equal(["HACPAHO"], Names(t));
        Assert.Equal(PartyFilterState.Party, t.PartyStatus().State);
    }

    [Fact]
    public void The_party_status_packet_names_the_party_without_a_roster_or_heals()
    {
        // 23 92 of a party member (2026-10-09 Abyss capture): HACPAHO, Hithanya
        var events = new List<GameEvent>();
        new PacketParser(GameData.Empty, events.Add).Handle(Opcodes.PartyMember, Hex("07 48 41 43 50 41 48 4f 1d 05 be 43 de 1f"));
        var e = Assert.IsType<PartyMemberEvent>(Assert.Single(events));
        Assert.Equal(("HACPAHO", 1309), (e.Name, e.ServerId));

        // A party member opens on a world boss before you hit: the whole fight is theirs and yours, a passer-by's is not.
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new PlayerSeenEvent(0, Mate, "HACPAHO", 1309, GameClass.Ranger));
        t.Process(new PlayerSeenEvent(0, Stranger, "Passerby", 1309, GameClass.Gladiator));
        t.Process(new NpcSeenEvent(0, Boss, 2000002, 50_000_000));
        t.Process(e with { TimeMs = 100 });
        t.Process(new HealEvent(200, Me, Stranger, 17010000, 3_000, HitFlags.None)); // an area heal on a passer-by
        t.Process(new DamageEvent(1_000, Mate, Boss, 14020000, 2_000_000, HitFlags.None));
        t.Process(new DamageEvent(1_200, Stranger, Boss, 11020000, 2_000_000, HitFlags.None));
        t.Process(new DamageEvent(1_500, Me, Boss, 17010000, 500_000, HitFlags.None));
        Assert.Equal(["HACPAHO", "Kunogarasu"], Names(t));
        Assert.Equal(2_000_000, t.Snapshot(null, 3_000)!.Combatants.First(c => c.Name == "HACPAHO").Damage);
        Assert.Equal(PartyFilterState.Party, t.PartyStatus().State);

        // Silent for five minutes: left the party.
        t.Tick(100 + CombatTracker.PartyMemberTimeoutMs + 1);
        Assert.Equal(PartyFilterState.Solo, t.PartyStatus().State);
    }

    [Fact]
    public void Someone_invited_mid_fight_is_added_by_hand_with_all_their_damage()
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new PlayerSeenEvent(0, Mate, "HACPAHO", 1309, GameClass.Ranger));
        t.Process(new NpcSeenEvent(0, Boss, 2000002, 50_000_000));
        t.Process(new DamageEvent(1_000, Me, Boss, 17010000, 500_000, HitFlags.None));
        t.Process(new DamageEvent(1_200, Mate, Boss, 14020000, 2_000_000, HitFlags.None)); // not in the party yet
        Assert.Equal(["Kunogarasu"], Names(t));
        Assert.False(t.IsPartyMember(Mate));
        Assert.Null(t.IsPartyMember(Me));

        Assert.True(t.SetPartyMember(Mate, true));
        Assert.Equal(["HACPAHO", "Kunogarasu"], Names(t));
        Assert.True(t.IsPartyMember(Mate));

        t.SetPartyMember(Mate, false);
        Assert.Equal(["Kunogarasu"], Names(t));
    }

    [Fact]
    public void A_player_with_no_name_yet_is_added_by_id()
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new NpcSeenEvent(0, Boss, 2000002, 50_000_000));
        t.Process(new DamageEvent(1_000, Me, Boss, 17010000, 500_000, HitFlags.None));
        t.Process(new DamageEvent(1_200, 777, Boss, 14020000, 2_000_000, HitFlags.None)); // "#777"
        Assert.Single(Names(t));
        Assert.False(t.IsPartyMember(777));
        Assert.True(t.SetPartyMember(777, true));
        Assert.Equal(2, Names(t).Length);
    }

    // ---------------------------------------------------------------- the game's party packets

    private static byte[] Str(string text) => [(byte)Encoding.UTF8.GetByteCount(text), .. Encoding.UTF8.GetBytes(text)];

    /// <summary>A 00 92 body: no optional blocks, the members, then the last byte (the members' bools share one byte).</summary>
    private static byte[] PartyList(params (uint Id, string Name, int Slot)[] members)
    {
        var b = new List<byte> { 0 };
        b.AddRange(new byte[8 + 8 + 4 + 4]);
        b.Add((byte)members.Length);
        var first = true;
        foreach (var (id, name, slot) in members)
        {
            b.Add(0);
            b.Add((byte)slot);
            b.AddRange(BitConverter.GetBytes(id));
            b.AddRange(BitConverter.GetBytes((ushort)1309));
            b.AddRange(BitConverter.GetBytes((ushort)1309));
            b.Add(0); // string
            b.AddRange(new byte[8]);
            b.AddRange(Str(name));
            b.AddRange(new byte[4]);
            b.Add(0); // varint
            b.Add(0); // varint
            b.AddRange(new byte[24 + 8 + 4 + 12]);
            if (first) b.Add(0); // the byte all the bools come from
            b.AddRange(new byte[12]);
            b.AddRange(new byte[8 + 1]);
            first = false;
        }
        b.Add(0);
        return b.ToArray();
    }

    /// <summary>A 0D 92 body: kind 3, slot, id, server twice, a 36-character uuid, 8 bytes, the name.</summary>
    private static byte[] PartyJoin(uint id, string name, int slot)
    {
        var b = new List<byte> { 3, (byte)slot };
        b.AddRange(BitConverter.GetBytes(id));
        b.AddRange(BitConverter.GetBytes((ushort)1309));
        b.AddRange(BitConverter.GetBytes((ushort)1309));
        b.Add(36);
        b.AddRange(Encoding.ASCII.GetBytes(new string('a', 36)));
        b.AddRange(new byte[8]);
        b.AddRange(Str(name));
        b.AddRange(new byte[20]);
        return b.ToArray();
    }

    [Fact]
    public void The_party_list_and_a_member_joining_are_read()
    {
        var events = new List<GameEvent>();
        var parser = new PacketParser(GameData.Empty, events.Add);
        parser.Handle(Opcodes.PartyList, PartyList((Me, "Kunogarasu", 1), (Mate, "HACPAHO", 2)));
        var list = Assert.IsType<PartyListEvent>(events.Last());
        Assert.Equal(["Kunogarasu", "HACPAHO"], list.Members.Select(m => m.Name));
        Assert.Equal(Mate, list.Members[1].ActorId);

        events.Clear();
        parser.Handle(Opcodes.PartyJoin, PartyJoin(Stranger, "PensionerPro", 3));
        var join = Assert.IsType<PartyJoinEvent>(events.Last());
        Assert.Equal(("PensionerPro", Stranger, 1309), (join.Member.Name, join.Member.ActorId, join.Member.ServerId));
        Assert.Contains(events, e => e is PlayerSeenEvent { Name: "PensionerPro" });

        events.Clear();
        parser.Handle(Opcodes.PartyLeave, [0, 0, 2, 0, 0, 0, 0]);
        Assert.Single(events.OfType<PartyLeftEvent>());
        Assert.True(Assert.Single(events.OfType<PartyPacketEvent>()).Read); // and the raw packet for the log
    }

    [Fact]
    public void Someone_who_joins_mid_fight_counts_with_the_whole_fight_and_leaving_ends_the_party()
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new PlayerSeenEvent(0, Mate, "HACPAHO", 1309, GameClass.Ranger));
        t.Process(new PlayerSeenEvent(0, Stranger, "PensionerPro", 1309, GameClass.Ranger));
        t.Process(new NpcSeenEvent(0, Boss, 2000002, 50_000_000));
        t.Process(new PartyListEvent(0, [new(Me, "Kunogarasu", 1309, 1), new(Mate, "HACPAHO", 1309, 2)]));
        t.Process(new DamageEvent(1_000, Me, Boss, 17010000, 500_000, HitFlags.None));
        t.Process(new DamageEvent(1_100, Mate, Boss, 14020000, 1_000_000, HitFlags.None));
        t.Process(new DamageEvent(1_200, Stranger, Boss, 14020000, 2_000_000, HitFlags.None)); // not in the party yet
        Assert.Equal(["HACPAHO", "Kunogarasu"], Names(t));

        t.Process(new PartyJoinEvent(1_500, new(Stranger, "PensionerPro", 1309, 3)));
        Assert.Equal(["PensionerPro", "HACPAHO", "Kunogarasu"], Names(t));

        t.Process(new PartyLeftEvent(2_000));
        Assert.Equal(["Kunogarasu"], Names(t));
        Assert.Equal(PartyFilterState.Solo, t.PartyStatus().State);
    }

    [Fact]
    public void The_boss_forecast_gives_the_kill_at_this_pace_and_the_enrage()
    {
        var data = GameData.Empty;
        data.Enrage[2000002] = 300;
        var t = new CombatTracker(data, new MeterOptions { TargetMode = TargetMode.All });
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new NpcSeenEvent(0, Boss, 2000002, 10_000_000));
        // 10 s at ~100k/s with 8–9M HP left: about 80 s to the kill, 290 s to the enrage
        for (var s = 1; s <= 11; s++) t.Process(new DamageEvent(s * 1_000, Me, Boss, 17010000, 100_000, HitFlags.None));
        t.Process(new NpcHpEvent(11_000, Boss, 8_900_000, 10_000_000, IsNpc: true));
        var f = t.Snapshot(null, 11_000)!.Boss!.Forecast!;
        Assert.InRange(f.KillSeconds!.Value, 60, 100);
        Assert.Equal(290, f.EnrageLeftSeconds!.Value, 1);
        Assert.False(f.Late);
        Assert.InRange(f.RequiredDps!.Value, 25_000, 31_000);
    }

    [Fact]
    public void A_party_member_dying_is_announced_once_and_a_stranger_is_not()
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        var died = new List<string>();
        t.PartyMemberDied += died.Add;
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new PlayerSeenEvent(0, Mate, "HACPAHO", 1309, GameClass.Ranger));
        t.Process(new PlayerSeenEvent(0, Stranger, "Passerby", 1309, GameClass.Gladiator));
        t.Process(new PartyListEvent(0, [new(Me, "Kunogarasu", 1309, 1), new(Mate, "HACPAHO", 1309, 2)]));
        t.Process(new DeathEvent(1_000, Mate, false));
        t.Process(new DeathEvent(1_500, Mate, false)); // the same death sent twice
        t.Process(new DeathEvent(2_000, Stranger, false));
        t.Tick(2_000);
        Assert.Equal(["HACPAHO"], died);
    }

    [Fact]
    public void Buffs_are_read_kept_until_they_run_out_and_removed()
    {
        // 2A 38: target 100, one buff: flags 1 (caster), instance 7, code 147300071, 60 s, expires, caster 100, level 1, not removing
        var body = new List<byte> { 100, 1, 1, 7 };
        body.AddRange(BitConverter.GetBytes(147300071));
        body.AddRange(BitConverter.GetBytes(60_000L));
        body.AddRange(BitConverter.GetBytes(0L));
        body.AddRange([100, 1, 0]);
        var events = new List<GameEvent>();
        new PacketParser(GameData.Empty, events.Add) { TimeMs = 1_000 }.Handle(Opcodes.BuffList, body.ToArray());
        var buff = Assert.IsType<BuffEvent>(Assert.Single(events));
        Assert.Equal((100u, 147300071, 60_000L, false), (buff.TargetId, buff.Code, buff.DurationMs, buff.Removing));

        var data = GameData.Empty;
        data.Buffs[147300071] = ("Wind Vigor", false);
        var t = new CombatTracker(data, new MeterOptions());
        t.Process(new SelfIdentifiedEvent(0, 100, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(buff);
        var on = Assert.Single(t.SelfBuffs(31_000));
        Assert.Equal(("Wind Vigor", 30_000L), (on.Name, on.LeftMs(31_000)));
        Assert.Empty(t.SelfBuffs(70_000)); // ran out
        t.Process(buff with { TimeMs = 80_000 });
        t.Process(buff with { TimeMs = 81_000, Removing = true });
        Assert.Empty(t.SelfBuffs(82_000));
    }

    [Fact]
    public void The_party_long_cooldowns_are_listed_until_ready()
    {
        var data = GameData.Empty;
        data.Skills[14220010] = "Bow of Blessing";
        var t = new CombatTracker(data, new MeterOptions { PartyOnly = true });
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new PlayerSeenEvent(0, Mate, "PensionerPro", 1309, GameClass.Ranger));
        t.Process(new PartyListEvent(0, [new(Me, "Kunogarasu", 1309, 1), new(Mate, "PensionerPro", 1309, 2)]));
        t.Process(new SkillCooldownEvent(1_000, Mate, 14220010, 115_800));
        t.Process(new SkillCooldownEvent(1_000, Mate, 14050140, 4_800));  // short: left out
        t.Process(new SkillCooldownEvent(1_000, Me, 17430020, 55_950));   // yours: left out
        var cd = Assert.Single(t.PartyCooldowns(11_000));
        Assert.Equal(("PensionerPro", "Bow of Blessing", 105_800L), (cd.Player, cd.Skill, cd.LeftMs));
        Assert.Empty(t.PartyCooldowns(120_000));
    }

    [Fact]
    public void A_party_request_names_the_class_level_and_combat_power()
    {
        var body = new List<byte>(new byte[10]);
        body.AddRange(BitConverter.GetBytes((ushort)2309));
        body.AddRange(new byte[6]);
        body.AddRange(BitConverter.GetBytes(30u));  // job: Cleric
        body.AddRange(BitConverter.GetBytes(45u));  // level
        body.AddRange(new byte[4]);
        body.AddRange(Str("Miranda"));
        body.AddRange(new byte[6]);
        body.AddRange(BitConverter.GetBytes(4_321_000u));
        body.AddRange(new byte[4]);
        var events = new List<GameEvent>();
        new PacketParser(GameData.Empty, events.Add).Handle(Opcodes.PartyRequest, body.ToArray());
        var r = Assert.IsType<PartyRequestEvent>(Assert.Single(events));
        Assert.Equal(("Miranda", 2309, GameClass.Cleric, 45, 4_321_000L), (r.Name, r.ServerId, r.Class, r.Level, r.Power));
    }

    [Fact]
    public void Tickets_and_energies_are_read()
    {
        // 0C 61 from the 2026-10-09 capture: ticket 12 now 14
        var events = new List<GameEvent>();
        new PacketParser(GameData.Empty, events.Add).Handle(Opcodes.ResourceUpdate, Hex("00 04 0c 00 00 00 0e 02"));
        var r = Assert.IsType<ResourceEvent>(Assert.Single(events));
        Assert.Equal((12, 14, 0), (r.Id, r.Count, r.Bonus));

        var t = new CombatTracker(GameData.Empty, new MeterOptions());
        t.Process(new ResourceEvent(0, ResourceEvent.Aether, 195, 80));
        Assert.Equal((195, 80), t.Resources()[ResourceEvent.Aether]);
    }

    [Theory]
    [InlineData(1500, 2600, 0, 0, ThreatLevel.Danger, true)]     // gear score 1100 above
    [InlineData(1500, 2300, 0, 0, ThreatLevel.Caution, true)]    // 800 above
    [InlineData(1500, 1900, 0, 0, ThreatLevel.Even, true)]
    [InlineData(1500, 400, 0, 0, ThreatLevel.Weak, true)]        // 1100 below
    [InlineData(0, 1659, 62_000, 77_632, ThreatLevel.Danger, false)] // your gear score unknown: combat power
    [InlineData(0, 0, 62_000, 70_478, ThreatLevel.Caution, false)]
    [InlineData(0, 0, 62_000, 49_616, ThreatLevel.Weak, false)]
    [InlineData(0, 0, 0, 70_000, ThreatLevel.Unknown, false)]
    public void Opponents_are_coloured_by_the_gap_to_you(long myGear, long theirGear, long myPower, long theirPower, ThreatLevel level, bool byGear)
    {
        var t = PvpThreat.Assess(myGear, theirGear, myPower, theirPower, new ThreatThresholds());
        Assert.Equal(level, t.Level);
        if (level != ThreatLevel.Unknown) Assert.Equal(byGear, t.ByGear);
    }

    [Fact]
    public void Combat_power_comes_with_you_and_with_players_in_view()
    {
        // 56 36 from the 2026-10-09 capture: 72,703
        var events = new List<GameEvent>();
        new PacketParser(GameData.Empty, events.Add).Handle(Opcodes.SelfPower, Hex("ff 1b 01 00 00 00 00 00 ff 1b 01 00 00 00 00 00"));
        Assert.Equal(72_703, Assert.IsType<SelfPowerEvent>(Assert.Single(events)).Power);

        var t = new CombatTracker(GameData.Empty, new MeterOptions());
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new SelfPowerEvent(0, 62_000));
        t.Process(new PlayerPowerEvent(0, Stranger, "AceRuffy", 77_632));
        Assert.Equal(77_632, t.PowerOf("AceRuffy"));
        Assert.Equal(ThreatLevel.Danger, t.ThreatOf("AceRuffy", new ThreatThresholds()).Level);
    }

    [Fact]
    public void A_pvp_session_lists_opponents_with_damage_both_ways_hp_and_kills()
    {
        var t = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        t.Process(new SelfIdentifiedEvent(0, Me, "Kunogarasu", 1309, GameClass.Cleric));
        t.Process(new PlayerSeenEvent(0, Stranger, "AceRuffy", 2309, GameClass.Gladiator));
        t.Process(new DamageEvent(1_000, Stranger, Me, 11020000, 1_600, HitFlags.None, 1, 12_000));
        t.Process(new DamageEvent(1_200, Me, Stranger, 17010000, 900, HitFlags.None, 1, 14_000));
        t.Process(new DamageEvent(1_300, Me, Stranger, 17020000, 14_000, HitFlags.Critical)); // the scalar where damage goes
        t.Process(new NpcHpEvent(1_400, Stranger, 20_000, 0, IsNpc: false));
        t.Process(new NpcHpEvent(1_500, Stranger, 15_000, 0, IsNpc: false));
        t.Process(new PlayerKilledEvent(2_000, Stranger, Me, "Kunogarasu", 1309));

        var s = t.Pvp(2_000)!;
        var o = Assert.Single(s.Opponents);
        Assert.Equal(("AceRuffy", 900L, 1_600L), (o.Name, o.Dealt, o.Taken));
        Assert.True(o.Defeated);
        Assert.Equal(1, s.Kills);
        Assert.Equal(0L, o.Hp);

        var ended = new List<PvpSession>();
        t.PvpSessionEnded += ended.Add;
        t.Tick(2_000 + PvpTracker_Gap + 1);
        Assert.Single(ended);
        Assert.Null(t.Pvp(2_000 + PvpTracker_Gap + 2));
    }

    private const long PvpTracker_Gap = 60_000;

    [Fact]
    public void A_dungeon_run_keeps_its_fights_deaths_and_members_until_you_leave()
    {
        var data = GameData.Empty;
        data.Npcs[2300243] = new NpcDef(2300243, "Fire Temple Boss", IsBoss: true, IsDummy: false);
        var t = new CombatTracker(data, new MeterOptions { TargetMode = TargetMode.All, PartyOnly = true });
        var runs = new List<DungeonRun>();
        t.DungeonRunEnded += runs.Add;
        t.Process(new SelfIdentifiedEvent(0, Me, "Ilvane", 1304, GameClass.Elementalist));
        t.Process(new ZoneChangedEvent(0, 600021, "Fire Temple", IsDungeon: true));
        t.Process(new PlayerSeenEvent(0, Mate, "Borgrim", 1304, GameClass.Gladiator));
        t.Process(new NpcSeenEvent(0, Boss, 2300243, 1_000_000));
        t.Process(new DamageEvent(10_000, Me, Boss, 16040000, 600_000, HitFlags.None));
        t.Process(new DamageEvent(11_000, Mate, Boss, 11020000, 400_000, HitFlags.None));
        t.Process(new DeathEvent(12_000, Mate));
        t.Process(new NpcHpEvent(20_000, Boss, 0, 1_000_000));
        Assert.Equal(1, t.CurrentRun(25_000)!.BossesKilled);
        t.Process(new ZoneChangedEvent(60_000, 20, "Open world", IsDungeon: false));

        var run = Assert.Single(runs);
        Assert.Equal("Fire Temple", run.Zone);
        Assert.True(run.Cleared);
        Assert.Equal(20_000, run.ClearMs);
        Assert.Equal(1, run.Deaths);
        Assert.Equal(["Ilvane", "Borgrim"], run.Members.Select(m => m.Name));
        Assert.Equal(1, run.Members.Single(m => m.Name == "Borgrim").Deaths);
        Assert.Null(t.CurrentRun(61_000));
    }
}

public class PartyFrameTests
{
    [Fact]
    public void YouFirstThenThePartyWithHpBuffsAndCooldowns()
    {
        var t = new AionMeter.Core.Combat.CombatTracker(AionMeter.Core.Game.GameData.Empty, new AionMeter.Core.Combat.MeterOptions());
        t.Process(new AionMeter.Core.Events.SelfIdentifiedEvent(0, 100, "Ilvane", 1304, AionMeter.Core.Events.GameClass.Cleric));
        t.Process(new AionMeter.Core.Events.PlayerSeenEvent(0, 200, "Borgrim", 1304, AionMeter.Core.Events.GameClass.Gladiator));
        t.Process(new AionMeter.Core.Events.PartyRosterEvent(0, ["Ilvane", "Borgrim", "Faraway"], Complete: true));
        t.Process(new AionMeter.Core.Events.NpcHpEvent(1_000, 200, 4_500, 9_000, IsNpc: false));
        t.Process(new AionMeter.Core.Events.SkillCooldownEvent(1_000, 200, 11_000_000, 120_000));
        t.Process(new AionMeter.Core.Events.SkillCooldownEvent(1_000, 200, 11_010_000, 5_000)); // short: not shown for the party
        t.Process(new AionMeter.Core.Events.SkillCooldownEvent(1_000, 100, 17_000_000, 8_000)); // yours: shown from 1 s

        var rows = t.PartyFrame(2_000);
        Assert.Equal(["Ilvane", "Borgrim", "Faraway"], rows.Select(r => r.Name));
        Assert.True(rows[0].IsSelf);
        Assert.Single(rows[0].Cooldowns);
        Assert.Equal(0.5, rows[1].HpFraction, 3);
        Assert.Single(rows[1].Cooldowns);
        Assert.Equal(119_000, rows[1].Cooldowns[0].LeftMs);
        Assert.Null(rows[2].ActorId); // not seen yet: a row without HP
        Assert.Equal(-1, rows[2].HpFraction);

        t.Process(new AionMeter.Core.Events.DeathEvent(3_000, 200));
        Assert.Equal(0, t.PartyFrame(3_500)[1].HpFraction);
    }
}
