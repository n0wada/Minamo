using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System;
using System.Text;

namespace Spellkit.Library.Binary;

public sealed class SpkByteArray : SpkForeignObject
{
    private const int DEFAULT_SIZE = 32;
    private byte[] buffer;
    private int size;
    private int readPosition;

    public SpkByteArray(SpkForeignTypeInfo typeInfo, byte[]? buffer) : base(typeInfo)
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

    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);

    public byte[] GetBytes()
    {
        var result = new byte[size];
        Array.Copy(buffer, result, size);
        return result;
    }

    public void Reset() => readPosition = 0;

    public override SpkObject Clone()
    {
        var clone = (SpkByteArray)MemberwiseClone();

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

    public void Write(ExecutionContext ctx, SpkObject obj)
    {
        Reset();

        switch (obj.TypeId)
        {
            case Spk.Integer:
                Write(((SpkInteger)obj).Value);
                break;
            case Spk.Float:
                Write(((SpkFloat)obj).Value);
                break;
            case Spk.Bool:
                Write(obj.IsTrue());
                break;
            case Spk.Char:
            case Spk.String:
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

    public SpkObject Read(ExecutionContext ctx, SpkTypeInfo type) =>
        type.ReflectedTypeId switch
        {
            Spk.Integer => ReadInt64(ctx),
            Spk.Float => ReadDouble(ctx),
            Spk.Bool => ReadBool(ctx),
            Spk.Char => ReadChar(ctx),
            Spk.String => ReadString(ctx),
            _ => ctx.InvalidType(type.ReflectedTypeName)
        };

    private SpkObject ReadInt64(ExecutionContext ctx)
    {
        if (readPosition > size - sizeof(long))
        {
            return ctx.IndexOutOfRange();
        }

        var cz = BitConverter.ToInt64(buffer, readPosition);
        readPosition += sizeof(long);
        return SpkInteger.Get(cz);
    }

    private SpkObject ReadDouble(ExecutionContext ctx)
    {
        if (readPosition > size - sizeof(double))
        {
            return ctx.IndexOutOfRange();
        }

        var cz = BitConverter.ToDouble(buffer, readPosition);
        readPosition += sizeof(double);
        return new SpkFloat(cz);
    }

    private SpkObject ReadBool(ExecutionContext ctx)
    {
        if (readPosition >= size)
        {
            return ctx.IndexOutOfRange();
        }

        var cz = buffer[readPosition];
        readPosition++;
        return cz is 1 ? True : False;
    }

    private SpkObject ReadChar(ExecutionContext ctx)
    {
        var ret = ReadRawString();
        return ret is null || ret.Length == 0 ? ctx.IndexOutOfRange() : new SpkChar(ret[0]);
    }

    private SpkObject ReadString(ExecutionContext ctx)
    {
        var ret = ReadRawString();
        return ret is null ? ctx.IndexOutOfRange() : new SpkString(ret);
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
