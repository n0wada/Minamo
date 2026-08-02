using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Spellkit.Runtime.Types;

public sealed class SpellkitByteArray : SpellkitForeignObject
{
    private const int DEFAULT_SIZE = 32;
    private byte[] buffer;
    private int size;
    private int readPosition;

    public SpellkitByteArray(SpellkitForeignTypeInfo typeInfo, byte[]? buffer) : base(typeInfo)
    {
        if (buffer is not null)
        {
            this.buffer = (byte[])buffer.Clone();
            size = buffer.Length;
        }
        else
        {
            this.buffer = new byte[DEFAULT_SIZE];
        }
    }

    public int Position => readPosition;

    public int Count => size;

    public override object ToObject() => GetBytes();

    public override int GetHashCode() => buffer.GetHashCode();

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public byte[] GetBytes()
    {
        var result = new byte[size];
        Array.Copy(buffer, result, size);
        return result;
    }

    public void Reset() => readPosition = 0;

    public override SpellkitObject Clone()
    {
        var clone = (SpellkitByteArray)MemberwiseClone();

        if (buffer is not null)
        {
            clone.buffer = GetBytes();
        }

        return clone;
    }

    private void EnsureSize(int append = 0)
    {
        if (append < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(append));
        }

        var required = checked(size + append);
        if (required <= buffer.Length)
        {
            return;
        }

        var doubled = buffer.Length == 0 ? 4 : checked(buffer.Length * 2);
        var newSize = System.Math.Max(required, doubled);
        var dest = new byte[newSize];
        Array.Copy(buffer, 0, dest, 0, size);
        buffer = dest;
    }

    public void Write(ExecutionContext ctx, SpellkitObject obj, bool littleEndian)
    {
        Reset();

        switch (obj.TypeId)
        {
            case SpellkitTypeCodes.Integer:
                Write(((SpellkitInteger)obj).Value, littleEndian);
                break;
            case SpellkitTypeCodes.Float:
                Write(((SpellkitFloat)obj).Value, littleEndian);
                break;
            case SpellkitTypeCodes.Bool:
                Write(obj.IsTrue());
                break;
            case SpellkitTypeCodes.Char:
            case SpellkitTypeCodes.String:
                Write(obj.ToString(), littleEndian);
                break;
            default:
                if (obj is SpellkitByteArray bytes)
                {
                    Write(bytes.GetBytes());
                }
                else
                {
                    ctx.InvalidType(obj);
                }
                break;
        }
    }

    private void Write(byte[] cz)
    {
        EnsureSize(cz.Length);
        Array.Copy(cz, 0, buffer, size, cz.Length);
        size += cz.Length;
    }

    private void Write(long value, bool littleEndian)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        }
        Write(bytes.ToArray());
    }

    private void Write(double value, bool littleEndian) =>
        Write(BitConverter.DoubleToInt64Bits(value), littleEndian);

    private void Write(bool value) => Write(BitConverter.GetBytes(value));

    private void Write(string value, bool littleEndian)
    {
        var cz = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt32LittleEndian(length, cz.Length);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(length, cz.Length);
        }
        Write(length.ToArray());
        Write(cz);
    }

    public SpellkitObject Read(ExecutionContext ctx, SpellkitTypeInfo type, bool littleEndian) =>
        type.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Integer => ReadInt64(ctx, littleEndian),
            SpellkitTypeCodes.Float => ReadDouble(ctx, littleEndian),
            SpellkitTypeCodes.Bool => ReadBool(ctx),
            SpellkitTypeCodes.Char => ReadChar(ctx, littleEndian),
            SpellkitTypeCodes.String => ReadString(ctx, littleEndian),
            _ => ctx.InvalidType(type.ReflectedTypeName)
        };

    private SpellkitObject ReadInt64(ExecutionContext ctx, bool littleEndian)
    {
        if (readPosition > size - sizeof(long))
        {
            return ctx.IndexOutOfRange();
        }

        var source = buffer.AsSpan(readPosition, sizeof(long));
        var cz = littleEndian
            ? BinaryPrimitives.ReadInt64LittleEndian(source)
            : BinaryPrimitives.ReadInt64BigEndian(source);
        readPosition += sizeof(long);
        return SpellkitInteger.Get(cz);
    }

    private SpellkitObject ReadDouble(ExecutionContext ctx, bool littleEndian)
    {
        if (readPosition > size - sizeof(double))
        {
            return ctx.IndexOutOfRange();
        }

        var source = buffer.AsSpan(readPosition, sizeof(double));
        var bits = littleEndian
            ? BinaryPrimitives.ReadInt64LittleEndian(source)
            : BinaryPrimitives.ReadInt64BigEndian(source);
        var cz = BitConverter.Int64BitsToDouble(bits);
        readPosition += sizeof(double);
        return new SpellkitFloat(cz);
    }

    private SpellkitObject ReadBool(ExecutionContext ctx)
    {
        if (readPosition >= size)
        {
            return ctx.IndexOutOfRange();
        }

        var cz = buffer[readPosition];
        readPosition++;
        return cz is 1 ? True : False;
    }

    private SpellkitObject ReadChar(ExecutionContext ctx, bool littleEndian)
    {
        var ret = ReadRawString(littleEndian);
        return ret is null || ret.Length == 0 ? ctx.IndexOutOfRange() : new SpellkitChar(ret[0]);
    }

    private SpellkitObject ReadString(ExecutionContext ctx, bool littleEndian)
    {
        var ret = ReadRawString(littleEndian);
        return ret is null ? ctx.IndexOutOfRange() : new SpellkitString(ret);
    }

    private string? ReadRawString(bool littleEndian)
    {
        if (readPosition > size - sizeof(int))
        {
            return null;
        }

        var source = buffer.AsSpan(readPosition, sizeof(int));
        var len = littleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(source)
            : BinaryPrimitives.ReadInt32BigEndian(source);
        readPosition += sizeof(int);

        if (len < 0 || len > size - readPosition)
        {
            return null;
        }

        var retval = Encoding.UTF8.GetString(buffer, readPosition, len);
        readPosition += len;
        return retval;
    }
}

[SpellkitType]
public sealed partial class SpellkitByteArrayTypeInfo : SpellkitForeignTypeInfo
{
    private const string ByteArray = nameof(ByteArray);

    public override string ReflectedTypeName => ByteArray;

    public SpellkitByteArrayTypeInfo()
    {
        SetSupportedOperations(Ops.Get | Ops.Len);
    }

    public SpellkitByteArray Create(byte[]? buffer) => new(this, buffer);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        var buffer = ((SpellkitByteArray)arg).GetBytes();
        var strs = buffer.Select(b => "0x" + b.ToString("X").PadLeft(2, '0')).ToArray();
        return new SpellkitString("{" + string.Join(",", strs) + "}");
    }

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitByteArray)arg).Count);

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index)
    {
        if (index is not SpellkitInteger integer || !integer.TryGetInt32(out var position))
        {
            return ctx.IndexOutOfRange(index);
        }

        var bytes = (SpellkitByteArray)self;
        position = position < 0 ? bytes.Count + position : position;
        return position < 0 || position >= bytes.Count
            ? ctx.IndexOutOfRange(index)
            : SpellkitInteger.Get(bytes.GetBytes()[position]);
    }

    [SpellkitMethod]
    internal static SpellkitObject ToArray(SpellkitByteArray self) =>
        new SpellkitArray(self.GetBytes().Select(value => (SpellkitObject)SpellkitInteger.Get(value)).ToArray());

    [SpellkitMethod]
    internal static string ToHex(SpellkitByteArray self, bool lowerCase = false)
    {
        var value = Convert.ToHexString(self.GetBytes());
        return lowerCase ? value.ToLowerInvariant() : value;
    }

    [SpellkitMethod]
    internal static string ToBase64(SpellkitByteArray self) => Convert.ToBase64String(self.GetBytes());

    [SpellkitMethod]
    internal static SpellkitObject Decode(ExecutionContext ctx, SpellkitByteArray self, string encoding = "utf-8")
    {
        var selected = GetEncoding(ctx, encoding);
        if (selected is null)
        {
            return Nil;
        }

        try
        {
            return SpellkitString.Get(selected.GetString(self.GetBytes()));
        }
        catch (DecoderFallbackException)
        {
            return ctx.ParsingFailed();
        }
    }

    [SpellkitStaticMethod]
    internal static SpellkitObject Concat(ExecutionContext ctx, SpellkitByteArray first, SpellkitByteArray second)
    {
        var a1 = first.GetBytes();
        var a2 = second.GetBytes();
        var a3 = new byte[a1.Length + a2.Length];
        Array.Copy(a1, a3, a1.Length);
        Array.Copy(a2, 0, a3, a1.Length, a2.Length);
        return new SpellkitByteArray(ctx.Type<SpellkitByteArrayTypeInfo>(), a3);
    }

    [SpellkitStaticMethod(ByteArray)]
    internal static SpellkitObject CreateNew(ExecutionContext ctx, [Default] SpellkitObject values = null!) =>
        values is null || values.TypeId == SpellkitTypeCodes.Nil
            ? new SpellkitByteArray(ctx.Type<SpellkitByteArrayTypeInfo>(), null)
            : FromArray(ctx, values);

    [SpellkitStaticMethod]
    internal static SpellkitObject FromArray(ExecutionContext ctx, SpellkitObject values)
    {
        var result = new List<byte>();
        foreach (var value in SpellkitIterator.ToEnumerable(ctx, values))
        {
            if (ctx.HasErrors)
            {
                return Nil;
            }

            if (value is not SpellkitInteger integer || integer.Value is < byte.MinValue or > byte.MaxValue)
            {
                return ctx.InvalidValue(value);
            }
            result.Add((byte)integer.Value);
        }
        return new SpellkitByteArray(ctx.Type<SpellkitByteArrayTypeInfo>(), result.ToArray());
    }

    [SpellkitStaticMethod]
    internal static SpellkitObject FromHex(ExecutionContext ctx, string value)
    {
        try
        {
            return new SpellkitByteArray(ctx.Type<SpellkitByteArrayTypeInfo>(), Convert.FromHexString(value));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    [SpellkitStaticMethod]
    internal static SpellkitObject FromBase64(ExecutionContext ctx, string value)
    {
        try
        {
            return new SpellkitByteArray(ctx.Type<SpellkitByteArrayTypeInfo>(), Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    [SpellkitStaticMethod]
    internal static SpellkitObject FromString(ExecutionContext ctx, string value, string encoding = "utf-8")
    {
        var selected = GetEncoding(ctx, encoding);
        if (selected is null)
        {
            return Nil;
        }

        try
        {
            return new SpellkitByteArray(ctx.Type<SpellkitByteArrayTypeInfo>(), selected.GetBytes(value));
        }
        catch (EncoderFallbackException)
        {
            return ctx.InvalidValue(value);
        }
    }

    private static Encoding? GetEncoding(ExecutionContext ctx, string name)
    {
        var normalized = name.Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "utf-8" or "utf8" => new UTF8Encoding(false, true),
            "utf-16" or "utf-16le" or "utf16" or "utf16le" => new UnicodeEncoding(false, false, true),
            "utf-16be" or "utf16be" => new UnicodeEncoding(true, false, true),
            "ascii" => Encoding.ASCII,
            _ => InvalidEncoding(ctx, name)
        };
    }

    private static Encoding? InvalidEncoding(ExecutionContext ctx, string name)
    {
        ctx.InvalidValue(name);
        return null;
    }
}
