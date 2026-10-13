using AionMeter.Core.Events;

namespace AionMeter.Core.Protocol;

/// <summary>
/// 1 HP: buff packets. <c>2A 38</c>: <code>target varint, count × buff</code>; <c>2B 38</c> (a refresh):
/// <code>target varint, buff, bool</code>. A buff: <code>flags u8, instance varint, code i32, duration ms i64,
/// expires at ms i64, [flags 1] caster varint, level u8, [flags 2] source skill u32, removing bool, [flags 4] varint,
/// [flags 8] i32, [flags 16] 12 bytes</code>.
/// </summary>
internal static class BuffPackets
{
    public static List<BuffEvent> Read(ReadOnlySpan<byte> body, bool refresh, long timeMs)
    {
        var list = new List<BuffEvent>();
        var r = new FieldReader(body);
        var target = (uint)r.Var(32);
        var count = refresh ? 1 : r.Count(23);
        for (var i = 0; i < count && !r.Failed; i++)
        {
            var flags = r.U8();
            if ((flags & ~0x1F) != 0) r.Fail();
            var instance = (int)r.Var(32);
            var code = (int)r.U32();
            var duration = r.I64();
            r.I64();
            var caster = (flags & 1) != 0 ? (uint)r.Var(32) : 0;
            r.U8();
            if ((flags & 2) != 0) r.U32();
            var removing = r.Bool();
            if ((flags & 4) != 0) r.Var(32);
            if ((flags & 8) != 0) r.U32();
            if ((flags & 16) != 0) r.Skip(12);
            if (code > 0 && duration >= -1) list.Add(new BuffEvent(timeMs, target, caster, code, duration, instance, removing));
        }
        if (refresh) r.Bool();
        return r.Done && target > 0 ? list : [];
    }
}
