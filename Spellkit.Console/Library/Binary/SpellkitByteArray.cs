using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System;
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

    public void Write(ExecutionContext ctx, SpellkitObject obj)
    {
        Reset();

        switch (obj.TypeId)
        {
            case SpellkitTypeCodes.Integer:
                Write(((SpellkitInteger)obj).Value);
                break;
            case SpellkitTypeCodes.Float:
                Write(((SpellkitFloat)obj).Value);
                break;
            case SpellkitTypeCodes.Bool:
                Write(obj.IsTrue());
                break;
            case SpellkitTypeCodes.Char:
            case SpellkitTypeCodes.String:
                Write(obj.ToString());
                break;
            default:
                ctx.InvalidType(obj);
                break;
        }
    }

    private void Write(byte[] cz)
    {
        EnsureSize(cz.Length);
        Array.Copy(cz, 0, buffer, size, cz.Length);
        size += cz.Length;
    }

    private void Write(long value) => Write(BitConverter.GetBytes(value));

    private void Write(double value) => Write(BitConverter.GetBytes(value));

    private void Write(bool value) => Write(BitConverter.GetBytes(value));

    private void Write(string value)
    {
        var cz = Encoding.UTF8.GetBytes(value);
        Write(BitConverter.GetBytes(cz.Length));
        Write(cz);
    }

    public SpellkitObject Read(ExecutionContext ctx, SpellkitTypeInfo type) =>
        type.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Integer => ReadInt64(ctx),
            SpellkitTypeCodes.Float => ReadDouble(ctx),
            SpellkitTypeCodes.Bool => ReadBool(ctx),
            SpellkitTypeCodes.Char => ReadChar(ctx),
            SpellkitTypeCodes.String => ReadString(ctx),
            _ => ctx.InvalidType(type.ReflectedTypeName)
        };

    private SpellkitObject ReadInt64(ExecutionContext ctx)
    {
        if (readPosition > size - sizeof(long))
        {
            return ctx.IndexOutOfRange();
        }

        var cz = BitConverter.ToInt64(buffer, readPosition);
        readPosition += sizeof(long);
        return SpellkitInteger.Get(cz);
    }

    private SpellkitObject ReadDouble(ExecutionContext ctx)
    {
        if (readPosition > size - sizeof(double))
        {
            return ctx.IndexOutOfRange();
        }

        var cz = BitConverter.ToDouble(buffer, readPosition);
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

    private SpellkitObject ReadChar(ExecutionContext ctx)
    {
        var ret = ReadRawString();
        return ret is null || ret.Length == 0 ? ctx.IndexOutOfRange() : new SpellkitChar(ret[0]);
    }

    private SpellkitObject ReadString(ExecutionContext ctx)
    {
        var ret = ReadRawString();
        return ret is null ? ctx.IndexOutOfRange() : new SpellkitString(ret);
    }

    private string? ReadRawString()
    {
        if (readPosition > size - sizeof(int))
        {
            return null;
        }

        var len = BitConverter.ToInt32(buffer, readPosition);
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
