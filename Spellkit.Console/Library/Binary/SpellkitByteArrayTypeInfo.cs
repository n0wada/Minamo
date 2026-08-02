using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Linq;
using System.Text;

namespace Spellkit.Library.Binary;

[SpellkitType]
public sealed partial class SpellkitByteArrayTypeInfo : SpellkitForeignTypeInfo
{
    private const string ByteArray = nameof(ByteArray);

    public override string ReflectedTypeName => ByteArray;

    public SpellkitByteArrayTypeInfo()
    {
        SetSupportedOperations(Ops.Len);
    }

    #region Operations
    public SpellkitByteArray Create(byte[]? buffer) => new(this, buffer);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        var buffer = ((SpellkitByteArray)arg).GetBytes();
        var strs = buffer.Select(b => "0x" + b.ToString("X").PadLeft(2, '0')).ToArray();
        return new SpellkitString("{" + string.Join(",", strs) + "}");
    }

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitByteArray)arg).Count);
    #endregion

    [SpellkitMethod]
    internal static SpellkitObject Read(
        ExecutionContext ctx,
        SpellkitByteArray self,
        SpellkitTypeInfo typeInfo,
        bool littleEndian = true) => self.Read(ctx, typeInfo, littleEndian);

    [SpellkitMethod]
    internal static void Write(
        ExecutionContext ctx,
        SpellkitByteArray self,
        SpellkitObject value,
        bool littleEndian = true) => self.Write(ctx, value, littleEndian);

    [SpellkitMethod]
    internal static void Reset(SpellkitByteArray self) => self.Reset();

    [SpellkitProperty]
    internal static int Position(SpellkitByteArray self) => self.Position;

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
    internal static SpellkitObject CreateNew(ExecutionContext ctx) => new SpellkitByteArray(ctx.Type<SpellkitByteArrayTypeInfo>(), null);

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
