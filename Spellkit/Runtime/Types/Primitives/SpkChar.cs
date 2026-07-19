using Spellkit.Codegen;

namespace Spellkit.Runtime.Types;

public sealed class SpkChar : SpkObject
{
    public static readonly SpkChar WhiteSpace = new(' ');
    public static readonly SpkChar Empty = new('\0');
    public static readonly SpkChar Max = new(char.MaxValue);
    public static readonly SpkChar Min = new(char.MinValue);

    internal readonly char Value;

    public override string TypeName => nameof(Spk.Char);

    public SpkChar(char value) : base(Spk.Char) => this.Value = value;

    public override object ToObject() => Value;

    public override string ToString() => Value.ToString();

    public override SpkObject Clone() => this;

    public override bool Equals(SpkObject? other) => other is SpkChar c && c.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();
}

[SpkType]
internal sealed partial class SpkCharTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Char);

    public override int ReflectedTypeId => Spk.Char;

    public SpkCharTypeInfo() => AddMixins(Spk.Order, Spk.Equatable);

    #region Operations
    protected override SpkObject AddOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i)
        {
            return new SpkChar((char)(((SpkChar)left).Value + i.Value));
        }

        if (right.TypeId is Spk.Char)
        {
            return new SpkString(left.ToString() + right.ToString());
        }

        return base.AddOp(ctx, left, right);
    }

    protected override SpkObject SubOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i)
        {
            return new SpkChar((char)(((SpkChar)left).Value - i.Value));
        }

        if (right is SpkChar c)
        {
            return new SpkChar((char)(((SpkChar)left).Value - c.Value));
        }

        return base.SubOp(ctx, left, right);
    }

    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((SpkChar)left).Value == ((SpkChar)right).Value ? True : False;
        }

        if (right is SpkString str)
        {
            return str.Value.Length == 1 && ((SpkChar)left).Value == str.Value[0] ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override SpkObject NeqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((SpkChar)left).Value != ((SpkChar)right).Value ? True : False;
        }

        if (right is SpkString str)
        {
            return str.Value.Length != 1 || ((SpkChar)left).Value != str.Value[0] ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override SpkObject GtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((SpkChar)left).Value.CompareTo(((SpkChar)right).Value) > 0 ? True : False;
        }

        if (right is SpkString str)
        {
            return ((SpkChar)left).Value.ToString().CompareTo(str.Value) > 0 ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override SpkObject LtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((SpkChar)left).Value.CompareTo(((SpkChar)right).Value) < 0 ? True : False;
        }

        if (right is SpkString str)
        {
            return ((SpkChar)left).Value.ToString().CompareTo(str.Value) < 0 ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            Spk.Integer => SpkInteger.Get(((SpkChar)self).Value),
            Spk.Float => new SpkFloat(((SpkChar)self).Value),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [SpkMethod(BuiltinMethodNames.IsLower)]
    internal static bool IsLower(char self) => char.IsLower(self);

    [SpkMethod(BuiltinMethodNames.IsUpper)]
    internal static bool IsUpper(char self) => char.IsUpper(self);

    [SpkMethod(BuiltinMethodNames.IsControl)]
    internal static bool IsControl(char self) => char.IsControl(self);

    [SpkMethod(BuiltinMethodNames.IsDigit)]
    internal static bool IsDigit(char self) => char.IsDigit(self);

    [SpkMethod(BuiltinMethodNames.IsLetter)]
    internal static bool IsLetter(char self) => char.IsLetter(self);

    [SpkMethod(BuiltinMethodNames.IsLetterOrDigit)]
    internal static bool IsLetterOrDigit(char self) => char.IsLetterOrDigit(self);

    [SpkMethod(BuiltinMethodNames.IsWhiteSpace)]
    internal static bool IsWhiteSpace(char self) => char.IsWhiteSpace(self);

    [SpkMethod(BuiltinMethodNames.Lower)]
    internal static char Lower(char self) => char.ToLower(self);

    [SpkMethod(BuiltinMethodNames.Upper)]
    internal static char Upper(char self) => char.ToUpper(self);

    [SpkMethod(BuiltinMethodNames.Order)]
    internal static int Order(char self) => self;

    [SpkStaticMethod(BuiltinMethodNames.Char)]
    internal static SpkObject CreateChar(SpkObject value)
    {
        if (value.TypeId is Spk.Char)
        {
            return value;
        }

        if (value is SpkString str)
        {
            return str.Value.Length > 0 ? new(str.Value[0]) : SpkChar.Empty;
        }

        if (value is SpkInteger i)
        {
            return new SpkChar((char)i.Value);
        }

        if (value is SpkFloat f)
        {
            return new SpkChar((char)f.Value);
        }

        throw new SpkCodeException(SpkError.InvalidCast, value.TypeName, nameof(Spk.Char));
    }

    [SpkStaticProperty(BuiltinMethodNames.Max)]
    internal static SpkChar Max() => SpkChar.Max;

    [SpkStaticProperty(BuiltinMethodNames.Min)]
    internal static SpkChar Min() => SpkChar.Min;

    [SpkStaticProperty(BuiltinMethodNames.Default)]
    internal static SpkChar Default() => SpkChar.Empty;
}
