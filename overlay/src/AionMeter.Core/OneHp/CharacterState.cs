using System.Text.Json;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;

namespace AionMeter.Core.OneHp;

/// <summary>
/// 1 HP: the live state of your character and party as one JSON document, for local tools (AutoHotkey) to read at
/// <c>http://127.0.0.1:7799/state</c>. It is a plain read of what the meter already decoded — nothing is sent to the
/// game. Values the meter does not know are <c>null</c>, never left out, so the shape is always the same.
/// </summary>
public static class CharacterState
{
    /// <summary>
    /// The whole document. <c>{"ok":false}</c> while you are not identified yet (the game has not been read or the
    /// meter started and has not seen your character). <paramref name="nowMs"/> is the wall clock the meter uses.
    /// </summary>
    public static string Json(CombatTracker tracker, long nowMs)
    {
        var frame = tracker.PartyFrame(nowMs, selfMinMs: 0, partyMinMs: 0);
        var self = frame.FirstOrDefault(m => m.IsSelf);
        if (self is null) return """{"ok":false}""";

        return JsonSerializer.Serialize(new
        {
            ok = true,
            ts = nowMs,
            me = Me(self, tracker, nowMs),
            target = (object?)null, // the current target is not tracked yet
            party = Party(frame, nowMs),
        });
    }

    private static object Me(PartyFrameMember self, CombatTracker tracker, long nowMs)
    {
        var (buffs, debuffs) = Split(self.Buffs, nowMs);
        return new
        {
            name = NullIfEmpty(self.Name),
            cls = self.Class == GameClass.Unknown ? null : self.Class.ToString().ToLowerInvariant(),
            alive = Alive(self.Hp),
            hp = self.Hp >= 0 ? self.Hp : (long?)null,
            hpMax = self.MaxHp > 0 ? self.MaxHp : (long?)null,
            mp = (long?)null,   // the HP/MP packet is parsed for HP only
            mpMax = (long?)null,
            // "in combat" = the meter has an open fight of yours; the game sends no reliable self combat flag.
            inCombat = tracker.LiveFight() is not null,
            casting = (object?)null, // a running cast is not tracked
            buffs,
            debuffs,
            skills = self.Cooldowns.Select(c => (object)new
            {
                id = c.SkillCode,
                name = c.Skill,
                cdRemainMs = Math.Max(0, c.LeftMs),
                cdTotalMs = c.TotalMs,
                charges = (int?)null,
                chargesMax = (int?)null,
                toggleOn = (bool?)null,
            }).ToList(),
        };
    }

    private static object Party(IReadOnlyList<PartyFrameMember> frame, long nowMs)
    {
        var party = new List<object>(frame.Count);
        foreach (var m in frame)
        {
            if (m.IsSelf) continue; // you are already "me"
            party.Add(new
            {
                name = NullIfEmpty(m.Name),
                cls = m.Class == GameClass.Unknown ? null : m.Class.ToString().ToLowerInvariant(),
                alive = Alive(m.Hp),
                hp = m.Hp >= 0 ? m.Hp : (long?)null,
                hpMax = m.MaxHp > 0 ? m.MaxHp : (long?)null,
                inRange = (bool?)null,  // player positions are not stored by the tracker
                distance = (double?)null,
                debuffs = Split(m.Buffs, nowMs).Debuffs,
            });
        }
        return party;
    }

    private static (IReadOnlyList<object> Buffs, IReadOnlyList<object> Debuffs) Split(IReadOnlyList<ActiveBuff> list, long nowMs)
    {
        var buffs = new List<object>();
        var debuffs = new List<object>();
        foreach (var b in list)
        {
            var remain = b.EndMs > 0 ? Math.Max(0, b.EndMs - nowMs) : (long?)null;
            if (b.Debuff) debuffs.Add(new { id = b.Code, name = b.Name, remainMs = remain, dispellable = (bool?)null });
            else buffs.Add(new { id = b.Code, name = b.Name, remainMs = remain, stacks = (int?)null });
        }
        return (buffs, debuffs);
    }

    private static bool? Alive(long hp) => hp > 0 ? true : hp == 0 ? false : null;
    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
