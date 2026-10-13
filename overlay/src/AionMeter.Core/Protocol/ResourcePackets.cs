using AionMeter.Core.Events;

namespace AionMeter.Core.Protocol;

/// <summary>
/// 1 HP: the character's entry tickets and energies. <c>0B 61</c>: <code>count × ticket</code>; <c>0C 61</c> (one
/// changed): <code>flags u8, ticket, u8, [flags 1] delta u32</code>. A ticket: <code>flags u8, id i32, [flags 1] i64,
/// [flags 2] i64, [flags 4] count varint, [flags 8] bonus varint</code>. Ids: 60000001 aether, 1 Shugo keys,
/// 4 Nightmare, 8 invasion, 80000001 Awakening.
/// </summary>
internal static class ResourcePackets
{
    public static List<ResourceEvent> Read(ReadOnlySpan<byte> body, bool single, long timeMs)
    {
        var list = new List<ResourceEvent>();
        var r = new FieldReader(body);
        var flags = single ? r.U8() : (byte)0;
        if ((flags & ~1) != 0) return [];
        var count = single ? 1 : r.Count(5);
        for (var i = 0; i < count && !r.Failed; i++)
        {
            var f = r.U8();
            if ((f & ~0x0F) != 0) return [];
            var id = (int)r.U32();
            if ((f & 1) != 0) r.I64();
            if ((f & 2) != 0) r.I64();
            var amount = (f & 4) != 0 ? (int)r.Var(32) : 0;
            var bonus = (f & 8) != 0 ? (int)r.Var(32) : 0;
            list.Add(new ResourceEvent(timeMs, id, amount, bonus));
        }
        if (single)
        {
            r.U8();
            if ((flags & 1) != 0) r.U32();
        }
        return r.Done ? list : [];
    }
}
