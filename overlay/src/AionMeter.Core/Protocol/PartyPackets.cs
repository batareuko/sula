using System.Buffers.Binary;
using System.Text;
using AionMeter.Core.Events;

namespace AionMeter.Core.Protocol;

/// <summary>
/// 1 HP: the party packets of the Global client — the list (<c>00 92</c>, sent on joining, on a change and on entering
/// a zone), one member (<c>0D 92</c>, someone joining or a member's card), leaving (<c>06 97</c>) and the party gone
/// (<c>1D 97</c>). Members out of view come with id 0 and their name.
/// </summary>
internal static class PartyPackets
{
    private static bool ValidServer(int server) => server is >= 1000 and < 3000;

    /// <summary>
    /// <c>00 92</c>: <code>
    /// flags u8, i64, i64, u32, 4 bytes,
    /// [flags 1] u8 f, u32, [f 1] u32, u32
    /// [flags 2] count × (u8 f, u8, u32, string, [f 1] u32)
    /// [flags 4] count × (17 bytes, bool)
    /// [flags 8] count × (u8, varint, string, u32, string, i64, u32, 4 bytes)
    /// count (≤ 6) × member
    /// [flags 16] count × (u8 f, u8, string, u32, 12 bytes, i64, i64, [f 1] count × 8 bytes)
    /// u8
    /// </code>
    /// member: <code>
    /// u8 f, slot u8, actor i32, server u16, u16, string, i64, name string, u32, varint, varint, 24 bytes, i64, u32,
    /// 12 bytes, bool, [f 1] string, [f 2] u32, 12 bytes, bool, bool, [f 4] count × (u8 g, u32, u8, [g 1] varint), i64, u8
    /// </code>
    /// </summary>
    public static List<PartyMemberInfo>? ReadList(ReadOnlySpan<byte> body)
    {
        var r = new FieldReader(body);
        var flags = r.U8();
        if ((flags & ~0x1F) != 0) return null;
        r.I64();
        r.I64();
        r.U32();
        r.Skip(4);
        if ((flags & 1) != 0)
        {
            var f = r.U8();
            if ((f & ~1) != 0) return null;
            r.U32();
            if ((f & 1) != 0) r.U32();
            r.U32();
        }
        if ((flags & 2) != 0)
            for (int i = 0, n = r.Count(7); i < n && !r.Failed; i++)
            {
                var f = r.U8();
                if ((f & ~1) != 0) r.Fail();
                r.U8();
                r.U32();
                r.SkipString();
                if ((f & 1) != 0) r.U32();
            }
        if ((flags & 4) != 0)
            for (int i = 0, n = r.Count(17); i < n && !r.Failed; i++)
            {
                r.Skip(17);
                r.Bool();
            }
        if ((flags & 8) != 0)
            for (int i = 0, n = r.Count(24); i < n && !r.Failed; i++)
            {
                r.U8();
                r.Var(32);
                r.SkipString();
                r.U32();
                r.SkipString();
                r.I64();
                r.U32();
                r.Skip(4);
            }
        var members = new List<PartyMemberInfo>();
        var count = r.Count();
        if (count > 6) return null;
        for (var i = 0; i < count && !r.Failed; i++)
            if (ReadMember(ref r) is { } m) members.Add(m);
        if ((flags & 0x10) != 0)
            for (int i = 0, n = r.Count(35); i < n && !r.Failed; i++)
            {
                var f = r.U8();
                if ((f & ~1) != 0) r.Fail();
                r.U8();
                r.SkipString();
                r.U32();
                r.Skip(12);
                r.I64();
                r.I64();
                if ((f & 1) != 0) r.Skip(r.Count(8) * 8);
            }
        r.U8();
        return r.Done ? members : null;
    }

    private static PartyMemberInfo? ReadMember(ref FieldReader r)
    {
        var f = r.U8();
        if ((f & ~7) != 0) r.Fail();
        var slot = r.U8();
        var actor = (int)r.U32();
        var server = r.U16();
        r.U16();
        r.SkipString();
        r.I64();
        var name = r.String().Trim();
        r.U32();
        r.Var();
        r.Var();
        r.Skip(24);
        r.I64();
        r.U32();
        r.Skip(12);
        r.Bool();
        if ((f & 1) != 0) r.SkipString();
        if ((f & 2) != 0) r.U32();
        r.Skip(12);
        r.Bool();
        r.Bool();
        if ((f & 4) != 0)
            for (int i = 0, n = r.Count(6); i < n && !r.Failed; i++)
            {
                var g = r.U8();
                if ((g & ~1) != 0) r.Fail();
                r.U32();
                r.U8();
                if ((g & 1) != 0) r.Var(32);
            }
        r.I64();
        r.U8();
        if (r.Failed || actor < 0 || slot is < 1 or > 6 || !ValidServer(server) || !Wire.TryName(Encoding.UTF8.GetBytes(name), out var valid))
            return null;
        return new PartyMemberInfo((uint)actor, valid, server, slot);
    }

    /// <summary>
    /// <c>0D 92</c>: <code>kind u8 (0 or 3), slot u8, actor i32 (0 out of view), server u16 ×2, uuid length u8 (36),
    /// uuid, 8 bytes, name length u8, name, …</code>
    /// </summary>
    public static PartyMemberInfo? ReadMember(ReadOnlySpan<byte> body)
    {
        if (body.Length < 57 || body[0] is not (0 or 3) || body[1] is < 1 or > 6 || body[10] != 36) return null;
        var actor = BinaryPrimitives.ReadInt32LittleEndian(body[2..]);
        var server = BinaryPrimitives.ReadUInt16LittleEndian(body[6..]);
        if (actor < 0 || !ValidServer(server) || server != BinaryPrimitives.ReadUInt16LittleEndian(body[8..])) return null;
        int len = body[55];
        if (len < 1 || 56 + len > body.Length || !Wire.TryName(body.Slice(56, len), out var name)) return null;
        return new PartyMemberInfo((uint)actor, name, server, body[1]);
    }

    /// <summary>
    /// 1 HP: <c>07 97</c>, someone asks to join your party: <code>…, server u16 at 10, …, job u32, level u32, 4 bytes,
    /// name length u8, name, 6 bytes, combat power u32, …</code> — the name sits near the end (up to 40 bytes after it).
    /// </summary>
    public static PartyRequestEvent? ReadRequest(ReadOnlySpan<byte> body, long timeMs)
    {
        if (body.Length < 12) return null;
        var server = BinaryPrimitives.ReadUInt16LittleEndian(body[10..]);
        for (var slack = 0; slack <= 40; slack++)
            for (var len = 1; len <= 36; len++)
            {
                var at = body.Length - len - 1 - slack;
                if (at < 12 || body[at] != len || !Wire.TryName(body.Slice(at + 1, len), out var name)) continue;
                var job = BinaryPrimitives.ReadUInt32LittleEndian(body[(at - 12)..]);
                var level = BinaryPrimitives.ReadUInt32LittleEndian(body[(at - 8)..]);
                if (level is < 1 or > 55) continue;
                var cp = at + 1 + len + 6;
                long power = cp + 4 <= body.Length && BinaryPrimitives.ReadUInt32LittleEndian(body[cp..]) is var p and <= 9_999_999 ? p : 0;
                return new PartyRequestEvent(timeMs, name, ValidServer(server) ? server : 0, ClassOfJob(job), (int)level, power);
            }
        return null;
    }

    /// <summary>The game's job codes, four to a class.</summary>
    public static GameClass ClassOfJob(uint job) => job switch
    {
        >= 5 and <= 8 => GameClass.Gladiator,
        >= 9 and <= 12 => GameClass.Templar,
        >= 13 and <= 16 => GameClass.Ranger,
        >= 17 and <= 20 => GameClass.Assassin,
        >= 21 and <= 24 => GameClass.Elementalist,
        >= 25 and <= 28 => GameClass.Sorcerer,
        >= 29 and <= 32 => GameClass.Cleric,
        >= 33 and <= 36 => GameClass.Chanter,
        >= 45 and <= 48 => GameClass.Brawler,
        _ => GameClass.Unknown,
    };

    /// <summary><c>06 97</c>: <code>result u16 (0 = done), state u8 (1 joined, 2 left), u32</code> — true when you left.</summary>
    public static bool IsLeave(ReadOnlySpan<byte> body) =>
        body.Length == 7 && BinaryPrimitives.ReadUInt16LittleEndian(body) == 0 && body[2] == 2;

    /// <summary><c>1D 97</c> with the body <c>00 00</c>: the party is gone.</summary>
    public static bool IsDisband(ReadOnlySpan<byte> body) => body is [0, 0];
}
