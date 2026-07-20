using System.Collections.Generic;
using Spellkit.Codegen;
using System.Linq;
using System.Text;

namespace Spellkit.Runtime.Types;

public sealed class SpkString : SpkCollection
{
    public static readonly SpkString Empty = new("");

    public override string TypeName => nameof(Spk.String);

    public readonly string Value;

    private int hashCode;

    public override int Count => Value.Length;

    public SpkString(string str) : base(Spk.String) => Value = str;

    public SpkString(HashString str) : base(Spk.String) => (Value, hashCode) = ((string)str, str.LookupHash());

    public static SpkString Get(string? val) => string.IsNullOrEmpty(val) ? Empty : new(val);

    public override SpkObject[] ToArray()
    {
        var arr = new SpkObject[Value.Length];

        for (var i = 0; i < Value.Length; i++)
        {
            arr[i] = new SpkChar(Value[i]);
        }

        return arr;
    }

    private IEnumerable<SpkChar> Iterate()
    {
        for (var i = 0; i < Value.Length; i++)
        {
            yield return new SpkChar(Value[i]);
        }
    }

    public override IEnumerator<SpkObject> GetEnumerator() => Iterate().GetEnumerator();

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

    public override bool Equals(SpkObject? obj) => obj is SpkString s && Value == s.Value;

    public override SpkObject Clone() => this;

    public static explicit operator string(SpkString str) => str.Value;

    protected internal override SpkObject[] UnsafeAccess() => throw new NotImplementedException();
}

[SpkType]
internal sealed partial class SpkStringTypeInfo : SpkCollTypeInfo
{
    readonly struct FormatData : IFormattable
    {
        public readonly SpkObject Object;
        public readonly ExecutionContext Context;

        public FormatData(SpkObject obj, ExecutionContext ctx) =>
            (Object, Context) = (obj, ctx);

        public string ToString(string? format, IFormatProvider? formatProvider) =>
            Object.ToString(Context, SpkString.Get(format)).ToString();
    }

    public override string ReflectedTypeName => nameof(Spk.String);

    public override int ReflectedTypeId => Spk.String;

    public SpkStringTypeInfo()
    {
        AddMixins(Spk.Lookup, Spk.Order, Spk.Equatable, Spk.Sequence);
        SetSupportedOperations(Ops.Add);
    }

    #region Operations
    protected override SpkObject AddOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        try
        {
            var other = right.TypeId == Spk.String || right.TypeId == Spk.Char ? right.ToString() : right.ToString(ctx).Value;
            return new SpkString(((SpkString)left).Value + other);
        }
        catch (SpkCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == Spk.Char)
        {
            return ((SpkString)left).Value == right.ToString() ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override SpkObject NeqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == Spk.Char)
        {
            return ((SpkString)left).Value != right.ToString() ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override SpkObject GtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == Spk.Char)
        {
            return ((SpkString)left).Value.CompareTo(right.ToString()) > 0 ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override SpkObject LtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == Spk.Char)
        {
            return ((SpkString)left).Value.CompareTo(right.ToString()) < 0 ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject arg)
    {
        var len = ((SpkString)arg).Value.Length;
        return SpkInteger.Get(len);
    }

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format) => arg;

    protected override SpkObject GetOp(ExecutionContext ctx, SpkObject self, SpkObject index)
    {
        if (index is not SpkInteger i)
        {
            return ctx.IndexOutOfRange(index);
        }

        var str = (SpkString)self;
        if (!i.TryGetInt32(out var ix))
        {
            return ctx.IndexOutOfRange(index);
        }

        ix = CorrectIndex(ix, str.Value);

        if (ix < 0 || ix >= str.Count)
        {
            return ctx.IndexOutOfRange(index);
        }

        return new SpkChar(str.Value[ix]);
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            Spk.Integer => long.TryParse(self.ToString(), out var i8) ? new SpkInteger(i8) : SpkInteger.Zero,
            Spk.Float => double.TryParse(self.ToString(), out var r8) ? new SpkFloat(r8) : SpkFloat.Zero,
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    private static int CorrectIndex(int index, string str) => index < 0 ? index + str.Length : index;

    [SpkMethod]
    internal static bool Contains(string self, string field) => self.Contains(field);

    [SpkMethod(BuiltinMethodNames.Slice)]
    internal static SpkObject Slice(SpkString self, int index = 0, int? size = null)
    {
        index = CorrectIndex(index, self.Value);
        size ??= self.Count - 1;

        if (index == 0 && size == self.Count - 1)
        {
            return self;
        }

        if (index >= self.Count)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, index);
        }

        if (size < 0)
        {
            size = self.Count + size - 1;
        }

        if (size >= self.Count)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange, size);
        }

        var len = size.Value - index + 1;

        if (len < 0)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        if (len == 0)
        {
            return SpkString.Empty;
        }

        return new SpkString(self.Value.Substring(index, len));
    }

    [SpkMethod(BuiltinMethodNames.IndexOf)]
    internal static int IndexOf(string self, string value, int index = 0, int? count = null)
    {
        index = CorrectIndex(index, self);
        count ??= self.Length - index;

        if (index < 0 || index > self.Length || count < 0 || count > self.Length - index)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        return self.IndexOf(value, index, count.Value);
    }

    [SpkMethod(BuiltinMethodNames.LastIndexOf)]
    internal static int LastIndexOf(string self, string value, int? index = null, int? count = null)
    {
        index ??= self.Length - 1;
        index = CorrectIndex(index.Value, self);
        count ??= index + 1;

        if (index < 0 || index > self.Length || count < 0 || index - count + 1 < 0)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        return self.LastIndexOf(value, index.Value, count.Value);
    }

    [SpkMethod(BuiltinMethodNames.Split)]
    internal static string[] Split(string self, params string[] separators) =>
        self.Split(separators, StringSplitOptions.RemoveEmptyEntries);

    [SpkMethod(BuiltinMethodNames.Capitalize)]
    internal static string Capitalize(string self) =>
        self.Length == 0 ? "" : char.ToUpper(self[0]) + self[1..].ToLower();

    [SpkMethod(BuiltinMethodNames.Upper)]
    internal static string Upper(string self) => self.ToUpper();

    [SpkMethod(BuiltinMethodNames.Lower)]
    internal static string Lower(string self) => self.ToLower();

    [SpkMethod(BuiltinMethodNames.StartsWith)]
    internal static bool StartsWith(string self, string value) => self.StartsWith(value);

    [SpkMethod(BuiltinMethodNames.EndsWith)]
    internal static bool EndsWith(string self, string value) => self.EndsWith(value);

    [SpkMethod(BuiltinMethodNames.EnumerateRunes)]
    internal static IEnumerable<SpkObject> EnumerateRunes(string self)
    {
        foreach (var rune in self.EnumerateRunes())
        {
            yield return SpkString.Get(rune.ToString());
        }
    }

    [SpkMethod(BuiltinMethodNames.RuneCount)]
    internal static int RuneCount(string self)
    {
        var count = 0;

        foreach (var _ in self.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    [SpkMethod(BuiltinMethodNames.Substring)]
    internal static string? Substring(string self, int index, int? count = null)
    {
        index = CorrectIndex(index, self);

        if (index >= self.Length)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        if (count is null)
        {
            return self[index..];
        }

        if (count < 0 || count + index > self.Length)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        return self.Substring(index, count.Value);
    }

    [SpkMethod(BuiltinMethodNames.Trim)]
    internal static string Trim(string self, params char[] chars) => self.Trim(chars);

    [SpkMethod(BuiltinMethodNames.TrimStart)]
    internal static string TrimStart(string self, params char[] chars) => self.TrimStart(chars);

    [SpkMethod(BuiltinMethodNames.TrimEnd)]
    internal static string TrimEnd(string self, params char[] chars) => self.TrimEnd(chars);

    [SpkMethod(BuiltinMethodNames.IsEmpty)]
    internal static bool IsEmpty(string self) => string.IsNullOrWhiteSpace(self);

    [SpkMethod(BuiltinMethodNames.PadLeft)]
    internal static string PadLeft(string self, int width, [ParameterName("char")] char c = ' ') =>
        self.PadLeft(width, c);

    [SpkMethod(BuiltinMethodNames.PadRight)]
    internal static string PadRight(string self, int width, [ParameterName("char")] char c = ' ') =>
        self.PadRight(width, c);

    [SpkMethod(BuiltinMethodNames.Replace)]
    internal static string Replace(string self, string value, string other, bool ignoreCase = false) =>
        self.Replace(value, other, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    [SpkMethod(BuiltinMethodNames.Remove)]
    internal static string? Remove(string self, int index, int? count = null)
    {
        count ??= self.Length - index;

        if (index + count > self.Length)
        {
            throw new SpkCodeException(SpkError.IndexOutOfRange);
        }

        return self.Remove(index, count.Value);
    }

    [SpkMethod(BuiltinMethodNames.Reverse)]
    internal static string Reverse(string self)
    {
        var sb = new StringBuilder(self.Length);

        for (var i = 0; i < self.Length; i++)
        {
            sb.Append(self[self.Length - i - 1]);
        }

        return sb.ToString();
    }

    [SpkMethod(BuiltinMethodNames.ToCharArray)]
    internal static SpkObject ToCharArray(string self) =>
        new SpkArray(self.ToCharArray().Select(c => new SpkChar(c)).ToArray());

    [SpkMethod(BuiltinMethodNames.Format)]
    internal static string? Format(ExecutionContext ctx, string self, params SpkObject[] values)
    {
        var arr = new object[values.Length];

        for (var i = 0; i < values.Length; i++)
        {
            arr[i] = new FormatData(values[i], ctx);
        }

        return string.Format(self, arr);
    }

    [SpkStaticMethod(BuiltinMethodNames.Concat)]
    internal static string? Concat(ExecutionContext ctx, params SpkObject[] values)
    {
        var xs = new List<string>();
        Collect(ctx, values, xs);
        return string.Concat(xs);
    }

    [SpkStaticMethod(BuiltinMethodNames.String)]
    internal static string? New(ExecutionContext ctx, params SpkObject[] values) => Concat(ctx, values);

    private static void Collect(ExecutionContext ctx, SpkObject[] values, List<string> xs)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var a = values[i];

            if (a.TypeId is Spk.String or Spk.Char)
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

    [SpkStaticMethod(BuiltinMethodNames.Join)]
    internal static string? Join(ExecutionContext ctx, [VarArg]SpkObject[] values, string separator = ",")
    {
        var xs = new List<string>();
        Collect(ctx, values, xs);
        return string.Join(separator, xs);
    }

    [SpkStaticProperty(BuiltinMethodNames.Empty)]
    internal static SpkObject Empty() => SpkString.Empty;

    [SpkStaticProperty(BuiltinMethodNames.Default)]
    internal static SpkObject Default() => SpkString.Empty;

    [SpkStaticMethod(BuiltinMethodNames.Repeat)]
    internal static string Repeat(string value, int count)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < count; i++)
        {
            sb.Append(value);
        }

        return sb.ToString();
    }

    [SpkStaticMethod(BuiltinMethodNames.Format)]
    internal static string? StaticFormat(ExecutionContext ctx, string template, params SpkObject[] values) => Format(ctx, template, values);
}
