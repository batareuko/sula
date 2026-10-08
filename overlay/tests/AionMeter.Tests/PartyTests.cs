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
}
