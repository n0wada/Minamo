using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System;
using System.Buffers.Binary;
using System.Text;

namespace Spellkit.Library.Binary;

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
