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
    public const ushort PartyRequest = 0x0797;   // 1 HP: someone asks to join your party
    public const ushort SelfPower = 0x5636;      // 1 HP: your combat power: u64 power, u64 (16 bytes)
    public const ushort ResourceList = 0x0B61;   // 1 HP: your tickets and energies (aether, Shugo keys, …)
    public const ushort ResourceUpdate = 0x0C61; // 1 HP: one of them changed
    public const ushort BuffList = 0x2A38;       // 1 HP: buffs and debuffs put on an actor (target, count × buff)
    public const ushort BuffRefresh = 0x2B38;    // 1 HP: one buff refreshed (target, buff, bool)
    public const ushort Move = 0x1B37;           // 1 HP: an entity moved: actor varint, 1–2 bytes, x y z f32 (cm), …
    public const ushort FieldBossList = 0x0191;  // a map's field bosses: alive since / back at (in-game map list)
    public const ushort CubeNear = 0x3836;       // 1 HP: hidden cube near you: [cube varint][you varint]
    public const ushort CubeSpawn = 0x3736;      // 1 HP: cube spawn record: [00 00 00][cube varint][8 × 00]
    public const ushort CubeInteract = 0x3A36;   // 1 HP: you start collecting: [cube][you][00 00 00][u32]
    public const ushort CubeFocus = 0x6D8D;      // 1 HP: the cube is in focus: [cube]
    public const ushort CubeGone = 0x058D;       // 1 HP: the cube is collected / gone: [cube]
    public const ushort CubeCollected = 0x3536;  // 1 HP: collection confirmed: [cube][04 03 00]

    public static string Name(ushort op) => op switch
    {
        Heartbeat => "Heartbeat",
        Move => "Move",
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
        PartyRequest => "PartyRequest",
        SelfPower => "SelfPower",
        ResourceList => "Resources",
        ResourceUpdate => "Resource",
        BuffList => "Buffs",
        BuffRefresh => "BuffRefresh",
        FieldBossList => "FieldBosses",
        CubeNear => "CubeNear",
        CubeSpawn => "CubeSpawn",
        CubeInteract => "CubeInteract",
        CubeFocus => "CubeFocus",
        CubeGone => "CubeGone",
        CubeCollected => "CubeCollected",
        _ => $"{op >> 8:x2} {op & 0xFF:x2}",
    };
}
