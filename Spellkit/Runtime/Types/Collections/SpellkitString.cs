using System.Collections.Generic;
using Spellkit.Codegen;
using System.Linq;
using System.Text;

namespace Spellkit.Runtime.Types;

public sealed class SpellkitString : SpellkitCollection
{
    public static readonly SpellkitString Empty = new("");

    public override string TypeName => nameof(SpellkitTypeCodes.String);

    public readonly string Value;

    private int hashCode;

    public override int Count => Value.Length;

    public SpellkitString(string str) : base(SpellkitTypeCodes.String) => Value = str;

    public SpellkitString(HashString str) : base(SpellkitTypeCodes.String) => (Value, hashCode) = ((string)str, str.LookupHash());

    public static SpellkitString Get(string? val) => string.IsNullOrEmpty(val) ? Empty : new(val);

    public override SpellkitObject[] ToArray()
    {
        var arr = new SpellkitObject[Value.Length];

        for (var i = 0; i < Value.Length; i++)
        {
            arr[i] = new SpellkitChar(Value[i]);
        }

        return arr;
    }

    private IEnumerable<SpellkitChar> Iterate()
    {
        for (var i = 0; i < Value.Length; i++)
        {
            yield return new SpellkitChar(Value[i]);
        }
    }

    public override IEnumerator<SpellkitObject> GetEnumerator() => Iterate().GetEnumerator();

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

    public override bool Equals(SpellkitObject? obj) => obj is SpellkitString s && Value == s.Value;

    public override SpellkitObject Clone() => this;

    public static explicit operator string(SpellkitString str) => str.Value;

    protected internal override SpellkitObject[] UnsafeAccess() => throw new NotImplementedException();
}

[SpellkitType]
internal sealed partial class SpellkitStringTypeInfo : SpellkitCollTypeInfo
{
    readonly struct FormatData : IFormattable
    {
        public readonly SpellkitObject Object;
        public readonly ExecutionContext Context;

        public FormatData(SpellkitObject obj, ExecutionContext ctx) =>
            (Object, Context) = (obj, ctx);

        public string ToString(string? format, IFormatProvider? formatProvider) =>
            Object.ToString(Context, SpellkitString.Get(format)).ToString();
    }

    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.String);

    public override int ReflectedTypeId => SpellkitTypeCodes.String;

    public SpellkitStringTypeInfo()
    {
        AddMixins(SpellkitTypeCodes.Lookup, SpellkitTypeCodes.Order, SpellkitTypeCodes.Equatable, SpellkitTypeCodes.Sequence);
        SetSupportedOperations(Ops.Add);
    }

    #region Operations
    protected override SpellkitObject AddOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        try
        {
            var other = right.TypeId == SpellkitTypeCodes.String || right.TypeId == SpellkitTypeCodes.Char ? right.ToString() : right.ToString(ctx).Value;
            return new SpellkitString(((SpellkitString)left).Value + other);
        }
        catch (SpellkitCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == SpellkitTypeCodes.Char)
        {
            return ((SpellkitString)left).Value == right.ToString() ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override SpellkitObject NeqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == SpellkitTypeCodes.Char)
        {
            return ((SpellkitString)left).Value != right.ToString() ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override SpellkitObject GtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == SpellkitTypeCodes.Char)
        {
            return ((SpellkitString)left).Value.CompareTo(right.ToString()) > 0 ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override SpellkitObject LtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId == right.TypeId || right.TypeId == SpellkitTypeCodes.Char)
        {
            return ((SpellkitString)left).Value.CompareTo(right.ToString()) < 0 ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg)
    {
        var len = ((SpellkitString)arg).Value.Length;
        return SpellkitInteger.Get(len);
    }

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) => arg;

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index)
    {
        if (index is not SpellkitInteger i)
        {
            return ctx.IndexOutOfRange(index);
        }

        var str = (SpellkitString)self;
        if (!i.TryGetInt32(out var ix))
        {
            return ctx.IndexOutOfRange(index);
        }

        ix = CorrectIndex(ix, str.Value);

        if (ix < 0 || ix >= str.Count)
        {
            return ctx.IndexOutOfRange(index);
        }

        return new SpellkitChar(str.Value[ix]);
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Integer => long.TryParse(self.ToString(), out var i8) ? new SpellkitInteger(i8) : SpellkitInteger.Zero,
            SpellkitTypeCodes.Float => double.TryParse(self.ToString(), out var r8) ? new SpellkitFloat(r8) : SpellkitFloat.Zero,
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    private static int CorrectIndex(int index, string str) => index < 0 ? index + str.Length : index;

    [SpellkitMethod]
    internal static bool Contains(string self, string field) => self.Contains(field);

    [SpellkitMethod(BuiltinMethodNames.Slice)]
    internal static SpellkitObject Slice(SpellkitString self, int index = 0, int? size = null)
    {
        index = CorrectIndex(index, self.Value);
        size ??= self.Count - 1;

        if (index == 0 && size == self.Count - 1)
        {
            return self;
        }

        if (index >= self.Count)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, index);
        }

        if (size < 0)
        {
            size = self.Count + size - 1;
        }

        if (size >= self.Count)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange, size);
        }

        var len = size.Value - index + 1;

        if (len < 0)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        if (len == 0)
        {
            return SpellkitString.Empty;
        }

        return new SpellkitString(self.Value.Substring(index, len));
    }

    [SpellkitMethod(BuiltinMethodNames.IndexOf)]
    internal static int IndexOf(string self, string value, int index = 0, int? count = null)
    {
        index = CorrectIndex(index, self);
        count ??= self.Length - index;

        if (index < 0 || index > self.Length || count < 0 || count > self.Length - index)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        return self.IndexOf(value, index, count.Value);
    }

    [SpellkitMethod(BuiltinMethodNames.LastIndexOf)]
    internal static int LastIndexOf(string self, string value, int? index = null, int? count = null)
    {
        index ??= self.Length - 1;
        index = CorrectIndex(index.Value, self);
        count ??= index + 1;

        if (index < 0 || index > self.Length || count < 0 || index - count + 1 < 0)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        return self.LastIndexOf(value, index.Value, count.Value);
    }

    [SpellkitMethod(BuiltinMethodNames.Split)]
    internal static string[] Split(string self, params string[] separators) =>
        self.Split(separators, StringSplitOptions.RemoveEmptyEntries);

    [SpellkitMethod(BuiltinMethodNames.Capitalize)]
    internal static string Capitalize(string self) =>
        self.Length == 0 ? "" : char.ToUpper(self[0]) + self[1..].ToLower();

    [SpellkitMethod(BuiltinMethodNames.Upper)]
    internal static string Upper(string self) => self.ToUpper();

    [SpellkitMethod(BuiltinMethodNames.Lower)]
    internal static string Lower(string self) => self.ToLower();

    [SpellkitMethod(BuiltinMethodNames.StartsWith)]
    internal static bool StartsWith(string self, string value) => self.StartsWith(value);

    [SpellkitMethod(BuiltinMethodNames.EndsWith)]
    internal static bool EndsWith(string self, string value) => self.EndsWith(value);

    [SpellkitMethod(BuiltinMethodNames.EnumerateRunes)]
    internal static IEnumerable<SpellkitObject> EnumerateRunes(string self)
    {
        foreach (var rune in self.EnumerateRunes())
        {
            yield return SpellkitString.Get(rune.ToString());
        }
    }

    [SpellkitMethod(BuiltinMethodNames.RuneCount)]
    internal static int RuneCount(string self)
    {
        var count = 0;

        foreach (var _ in self.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    [SpellkitMethod(BuiltinMethodNames.Substring)]
    internal static string? Substring(string self, int index, int? count = null)
    {
        index = CorrectIndex(index, self);

        if (index >= self.Length)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        if (count is null)
        {
            return self[index..];
        }

        if (count < 0 || count + index > self.Length)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        return self.Substring(index, count.Value);
    }

    [SpellkitMethod(BuiltinMethodNames.Trim)]
    internal static string Trim(string self, params char[] chars) => self.Trim(chars);

    [SpellkitMethod(BuiltinMethodNames.TrimStart)]
    internal static string TrimStart(string self, params char[] chars) => self.TrimStart(chars);

    [SpellkitMethod(BuiltinMethodNames.TrimEnd)]
    internal static string TrimEnd(string self, params char[] chars) => self.TrimEnd(chars);

    [SpellkitMethod(BuiltinMethodNames.IsEmpty)]
    internal static bool IsEmpty(string self) => string.IsNullOrWhiteSpace(self);

    [SpellkitMethod(BuiltinMethodNames.PadLeft)]
    internal static string PadLeft(string self, int width, [ParameterName("char")] char c = ' ') =>
        self.PadLeft(width, c);

    [SpellkitMethod(BuiltinMethodNames.PadRight)]
    internal static string PadRight(string self, int width, [ParameterName("char")] char c = ' ') =>
        self.PadRight(width, c);

    [SpellkitMethod(BuiltinMethodNames.Replace)]
    internal static string Replace(string self, string value, string other, bool ignoreCase = false) =>
        self.Replace(value, other, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    [SpellkitMethod(BuiltinMethodNames.Remove)]
    internal static string? Remove(string self, int index, int? count = null)
    {
        count ??= self.Length - index;

        if (index + count > self.Length)
        {
            throw new SpellkitCodeException(SpellkitError.IndexOutOfRange);
        }

        return self.Remove(index, count.Value);
    }

    [SpellkitMethod(BuiltinMethodNames.Reverse)]
    internal static string Reverse(string self)
    {
        var sb = new StringBuilder(self.Length);

        for (var i = 0; i < self.Length; i++)
        {
            sb.Append(self[self.Length - i - 1]);
        }

        return sb.ToString();
    }

    [SpellkitMethod(BuiltinMethodNames.ToCharArray)]
    internal static SpellkitObject ToCharArray(string self) =>
        new SpellkitArray(self.ToCharArray().Select(c => new SpellkitChar(c)).ToArray());

    [SpellkitMethod(BuiltinMethodNames.Format)]
    internal static string? Format(ExecutionContext ctx, string self, params SpellkitObject[] values)
    {
        var arr = new object[values.Length];

        for (var i = 0; i < values.Length; i++)
        {
            arr[i] = new FormatData(values[i], ctx);
        }

        return string.Format(self, arr);
    }

    [SpellkitStaticMethod(BuiltinMethodNames.Concat)]
    internal static string? Concat(ExecutionContext ctx, params SpellkitObject[] values)
    {
        var xs = new List<string>();
        Collect(ctx, values, xs);
        return string.Concat(xs);
    }

    [SpellkitStaticMethod(BuiltinMethodNames.String)]
    internal static string? New(ExecutionContext ctx, params SpellkitObject[] values) => Concat(ctx, values);

    private static void Collect(ExecutionContext ctx, SpellkitObject[] values, List<string> xs)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var a = values[i];

            if (a.TypeId is SpellkitTypeCodes.String or SpellkitTypeCodes.Char)
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

    [SpellkitStaticMethod(BuiltinMethodNames.Join)]
    internal static string? Join(ExecutionContext ctx, [VarArg]SpellkitObject[] values, string separator = ",")
    {
        var xs = new List<string>();
        Collect(ctx, values, xs);
        return string.Join(separator, xs);
    }

    [SpellkitStaticProperty(BuiltinMethodNames.Empty)]
    internal static SpellkitObject Empty() => SpellkitString.Empty;

    [SpellkitStaticProperty(BuiltinMethodNames.Default)]
    internal static SpellkitObject Default() => SpellkitString.Empty;

    [SpellkitStaticMethod(BuiltinMethodNames.Repeat)]
    internal static string Repeat(string value, int count)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < count; i++)
        {
            sb.Append(value);
        }

        return sb.ToString();
    }

    [SpellkitStaticMethod(BuiltinMethodNames.Format)]
    internal static string? StaticFormat(ExecutionContext ctx, string template, params SpellkitObject[] values) => Format(ctx, template, values);
}
