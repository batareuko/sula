namespace AionMeter.Core.Combat;

/// <summary>
/// 1 HP: the live character and party state served by the local <c>/state</c> endpoint (App/Services/StreamServer.cs).
/// It is a flat snapshot of what the meter decodes right now; anything the packets do not carry is null, never a guess.
/// </summary>
/// <param name="TsMs">Unix milliseconds the snapshot was built at.</param>
public sealed record LiveState(long TsMs, LiveSelf Me, LiveTarget? Target, IReadOnlyList<LivePartyMember> Party);

/// <param name="Mp">Mana is not in a packet the meter decodes yet: null until one is confirmed.</param>
/// <param name="Casting">A cast in progress is not announced with its end: null until the packets are confirmed.</param>
public sealed record LiveSelf(
    string? Name,
    string? Class,
    bool? Alive,
    long? Hp,
    long? HpMax,
    long? Mp,
    long? MpMax,
    bool InCombat,
    LiveCast? Casting,
    IReadOnlyList<LiveBuff> Buffs,
    IReadOnlyList<LiveBuff> Debuffs,
    IReadOnlyList<LiveSkill> Skills);

/// <param name="Distance">Metres, from the last positions both the target and you reported.</param>
public sealed record LiveTarget(string? Name, long? Hp, long? HpMax, bool IsEnemy, double? Distance);

/// <param name="InRange">What the game dims on its party panel is drawn by the client and is not in the traffic: null.</param>
/// <param name="Distance">Metres, from the last positions both of you reported; null until both are known.</param>
public sealed record LivePartyMember(
    string? Name,
    string? Class,
    bool? Alive,
    long? Hp,
    long? HpMax,
    bool? InRange,
    double? Distance,
    IReadOnlyList<LiveBuff> Debuffs);

public sealed record LiveCast(int? SkillId, string? Name, int? ChargeLevel, long? EndsInMs);

/// <param name="RemainMs">Null for a buff that lasts until it is removed.</param>
/// <param name="Stacks">Not carried by the buff packets the meter reads: null.</param>
/// <param name="Dispellable">Not carried by the buff packets the meter reads: null.</param>
public sealed record LiveBuff(int Id, string? Name, long? RemainMs, int? Stacks, bool? Dispellable);

/// <param name="Charges">The charge count the game sent with the cooldown; null for a skill without charges.</param>
/// <param name="ChargesMax">The packet carries no maximum: null.</param>
/// <param name="ToggleOn">For a skill seen putting on a lasting buff: true while that buff is on; null for the rest.</param>
public sealed record LiveSkill(
    int? Id,
    string? Name,
    long? CdRemainMs,
    long? CdTotalMs,
    int? Charges,
    int? ChargesMax,
    bool? ToggleOn);
