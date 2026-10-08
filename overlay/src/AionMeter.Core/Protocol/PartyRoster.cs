using System.Text;

namespace AionMeter.Core.Protocol;

public sealed record PartyMember(string Name, int Slot, int ServerId, int Level, int GearScore, long CombatPower);

/// <summary>
/// 1 HP: the party roster the server sends on every party change (opcode <c>02 97</c>). Layout and the walk over
/// vacant slots follow taengu/A2Tools-DPS-Meter (GPL-3.0, src-tauri/src/capture/stream_processor.rs,
/// parse_party_roster_at, 2026-10):
/// <code>
/// party_key u32, party_name (u8 len + utf8), party_size u8, dungeon_id u32, u8 u8, leader_dbid u64, u8 u8 u8,
/// member_count varint, member × count:
///   mask u8, slot u8 (1-based), dbid u64 (high u16 = world id), name (u8 len + utf8), class u32, level u32,
///   gear score u32, … world id u16, u16, u8, combat power u64, …
/// </code>
/// Empty slots have a zero mask, a zero dbid and an empty name; record tails vary in width, so each next member is
/// found again by its header shape. Members join to players in the world by name (dbid is an account id).
/// </summary>
public sealed record PartyRoster(IReadOnlyList<PartyMember> Members, bool Complete, int DungeonId)
{
    /// <summary>Every roster in <paramref name="data"/> (scanned for the opcode: rosters also ride inside other packets).</summary>
    public static List<PartyRoster> FindAll(ReadOnlySpan<byte> data)
    {
        var found = new List<PartyRoster>();
        for (var i = 0; i + 24 < data.Length; i++)
        {
            if (data[i] != 0x02 || data[i + 1] != 0x97) continue;
            if (TryParse(data, i + 2) is { } roster)
            {
                found.Add(roster);
                i += 1;
            }
        }
        return found;
    }

    /// <summary>The roster whose body starts at <paramref name="at"/> (right after the opcode), or null.</summary>
    public static PartyRoster? TryParse(ReadOnlySpan<byte> data, int at)
    {
        var o = at + 4; // party key
        if (o >= data.Length) return null;
        int nameLen = data[o++];
        if (nameLen is < 1 or > 40 || o + nameLen > data.Length || !Utf8(data.Slice(o, nameLen), out _)) return null;
        o += nameLen;
        if (o >= data.Length) return null;
        int partySize = data[o++];
        if (partySize is < 1 or > 12) return null;
        if (o + 4 > data.Length) return null;
        var dungeonId = (int)Wire.U32(data, o);
        o += 4 + 2 + 8 + 3; // dungeon id, 2 unknown, leader dbid, 3 unknown
        if (!Wire.TryVarint(data, o, out var countValue, out var countLen) || countValue is < 1 or > 12) return null;
        o += countLen;
        var count = (int)countValue;

        var members = new List<PartyMember>();
        var complete = false;
        for (var n = 0; n < count; n++)
        {
            if (o + 20 > data.Length) break;
            int slot = data[o + 1];
            o += 2; // mask, slot
            var dbid = BitConverter.ToUInt64(data.Slice(o, 8));
            o += 8;
            var serverId = (int)(dbid >> 48);
            int len = data[o++];
            if (len == 0)
            {
                // A vacant slot: a later one may still hold a member (slots 1 and 5 filled, 2-4 empty).
                if (FindLaterMember(data, o, slot, count) is { } next)
                {
                    o = next;
                    continue;
                }
                complete = true;
                break;
            }
            if (len > 40 || o + len > data.Length || !Utf8(data.Slice(o, len), out var name)) break;
            o += len;
            if (o + 12 > data.Length) break;
            o += 4; // class (roster encoding)
            var level = (int)Wire.U32(data, o);
            o += 4;
            if (level is < 1 or > 200) break;
            var gear = (int)Wire.U32(data, o);
            o += 4;
            if (gear is < 0 or > 1_000_000) break;

            // The stretch up to combat power varies in width: anchor on the world id repeated as a u16.
            if (FindU16(data, o, o + 10, (ushort)serverId) is not { } anchor) break;
            o = anchor + 2 + 2 + 1;
            if (o + 8 > data.Length) break;
            var power = BitConverter.ToInt64(data.Slice(o, 8));
            o += 8;
            if (power is < 0 or > 100_000_000) break;
            members.Add(new PartyMember(name, slot, serverId, level, gear, power));

            if (slot >= count)
            {
                complete = true;
                break;
            }
            if ((FindMember(data, o, 32, slot + 1) ?? FindVacant(data, o, slot + 1)) is { } following) o = following;
            else break;
        }
        return members.Count == 0 ? null : new PartyRoster(members, complete, dungeonId);
    }

    private static int? FindU16(ReadOnlySpan<byte> data, int from, int to, ushort wanted)
    {
        var end = Math.Min(to, data.Length - 2);
        for (var i = from; i <= end; i++)
            if (Wire.U16(data, i) == wanted) return i;
        return null;
    }

    /// <summary>A named member in a slot after <paramref name="slot"/>, past the vacant records (about 36 bytes each).</summary>
    private static int? FindLaterMember(ReadOnlySpan<byte> data, int from, int slot, int count)
    {
        for (var next = slot + 1; next <= count; next++)
            if (FindMember(data, from, 48 * (next - slot), next) is { } at) return at;
        return null;
    }

    /// <summary>A vacant record for <paramref name="slot"/>: zero mask, the slot, zero dbid, empty name.</summary>
    private static int? FindVacant(ReadOnlySpan<byte> data, int from, int slot)
    {
        var end = Math.Min(from + 32, data.Length - 11);
        for (var i = from; i <= end; i++)
            if (data[i] == 0 && data[i + 1] == slot && data.Slice(i + 2, 9).IndexOfAnyExcept((byte)0) < 0) return i;
        return null;
    }

    /// <summary><c>mask u8, slot u8, dbid u64 (world id 1..9999 on top), name len u8, utf8 name</c> within the span.</summary>
    private static int? FindMember(ReadOnlySpan<byte> data, int from, int span, int slot)
    {
        var end = Math.Min(from + span, data.Length - 12);
        for (var i = from; i <= end; i++)
        {
            if (data[i + 1] != slot) continue;
            var server = Wire.U16(data, i + 8);
            if (server is 0 or > 9_999) continue;
            int len = data[i + 10];
            if (len is 0 or > 40 || i + 11 + len > data.Length) continue;
            if (!Utf8(data.Slice(i + 11, len), out _)) continue;
            return i;
        }
        return null;
    }

    private static readonly UTF8Encoding Strict = new(false, true);

    private static bool Utf8(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = Strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = "";
            return false;
        }
    }
}
