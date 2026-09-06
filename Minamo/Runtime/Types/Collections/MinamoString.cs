using System.Collections.Generic;
using Minamo.Codegen;
using System.Linq;
using System.Text;

namespace Minamo.Runtime.Types;

public sealed class MinamoString : MinamoCollection
{
    public static readonly MinamoString Empty = new("");

    public override string TypeName => nameof(MinamoTypeCodes.String);

    public readonly string Value;

    private int hashCode;

    public override int Count => Value.Length;

    public MinamoString(string str) : base(MinamoTypeCodes.String) => Value = str;

    public MinamoString(HashString str) : base(MinamoTypeCodes.String) => (Value, hashCode) = ((string)str, str.LookupHash());

    public static MinamoString Get(string? val) => string.IsNullOrEmpty(val) ? Empty : new(val);

    public override MinamoObject[] ToArray()
    {
        var arr = new MinamoObject[Value.Length];

        for (var i = 0; i < Value.Length; i++)
        {
            arr[i] = new MinamoChar(Value[i]);
        }

        return arr;
    }

    private IEnumerable<MinamoChar> Iterate()
    {
        for (var i = 0; i < Value.Length; i++)
        {
            yield return new MinamoChar(Value[i]);
        }
    }

    public override IEnumerator<MinamoObject> GetEnumerator() => Iterate().GetEnumerator();

    public override object ToObject() => Value;

    public override string ToString() => Value;

    public override int GetHashCode()
    {
        if (hashCode == 0)
        {
            hashCode = Value.GetHashCode();
        }

        return hashCode;
    }

    public override bool Equals(MinamoObject? obj) => obj is MinamoString s && Value == s.Value;

    public override MinamoObject Clone() => this;

    public static explicit operator string(MinamoString str) => str.Value;

}

[MinamoType]
internal sealed partial class MinamoStringTypeInfo : MinamoCollTypeInfo
{
    readonly struct FormatData : IFormattable
    {
        public readonly MinamoObject Object;
        public readonly ExecutionContext Context;

        public FormatData(MinamoObject obj, ExecutionContext ctx) =>
            (Object, Context) = (obj, ctx);

        public string ToString(string? format, IFormatProvider? formatProvider) =>
            Object.ToString(Context, MinamoString.Get(format)).ToString();
    }

    public override string ReflectedTypeName => nameof(MinamoTypeCodes.String);

    public override int ReflectedTypeId => MinamoTypeCodes.String;

    public MinamoStringTypeInfo()
    {
        AddMixins(MinamoTypeCodes.Lookup, MinamoTypeCodes.Order, MinamoTypeCodes.Equatable, MinamoTypeCodes.Sequence);
        SetSupportedOperations(Ops.Add);
    }

    #region Operations
    protected override MinamoObject AddOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        try
        {
            var other = right.TypeId == MinamoTypeCodes.String || right.TypeId == MinamoTypeCodes.Char ? right.ToString() : right.ToString(ctx).Value;
            return new MinamoString(((MinamoString)left).Value + other);
        }
        catch (MinamoCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == MinamoTypeCodes.Char)
        {
            return ((MinamoString)left).Value == right.ToString() ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override MinamoObject NeqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == MinamoTypeCodes.Char)
        {
            return ((MinamoString)left).Value != right.ToString() ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override MinamoObject GtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == MinamoTypeCodes.Char)
        {
            return ((MinamoString)left).Value.CompareTo(right.ToString()) > 0 ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override MinamoObject LtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == MinamoTypeCodes.Char)
        {
            return ((MinamoString)left).Value.CompareTo(right.ToString()) < 0 ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg)
    {
        var len = ((MinamoString)arg).Value.Length;
        return MinamoInteger.Get(len);
    }

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) => arg;

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index)
    {
        if (index is not MinamoInteger i)
        {
            return ctx.IndexOutOfRange(index);
        }

        var str = (MinamoString)self;
        if (!i.TryGetInt32(out var ix))
        {
            return ctx.IndexOutOfRange(index);
        }

        ix = CorrectIndex(ix, str.Value);

        if (ix < 0 || ix >= str.Count)
        {
            return ctx.IndexOutOfRange(index);
        }

        return new MinamoChar(str.Value[ix]);
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            MinamoTypeCodes.Integer => long.TryParse(self.ToString(), out var i8) ? new MinamoInteger(i8) : MinamoInteger.Zero,
            MinamoTypeCodes.Float => double.TryParse(self.ToString(), out var r8) ? new MinamoFloat(r8) : MinamoFloat.Zero,
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    private static int CorrectIndex(int index, string str) => index < 0 ? index + str.Length : index;

    [MinamoMethod]
    internal static bool Contains(string self, string field) => self.Contains(field);

    [MinamoMethod(BuiltinMethodNames.Slice)]
    internal static MinamoObject Slice(MinamoString self, int index = 0, int? size = null)
    {
        index = CorrectIndex(index, self.Value);
        size ??= self.Count - 1;

        if (index == 0 && size == self.Count - 1)
        {
            return self;
        }

        if (index < 0 || index >= self.Count)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, index);
        }

        if (size < 0)
        {
            size = self.Count + size - 1;
        }

        if (size >= self.Count)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange, size);
        }

        var len = size.Value - index + 1;

        if (len < 0)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        if (len == 0)
        {
            return MinamoString.Empty;
        }

        return new MinamoString(self.Value.Substring(index, len));
    }

    [MinamoMethod(BuiltinMethodNames.IndexOf)]
    internal static int IndexOf(string self, string value, int index = 0, int? count = null)
    {
        index = CorrectIndex(index, self);
        count ??= self.Length - index;

        if (index < 0 || index > self.Length || count < 0 || count > self.Length - index)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        return self.IndexOf(value, index, count.Value);
    }

    [MinamoMethod(BuiltinMethodNames.LastIndexOf)]
    internal static int LastIndexOf(string self, string value, int? index = null, int? count = null)
    {
        index ??= self.Length - 1;
        index = CorrectIndex(index.Value, self);
        count ??= index + 1;

        if (index < 0 || index > self.Length || count < 0 || index - count + 1 < 0)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        return self.LastIndexOf(value, index.Value, count.Value);
    }

    [MinamoMethod(BuiltinMethodNames.Split)]
    internal static string[] Split(string self, params string[] separators) =>
        self.Split(separators, StringSplitOptions.RemoveEmptyEntries);

    [MinamoMethod(BuiltinMethodNames.Capitalize)]
    internal static string Capitalize(string self) =>
        self.Length == 0 ? "" : char.ToUpper(self[0]) + self[1..].ToLower();

    [MinamoMethod(BuiltinMethodNames.Upper)]
    internal static string Upper(string self) => self.ToUpper();

    [MinamoMethod(BuiltinMethodNames.Lower)]
    internal static string Lower(string self) => self.ToLower();

    [MinamoMethod(BuiltinMethodNames.StartsWith)]
    internal static bool StartsWith(string self, string value) => self.StartsWith(value);

    [MinamoMethod(BuiltinMethodNames.EndsWith)]
    internal static bool EndsWith(string self, string value) => self.EndsWith(value);

    [MinamoMethod(BuiltinMethodNames.EnumerateRunes)]
    internal static IEnumerable<MinamoObject> EnumerateRunes(string self)
    {
        foreach (var rune in self.EnumerateRunes())
        {
            yield return MinamoString.Get(rune.ToString());
        }
    }

    [MinamoMethod(BuiltinMethodNames.RuneCount)]
    internal static int RuneCount(string self)
    {
        var count = 0;

        foreach (var _ in self.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    [MinamoMethod(BuiltinMethodNames.Substring)]
    internal static string? Substring(string self, int index, int? count = null)
    {
        index = CorrectIndex(index, self);

        if (index >= self.Length)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        if (count is null)
        {
            return self[index..];
        }

        if (count < 0 || count + index > self.Length)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        return self.Substring(index, count.Value);
    }

    [MinamoMethod(BuiltinMethodNames.Trim)]
    internal static string Trim(string self, params char[] chars) => self.Trim(chars);

    [MinamoMethod(BuiltinMethodNames.TrimStart)]
    internal static string TrimStart(string self, params char[] chars) => self.TrimStart(chars);

    [MinamoMethod(BuiltinMethodNames.TrimEnd)]
    internal static string TrimEnd(string self, params char[] chars) => self.TrimEnd(chars);

    [MinamoMethod(BuiltinMethodNames.IsEmpty)]
    internal static bool IsEmpty(string self) => string.IsNullOrWhiteSpace(self);

    [MinamoMethod(BuiltinMethodNames.PadLeft)]
    internal static string PadLeft(string self, int width, [ParameterName("char")] char c = ' ') =>
        self.PadLeft(width, c);

    [MinamoMethod(BuiltinMethodNames.PadRight)]
    internal static string PadRight(string self, int width, [ParameterName("char")] char c = ' ') =>
        self.PadRight(width, c);

    [MinamoMethod(BuiltinMethodNames.Replace)]
    internal static string Replace(string self, string value, string other, bool ignoreCase = false) =>
        self.Replace(value, other, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    [MinamoMethod(BuiltinMethodNames.Remove)]
    internal static string? Remove(string self, int index, int? count = null)
    {
        count ??= self.Length - index;

        if (index + count > self.Length)
        {
            throw new MinamoCodeException(MinamoError.IndexOutOfRange);
        }

        return self.Remove(index, count.Value);
    }

    [MinamoMethod(BuiltinMethodNames.Reverse)]
    internal static string Reverse(string self)
    {
        var sb = new StringBuilder(self.Length);

        for (var i = 0; i < self.Length; i++)
        {
            sb.Append(self[self.Length - i - 1]);
        }

        return sb.ToString();
    }

    [MinamoMethod(BuiltinMethodNames.ToCharArray)]
    internal static MinamoObject ToCharArray(string self) =>
        new MinamoArray(self.ToCharArray().Select(c => new MinamoChar(c)).ToArray());

    [MinamoMethod(BuiltinMethodNames.Format)]
    internal static string? Format(ExecutionContext ctx, string self, params MinamoObject[] values)
    {
        var arr = new object[values.Length];

        for (var i = 0; i < values.Length; i++)
        {
            arr[i] = new FormatData(values[i], ctx);
        }

        return string.Format(self, arr);
    }

    [MinamoStaticMethod(BuiltinMethodNames.Concat)]
    internal static string? Concat(ExecutionContext ctx, params MinamoObject[] values)
    {
        var xs = new List<string>();
        Collect(ctx, values, xs);
        return string.Concat(xs);
    }

    [MinamoStaticMethod(BuiltinMethodNames.String)]
    internal static string? New(ExecutionContext ctx, params MinamoObject[] values) => Concat(ctx, values);

    private static void Collect(ExecutionContext ctx, MinamoObject[] values, List<string> xs)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var a = values[i];

            if (a.TypeId is MinamoTypeCodes.String or MinamoTypeCodes.Char)
            {
                xs.Add(a.ToString());
            }
            else
            {
                var res = a.ToString(ctx);
                xs.Add(res.Value);
            }
        }
    }

    [MinamoStaticMethod(BuiltinMethodNames.Join)]
    internal static string? Join(ExecutionContext ctx, [VarArg]MinamoObject[] values, string separator = ",")
    {
        var xs = new List<string>();
        Collect(ctx, values, xs);
        return string.Join(separator, xs);
    }

    [MinamoStaticProperty(BuiltinMethodNames.Empty)]
    internal static MinamoObject Empty() => MinamoString.Empty;

    [MinamoStaticProperty(BuiltinMethodNames.Default)]
    internal static MinamoObject Default() => MinamoString.Empty;

    [MinamoStaticMethod(BuiltinMethodNames.Repeat)]
    internal static string Repeat(string value, int count)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < count; i++)
        {
            sb.Append(value);
        }

        return sb.ToString();
    }

    [MinamoStaticMethod(BuiltinMethodNames.Format)]
    internal static string? StaticFormat(ExecutionContext ctx, string template, params MinamoObject[] values) => Format(ctx, template, values);
}
