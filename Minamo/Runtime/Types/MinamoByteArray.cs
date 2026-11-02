using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System;
using System.Collections.Generic;
using System.Text;

namespace Minamo.Runtime.Types;

public sealed class MinamoByteArray : MinamoForeignObject
{
    private const int DEFAULT_SIZE = 32;
    private byte[] buffer;
    private int size;

    public MinamoByteArray(MinamoForeignTypeInfo typeInfo, byte[]? buffer) : base(typeInfo)
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

    public int Count => size;

    internal byte[] UnsafeAccess() => buffer;

    public override object ToObject() => GetBytes();

    public override int GetHashCode() => buffer.GetHashCode();

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public byte[] GetBytes()
    {
        var result = new byte[size];
        Array.Copy(buffer, result, size);
        return result;
    }

    public override MinamoObject Clone()
    {
        var clone = (MinamoByteArray)MemberwiseClone();

        if (buffer is not null)
        {
            clone.buffer = GetBytes();
        }

        return clone;
    }

}

[MinamoType]
public sealed partial class MinamoByteArrayTypeInfo : MinamoForeignTypeInfo
{
    private const string ByteArray = nameof(ByteArray);

    public override string ReflectedTypeName => ByteArray;

    public MinamoByteArrayTypeInfo()
    {
        SetSupportedOperations(Ops.Get | Ops.Len);
    }

    public MinamoByteArray Create(byte[]? buffer) => new(this, buffer);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format)
    {
        var builder = new StringBuilder("{");
        var bytes = (MinamoByteArray)arg;
        for (var i = 0; i < bytes.Count; i++)
        {
            if (builder.Length > 1)
            {
                builder.Append(',');
            }
            builder.Append("0x").Append(bytes.UnsafeAccess()[i].ToString("X2"));
        }
        return new MinamoString(builder.Append('}').ToString());
    }

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        MinamoInteger.Get(((MinamoByteArray)arg).Count);

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index)
    {
        if (index is not MinamoInteger integer || !integer.TryGetInt32(out var position))
        {
            return ctx.IndexOutOfRange(index);
        }

        var bytes = (MinamoByteArray)self;
        position = position < 0 ? bytes.Count + position : position;
        return position < 0 || position >= bytes.Count
            ? ctx.IndexOutOfRange(index)
            : MinamoInteger.Get(bytes.UnsafeAccess()[position]);
    }

    [MinamoMethod]
    internal static MinamoObject ToArray(MinamoByteArray self)
    {
        var values = new MinamoObject[self.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = MinamoInteger.Get(self.UnsafeAccess()[i]);
        }
        return new MinamoArray(values);
    }

    [MinamoMethod]
    internal static string ToHex(MinamoByteArray self, bool lowerCase = false)
    {
        var value = Convert.ToHexString(self.UnsafeAccess(), 0, self.Count);
        return lowerCase ? value.ToLowerInvariant() : value;
    }

    [MinamoMethod]
    internal static string ToBase64(MinamoByteArray self) => Convert.ToBase64String(self.UnsafeAccess(), 0, self.Count);

    [MinamoMethod]
    internal static MinamoObject Decode(ExecutionContext ctx, MinamoByteArray self, string encoding = "utf-8")
    {
        var selected = GetEncoding(ctx, encoding);
        if (selected is null)
        {
            return Nil;
        }

        try
        {
            return MinamoString.Get(selected.GetString(self.UnsafeAccess(), 0, self.Count));
        }
        catch (DecoderFallbackException)
        {
            return ctx.ParsingFailed();
        }
    }

    [MinamoStaticMethod]
    internal static MinamoObject Concat(ExecutionContext ctx, MinamoByteArray first, MinamoByteArray second)
    {
        var a1 = first.UnsafeAccess();
        var a2 = second.UnsafeAccess();
        var a3 = new byte[first.Count + second.Count];
        Array.Copy(a1, a3, first.Count);
        Array.Copy(a2, 0, a3, first.Count, second.Count);
        return new MinamoByteArray(ctx.Type<MinamoByteArrayTypeInfo>(), a3);
    }

    [MinamoStaticMethod(ByteArray)]
    internal static MinamoObject CreateNew(ExecutionContext ctx, [Default] MinamoObject values = null!) =>
        values is null || values.TypeId == MinamoTypeCodes.Nil
            ? new MinamoByteArray(ctx.Type<MinamoByteArrayTypeInfo>(), null)
            : FromArray(ctx, values);

    [MinamoStaticMethod]
    internal static MinamoObject FromArray(ExecutionContext ctx, MinamoObject values)
    {
        var result = new List<byte>();
        foreach (var value in MinamoIterator.ToEnumerable(ctx, values))
        {
            if (ctx.HasErrors)
            {
                return Nil;
            }

            if (value is not MinamoInteger integer || integer.Value is < byte.MinValue or > byte.MaxValue)
            {
                return ctx.InvalidValue(value);
            }
            result.Add((byte)integer.Value);
        }
        return new MinamoByteArray(ctx.Type<MinamoByteArrayTypeInfo>(), result.ToArray());
    }

    [MinamoStaticMethod]
    internal static MinamoObject FromHex(ExecutionContext ctx, string value)
    {
        try
        {
            return new MinamoByteArray(ctx.Type<MinamoByteArrayTypeInfo>(), Convert.FromHexString(value));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    [MinamoStaticMethod]
    internal static MinamoObject FromBase64(ExecutionContext ctx, string value)
    {
        try
        {
            return new MinamoByteArray(ctx.Type<MinamoByteArrayTypeInfo>(), Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    [MinamoStaticMethod]
    internal static MinamoObject FromString(ExecutionContext ctx, string value, string encoding = "utf-8")
    {
        var selected = GetEncoding(ctx, encoding);
        if (selected is null)
        {
            return Nil;
        }

        try
        {
            return new MinamoByteArray(ctx.Type<MinamoByteArrayTypeInfo>(), selected.GetBytes(value));
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
