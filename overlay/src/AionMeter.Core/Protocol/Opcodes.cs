namespace AionMeter.Core.Protocol;

/// <summary>
/// Opcodes as the two bytes appear on the wire (first byte, second byte), written as <c>0xAABB</c> where AA is the
/// first byte. Values are for the post-June-2026 client (Global launched after that patch).
/// </summary>
public static class Opcodes
{
    public static ushort Of(byte first, byte second) => (ushort)(first << 8 | second);

    public const ushort Heartbeat = 0x0036;      // 8 data bytes, ~19/s
    public const ushort SelfInfo = 0x3336;       // own character
    public const ushort PlayerInfo = 0x4536;     // another player
    public const ushort PlayerInfoOld = 0x4436;  // pre-June-2026 player record
    public const ushort Spawn = 0x4136;          // NPC / summon / effect entity
    public const ushort Death = 0x4236;
    public const ushort MapLoad = 0x2136;
    public const ushort Teleport = 0x2336;
    public const ushort Cast = 0x0238;           // skill use announcement (players and effect entities)
    public const ushort Damage = 0x0438;
    public const ushort Dot = 0x0538;
    public const ushort RemainHp = 0x008D;
    public const ushort KillCredit = 0x048D;
    public const ushort BattleToggle = 0x218D;   // KR only, unverified on Global
    public const ushort HpMp = 0x1B92;
    public const ushort PartyRoster = 0x0297;
    public const ushort PlayerGear = 0x5036;     // 1 HP: another player's equipment, sent when you inspect them
    public const ushort PartyMember = 0x2392;    // 1 HP: a party member's status (name + server), every few seconds
    public const ushort PartyList = 0x0092;      // 1 HP: the party: every member, sent on joining, a change, a zone
    public const ushort PartyJoin = 0x0D92;      // 1 HP: one party member (joined, or their card)
    public const ushort PartyLeave = 0x0697;     // 1 HP: result u16, state u8 (2 = you left), u32
    public const ushort PartyGone = 0x1D97;      // 1 HP: 00 00 — the party is gone
    public const ushort FieldBossList = 0x0191;  // a map's field bosses: alive since / back at (in-game map list)

    public static string Name(ushort op) => op switch
    {
        Heartbeat => "Heartbeat",
        SelfInfo => "SelfInfo",
        PlayerGear => "PlayerGear",
        PlayerInfo or PlayerInfoOld => "PlayerInfo",
        Spawn => "Spawn",
        Death => "Death",
        MapLoad => "MapLoad",
        Teleport => "Teleport",
        Cast => "Cast",
        Damage => "Damage",
        Dot => "Dot",
        RemainHp => "RemainHp",
        KillCredit => "KillCredit",
        BattleToggle => "BattleToggle",
        HpMp => "HpMp",
        PartyRoster => "PartyRoster",
        PartyMember => "PartyMember",
        PartyList => "PartyList",
        PartyJoin => "PartyJoin",
        PartyLeave => "PartyLeave",
        PartyGone => "PartyGone",
        FieldBossList => "FieldBosses",
        _ => $"{op >> 8:x2} {op & 0xFF:x2}",
    };
}
