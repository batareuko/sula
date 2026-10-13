using System.Buffers.Binary;
using System.Text;

namespace AionMeter.Core.Protocol;

/// <summary>
/// 1 HP: reads a packet laid out field by field the way the game serializes its records: little-endian integers,
/// LEB128 varints, strings and lists prefixed with a varint count, and booleans packed eight to a byte (a byte is taken
/// when the first of eight is read). Any read past the end marks the reader failed; <see cref="Done"/> means every byte
/// was used and nothing failed — the check that the bytes really are this record.
/// </summary>
internal ref struct FieldReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _at;
    private int _bit = 8;
    private byte _bits;

    public bool Failed { get; private set; }

    public bool Done => !Failed && _at == _data.Length;

    private int Left => _data.Length - _at;

    public void Fail() => Failed = true;

    private ReadOnlySpan<byte> Take(int n)
    {
        if (Failed || n < 0 || n > Left)
        {
            Failed = true;
            return default;
        }
        var s = _data.Slice(_at, n);
        _at += n;
        return s;
    }

    public void Skip(int n) => Take(n);

    public byte U8() => Take(1) is { Length: 1 } s ? s[0] : (byte)0;

    public ushort U16() => Take(2) is { Length: 2 } s ? BinaryPrimitives.ReadUInt16LittleEndian(s) : (ushort)0;

    public uint U32() => Take(4) is { Length: 4 } s ? BinaryPrimitives.ReadUInt32LittleEndian(s) : 0;

    public long I64() => Take(8) is { Length: 8 } s ? BinaryPrimitives.ReadInt64LittleEndian(s) : 0;

    public ulong Var(int bits = 64)
    {
        ulong v = 0;
        for (var shift = 0; shift < bits && !Failed; shift += 7)
        {
            var b = U8();
            v |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return v;
        }
        Failed = true;
        return 0;
    }

    /// <summary>A list or string length; more items than bytes left (at <paramref name="minBytes"/> each) is not one.</summary>
    public int Count(int minBytes = 1)
    {
        var n = Var(32);
        if (n > (ulong)(Left / minBytes)) Failed = true;
        return Failed ? 0 : (int)n;
    }

    public bool Bool()
    {
        if (_bit == 8)
        {
            _bits = U8();
            _bit = 0;
        }
        return (_bits & (1 << _bit++)) != 0;
    }

    public string String()
    {
        var s = Take(Count());
        if (s.Length > 0 && s[^1] == 0) s = s[..^1];
        try
        {
            return Encoding.UTF8.GetString(s);
        }
        catch (ArgumentException)
        {
            Failed = true;
            return "";
        }
    }

    public void SkipString() => Take(Count());
}
