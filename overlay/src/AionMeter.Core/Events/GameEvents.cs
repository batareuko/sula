namespace AionMeter.Core.Events;

/// <summary>Quality flags of a single hit, normalised from whatever the wire format uses.</summary>
[Flags]
public enum HitFlags : ushort
{
    None = 0,
    Critical = 1 << 0,
    Back = 1 << 1,
    Front = 1 << 2,
    Perfect = 1 << 3,
    Heavy = 1 << 4,      // "Smite" in AionFlex terms
    Multi = 1 << 5,
    Block = 1 << 6,
    Parry = 1 << 7,
    Endure = 1 << 8,     // Iron Wall
    Evade = 1 << 9,
    Dot = 1 << 10,
    Restore = 1 << 11,
    Double = 1 << 12,
}

public enum GameClass : byte
{
    Unknown = 0,
    Gladiator,
    Templar,
    Ranger,
    Assassin,
    Sorcerer,
    Elementalist,
    Cleric,
    Chanter,
    Brawler,
}

/// <summary>Everything the combat engine consumes. Time is Unix milliseconds taken from the packet capture.</summary>
public abstract record GameEvent(long TimeMs);

/// <param name="Damage">Total damage of the record, additional hits included.</param>
/// <param name="HitCount">1 + number of additional hits carried by the record.</param>
/// <param name="PowerScalar">The actor's damage multiplier in 1/100 % (players 16000–22000, mobs 10000); 0 if absent.
/// Summons inherit their owner's value, which lets unannounced summons be merged into the right player.</param>
public sealed record DamageEvent(
    long TimeMs,
    uint SourceId,
    uint TargetId,
    int SkillCode,
    long Damage,
    HitFlags Flags,
    int HitCount = 1,
    int PowerScalar = 0) : GameEvent(TimeMs);

public sealed record HealEvent(long TimeMs, uint SourceId, uint TargetId, int SkillCode, long Amount, HitFlags Flags) : GameEvent(TimeMs);

/// <summary>The local player's own character.</summary>
public sealed record SelfIdentifiedEvent(long TimeMs, uint ActorId, string Name, int ServerId, GameClass Class, int Level = 0) : GameEvent(TimeMs);

public sealed record PlayerSeenEvent(long TimeMs, uint ActorId, string Name, int ServerId, GameClass Class) : GameEvent(TimeMs);

/// <param name="Name">Display name when not resolvable from the NPC tables (demo / tests).</param>
/// <param name="X">Position where it came into view (0 when unknown); Y, Z likewise.</param>
public sealed record NpcSeenEvent(long TimeMs, uint ActorId, int NpcCode, long MaxHp = 0, string? Name = null,
    float X = 0, float Y = 0, float Z = 0) : GameEvent(TimeMs);

/// <summary>
/// A pet / spirit / totem / skill entity. Owner is known by id, only by the character name it carries, or not at all
/// (both empty) — it is still never a player.
/// </summary>
public sealed record SummonSeenEvent(long TimeMs, uint ActorId, uint OwnerId, string? OwnerName, int NpcCode) : GameEvent(TimeMs);

/// <summary>HP update. <paramref name="MaxHp"/> is 0 when the packet does not carry it. <paramref name="IsNpc"/> is
/// true when the record itself says the entity is an NPC; otherwise it may be a player and only updates known NPCs.</summary>
public sealed record NpcHpEvent(long TimeMs, uint ActorId, long CurrentHp, long MaxHp, bool IsNpc = true) : GameEvent(TimeMs);

/// <summary>1 HP: a player's mana (<c>00 8D</c>, resource 1) — the packet carries the current value only.</summary>
public sealed record PlayerMpEvent(long TimeMs, uint ActorId, long Mp) : GameEvent(TimeMs);

/// <summary>
/// 1 HP: a player's HP and MP with their maxima, from the character record (<c>33 36</c> for you, <c>45 36</c> for
/// another player): <c>[hp varint][hpMax varint][mp u32][mpMax u32]</c>. The record is sent when the character enters
/// the game, so this is the only place the maxima come from — the HP updates (00 8D) carry just the current values.
/// </summary>
public sealed record PlayerVitalsEvent(long TimeMs, uint ActorId, long Hp, long HpMax, long Mp, long MpMax) : GameEvent(TimeMs);

/// <summary>1 HP: an entity moved to x, y, z (centimetres; packet 1b 37).</summary>
public sealed record MoveEvent(long TimeMs, uint ActorId, float X, float Y, float Z) : GameEvent(TimeMs);

/// <summary>An entity died, or (<paramref name="AlreadyDead"/>) came into view already dead.</summary>
public sealed record DeathEvent(long TimeMs, uint ActorId, bool AlreadyDead = false) : GameEvent(TimeMs);

/// <summary>One field boss in the map's boss list: alive since <paramref name="AtMs"/>, or back at <paramref name="AtMs"/>.</summary>
/// <param name="SlotId">Map id × 100 + the boss's place in the list (the map's bosses sorted by NPC code).</param>
public sealed record FieldBossSlot(int SlotId, bool Alive, long AtMs, float X = 0, float Y = 0, float Z = 0);

/// <summary>
/// The field boss list of a map (<c>01 91</c>), sent while the in-game map shows it — the respawn timers straight from
/// the server. <paramref name="Count"/> is what the header announces; <paramref name="Slots"/> may stop short of it when
/// a slot could not be read (<see cref="Complete"/> is then false and <paramref name="Raw"/> helps work out why).
/// <paramref name="ServerId"/> is the local character's server (set by the tracker): every server runs its own timers.
/// </summary>
public sealed record FieldBossListEvent(long TimeMs, int MapId, int Count, IReadOnlyList<FieldBossSlot> Slots, byte[] Raw, int ServerId = 0)
    : GameEvent(TimeMs)
{
    public bool Complete => Slots.Count == Count;
}

/// <summary>
/// A skill use announcement (<c>02 38</c>). <paramref name="SkillCode"/> is the raw variant (e.g. 15280140): an effect
/// entity's own ticks carry its caster's variant with a different last digit, which is how it is tied to its owner.
/// </summary>
public sealed record CastEvent(long TimeMs, uint ActorId, uint TargetId, int SkillCode) : GameEvent(TimeMs);

/// <summary>Server-side combat state toggle for an NPC (boss engaged / disengaged). KR only so far.</summary>
public sealed record BattleStateEvent(long TimeMs, uint ActorId, bool InCombat) : GameEvent(TimeMs);

/// <summary>A map load. <paramref name="IsTeleport"/> is true for a re-load inside the same map.</summary>
public sealed record ZoneChangedEvent(long TimeMs, int MapId, string ZoneName, bool IsDungeon, bool IsTeleport = false) : GameEvent(TimeMs);

/// <summary>
/// 1 HP: the party roster (02 97), sent on every party change. <see cref="Complete"/>: every member the packet declared
/// was read, so the list replaces the previous one (one name = on your own again). <see cref="Powers"/>: each member's
/// combat power by name, as the roster states it.
/// </summary>
/// <summary>
/// 1 HP: the server's kill notice (<c>04 8D</c>) naming who killed whom — for a player killed by another player it
/// carries the killer's id, server and name.
/// </summary>
public sealed record PlayerKilledEvent(long TimeMs, uint VictimId, uint KillerId, string KillerName, int KillerServer) : GameEvent(TimeMs);

/// <summary>1 HP: an inspected player's header (<c>50 36</c>): level and gear score (the number the game shows).</summary>
public sealed record PlayerGearEvent(long TimeMs, string Name, int ServerId, int Level, int GearScore) : GameEvent(TimeMs);

/// <summary>1 HP: <c>23 92</c> — a party member's status (name, server); only your party's members are sent.</summary>
public sealed record PartyMemberEvent(long TimeMs, string Name, int ServerId) : GameEvent(TimeMs);

/// <summary>
/// 1 HP: a buff or debuff on <paramref name="TargetId"/> (<c>2A 38</c>, <c>2B 38</c>): its code (9 digits, the skill
/// code and a digit), who put it on, how long it lasts (-1 = until removed) and whether it is going.
/// </summary>
public sealed record BuffEvent(long TimeMs, uint TargetId, uint CasterId, int Code, long DurationMs, int InstanceId, bool Removing) : GameEvent(TimeMs);

/// <summary>1 HP: your own combat power (<c>56 36</c>).</summary>
public sealed record SelfPowerEvent(long TimeMs, long Power) : GameEvent(TimeMs);

/// <summary>1 HP: a player's combat power, sent with their record when they come into view (<c>45 36</c>).</summary>
public sealed record PlayerPowerEvent(long TimeMs, uint ActorId, string Name, long Power) : GameEvent(TimeMs);

/// <summary>1 HP: <c>0B 61</c> / <c>0C 61</c> — one of your tickets or energies: how many, plus the bonus ones.</summary>
public sealed record ResourceEvent(long TimeMs, int Id, int Count, int Bonus) : GameEvent(TimeMs)
{
    public const int Aether = 60000001, ShugoKeys = 1, Nightmare = 4, Invasion = 8, Awakening = 80000001;
}

/// <summary>1 HP: <c>07 97</c> — a player asks to join your party: name, server, class, level, combat power.</summary>
public sealed record PartyRequestEvent(long TimeMs, string Name, int ServerId, GameClass Class, int Level, long Power) : GameEvent(TimeMs);

/// <summary>1 HP: a cast started its skill's cooldown (<c>02 38</c>; the game sends it for you and your party).</summary>
/// <param name="Charges">Charges the game sent with the cooldown (a charge skill); -1 when the packet carried none.</param>
public sealed record SkillCooldownEvent(long TimeMs, uint ActorId, int SkillCode, long CooldownMs, int Charges = -1) : GameEvent(TimeMs);

/// <summary>1 HP diagnostics: a party packet as it came (hex, cut short) and whether it was read — for the log.</summary>
public sealed record PartyPacketEvent(long TimeMs, ushort Opcode, string Hex, bool Read) : GameEvent(TimeMs);

/// <summary>1 HP: a party member as the party packets name them: id 0 while out of view.</summary>
public sealed record PartyMemberInfo(uint ActorId, string Name, int ServerId, int Slot);

/// <summary>1 HP: <c>00 92</c> — the whole party (you among them).</summary>
public sealed record PartyListEvent(long TimeMs, IReadOnlyList<PartyMemberInfo> Members) : GameEvent(TimeMs);

/// <summary>1 HP: <c>0D 92</c> — one member: someone who joined, or a member's card.</summary>
public sealed record PartyJoinEvent(long TimeMs, PartyMemberInfo Member) : GameEvent(TimeMs);

/// <summary>1 HP: <c>06 97</c> / <c>1D 97</c> — you left the party, or it is gone.</summary>
public sealed record PartyLeftEvent(long TimeMs) : GameEvent(TimeMs);

public sealed record PartyRosterEvent(long TimeMs, IReadOnlyList<string> Names, bool Complete,
    IReadOnlyDictionary<string, long>? Powers = null, IReadOnlyDictionary<string, int>? Gear = null) : GameEvent(TimeMs);

/// <summary>1 HP: what a Hidden Cube packet means.</summary>
public enum CubePhase
{
    /// <summary><c>38 36</c>: the cube came near you (carries your actor id too).</summary>
    Near,
    /// <summary><c>37 36</c>: the cube's spawn record.</summary>
    SpawnRecord,
    /// <summary><c>3a 36</c>: you started collecting the cube.</summary>
    Interact,
    /// <summary><c>6d 8d</c>: the cube is in focus.</summary>
    Focus,
    /// <summary><c>05 8d</c>: the cube was collected / is gone.</summary>
    Gone,
    /// <summary><c>35 36</c>: the collection is confirmed.</summary>
    Collected,
}

/// <summary>
/// 1 HP: a Hidden Cube packet. <paramref name="CubeId"/> is the dynamic varint id the game hands the cube (unique per
/// cube); <paramref name="PlayerId"/> is your own actor id when the packet carries it. The cube's coordinates are not in
/// these packets (it is "hidden" by design), so this is presence only — the direction arrow waits on the position packet.
/// </summary>
public sealed record CubeEvent(long TimeMs, CubePhase Phase, uint CubeId, uint PlayerId = 0) : GameEvent(TimeMs);
