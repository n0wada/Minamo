using Spellkit.Codegen;

namespace Spellkit.Runtime.Types;

public sealed class SpellkitChar : SpellkitObject
{
    public static readonly SpellkitChar WhiteSpace = new(' ');
    public static readonly SpellkitChar Empty = new('\0');
    public static readonly SpellkitChar Max = new(char.MaxValue);
    public static readonly SpellkitChar Min = new(char.MinValue);

    internal readonly char Value;

    public override string TypeName => nameof(SpellkitTypeCodes.Char);

    public SpellkitChar(char value) : base(SpellkitTypeCodes.Char) => this.Value = value;

    public override object ToObject() => Value;

    public override string ToString() => Value.ToString();

    public override SpellkitObject Clone() => this;

    public override bool Equals(SpellkitObject? other) => other is SpellkitChar c && c.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();
}

[SpellkitType]
internal sealed partial class SpellkitCharTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Char);

    public override int ReflectedTypeId => SpellkitTypeCodes.Char;

    public SpellkitCharTypeInfo() => AddMixins(SpellkitTypeCodes.Order, SpellkitTypeCodes.Equatable);

    #region Operations
    protected override SpellkitObject AddOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i)
        {
            return new SpellkitChar((char)(((SpellkitChar)left).Value + i.Value));
        }

        if (right.TypeId is SpellkitTypeCodes.Char)
        {
            return new SpellkitString(left.ToString() + right.ToString());
        }

        return base.AddOp(ctx, left, right);
    }

    protected override SpellkitObject SubOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i)
        {
            return new SpellkitChar((char)(((SpellkitChar)left).Value - i.Value));
        }

        if (right is SpellkitChar c)
        {
            return new SpellkitChar((char)(((SpellkitChar)left).Value - c.Value));
        }

        return base.SubOp(ctx, left, right);
    }

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((SpellkitChar)left).Value == ((SpellkitChar)right).Value ? True : False;
        }

        if (right is SpellkitString str)
        {
            return str.Value.Length == 1 && ((SpellkitChar)left).Value == str.Value[0] ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override SpellkitObject NeqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((SpellkitChar)left).Value != ((SpellkitChar)right).Value ? True : False;
        }

        if (right is SpellkitString str)
        {
            return str.Value.Length != 1 || ((SpellkitChar)left).Value != str.Value[0] ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override SpellkitObject GtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((SpellkitChar)left).Value.CompareTo(((SpellkitChar)right).Value) > 0 ? True : False;
        }

        if (right is SpellkitString str)
        {
            return ((SpellkitChar)left).Value.ToString().CompareTo(str.Value) > 0 ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override SpellkitObject LtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((SpellkitChar)left).Value.CompareTo(((SpellkitChar)right).Value) < 0 ? True : False;
        }

        if (right is SpellkitString str)
        {
            return ((SpellkitChar)left).Value.ToString().CompareTo(str.Value) < 0 ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Integer => SpellkitInteger.Get(((SpellkitChar)self).Value),
            SpellkitTypeCodes.Float => new SpellkitFloat(((SpellkitChar)self).Value),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [SpellkitMethod(BuiltinMethodNames.IsLower)]
    internal static bool IsLower(char self) => char.IsLower(self);

    [SpellkitMethod(BuiltinMethodNames.IsUpper)]
    internal static bool IsUpper(char self) => char.IsUpper(self);

    [SpellkitMethod(BuiltinMethodNames.IsControl)]
    internal static bool IsControl(char self) => char.IsControl(self);

    [SpellkitMethod(BuiltinMethodNames.IsDigit)]
    internal static bool IsDigit(char self) => char.IsDigit(self);

    [SpellkitMethod(BuiltinMethodNames.IsLetter)]
    internal static bool IsLetter(char self) => char.IsLetter(self);

    [SpellkitMethod(BuiltinMethodNames.IsLetterOrDigit)]
    internal static bool IsLetterOrDigit(char self) => char.IsLetterOrDigit(self);

    [SpellkitMethod(BuiltinMethodNames.IsWhiteSpace)]
    internal static bool IsWhiteSpace(char self) => char.IsWhiteSpace(self);

    [SpellkitMethod(BuiltinMethodNames.Lower)]
    internal static char Lower(char self) => char.ToLower(self);

    [SpellkitMethod(BuiltinMethodNames.Upper)]
    internal static char Upper(char self) => char.ToUpper(self);

    [SpellkitMethod(BuiltinMethodNames.Order)]
    internal static int Order(char self) => self;

    [SpellkitStaticMethod(BuiltinMethodNames.Char)]
    internal static SpellkitObject CreateChar(SpellkitObject value)
    {
        if (value.TypeId is SpellkitTypeCodes.Char)
        {
            return value;
        }

        if (value is SpellkitString str)
        {
            return str.Value.Length > 0 ? new(str.Value[0]) : SpellkitChar.Empty;
        }

        if (value is SpellkitInteger i)
        {
            return new SpellkitChar((char)i.Value);
        }

        if (value is SpellkitFloat f)
        {
            return new SpellkitChar((char)f.Value);
        }

        throw new SpellkitCodeException(SpellkitError.InvalidCast, value.TypeName, nameof(SpellkitTypeCodes.Char));
    }

    [SpellkitStaticProperty(BuiltinMethodNames.Max)]
    internal static SpellkitChar Max() => SpellkitChar.Max;

    [SpellkitStaticProperty(BuiltinMethodNames.Min)]
    internal static SpellkitChar Min() => SpellkitChar.Min;

    [SpellkitStaticProperty(BuiltinMethodNames.Default)]
    internal static SpellkitChar Default() => SpellkitChar.Empty;
}
