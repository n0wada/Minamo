using Minamo.Codegen;

namespace Minamo.Runtime.Types;

public sealed class MinamoChar : MinamoObject
{
    public static readonly MinamoChar WhiteSpace = new(' ');
    public static readonly MinamoChar Empty = new('\0');
    public static readonly MinamoChar Max = new(char.MaxValue);
    public static readonly MinamoChar Min = new(char.MinValue);

    internal readonly char Value;

    public override string TypeName => nameof(MinamoTypeCodes.Char);

    public MinamoChar(char value) : base(MinamoTypeCodes.Char) => this.Value = value;

    public override object ToObject() => Value;

    public override string ToString() => Value.ToString();

    public override MinamoObject Clone() => this;

    public override bool Equals(MinamoObject? other) => other is MinamoChar c && c.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();
}

[MinamoType]
internal sealed partial class MinamoCharTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Char);

    public override int ReflectedTypeId => MinamoTypeCodes.Char;

    public MinamoCharTypeInfo() => AddMixins(MinamoTypeCodes.Order, MinamoTypeCodes.Equatable);

    #region Operations
    protected override MinamoObject AddOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i)
        {
            return new MinamoChar((char)(((MinamoChar)left).Value + i.Value));
        }

        if (right.TypeId is MinamoTypeCodes.Char)
        {
            return new MinamoString(left.ToString() + right.ToString());
        }

        return base.AddOp(ctx, left, right);
    }

    protected override MinamoObject SubOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i)
        {
            return new MinamoChar((char)(((MinamoChar)left).Value - i.Value));
        }

        if (right is MinamoChar c)
        {
            return new MinamoChar((char)(((MinamoChar)left).Value - c.Value));
        }

        return base.SubOp(ctx, left, right);
    }

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((MinamoChar)left).Value == ((MinamoChar)right).Value ? True : False;
        }

        if (right is MinamoString str)
        {
            return str.Value.Length == 1 && ((MinamoChar)left).Value == str.Value[0] ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override MinamoObject NeqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((MinamoChar)left).Value != ((MinamoChar)right).Value ? True : False;
        }

        if (right is MinamoString str)
        {
            return str.Value.Length != 1 || ((MinamoChar)left).Value != str.Value[0] ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override MinamoObject GtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((MinamoChar)left).Value.CompareTo(((MinamoChar)right).Value) > 0 ? True : False;
        }

        if (right is MinamoString str)
        {
            return ((MinamoChar)left).Value.ToString().CompareTo(str.Value) > 0 ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override MinamoObject LtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId == right.TypeId)
        {
            return ((MinamoChar)left).Value.CompareTo(((MinamoChar)right).Value) < 0 ? True : False;
        }

        if (right is MinamoString str)
        {
            return ((MinamoChar)left).Value.ToString().CompareTo(str.Value) < 0 ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            MinamoTypeCodes.Integer => MinamoInteger.Get(((MinamoChar)self).Value),
            MinamoTypeCodes.Float => new MinamoFloat(((MinamoChar)self).Value),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [MinamoMethod(BuiltinMethodNames.IsLower)]
    internal static bool IsLower(char self) => char.IsLower(self);

    [MinamoMethod(BuiltinMethodNames.IsUpper)]
    internal static bool IsUpper(char self) => char.IsUpper(self);

    [MinamoMethod(BuiltinMethodNames.IsControl)]
    internal static bool IsControl(char self) => char.IsControl(self);

    [MinamoMethod(BuiltinMethodNames.IsDigit)]
    internal static bool IsDigit(char self) => char.IsDigit(self);

    [MinamoMethod(BuiltinMethodNames.IsLetter)]
    internal static bool IsLetter(char self) => char.IsLetter(self);

    [MinamoMethod(BuiltinMethodNames.IsLetterOrDigit)]
    internal static bool IsLetterOrDigit(char self) => char.IsLetterOrDigit(self);

    [MinamoMethod(BuiltinMethodNames.IsWhiteSpace)]
    internal static bool IsWhiteSpace(char self) => char.IsWhiteSpace(self);

    [MinamoMethod(BuiltinMethodNames.Lower)]
    internal static char Lower(char self) => char.ToLower(self);

    [MinamoMethod(BuiltinMethodNames.Upper)]
    internal static char Upper(char self) => char.ToUpper(self);

    [MinamoMethod(BuiltinMethodNames.Order)]
    internal static int Order(char self) => self;

    [MinamoStaticMethod(BuiltinMethodNames.Char)]
    internal static MinamoObject CreateChar(MinamoObject value)
    {
        if (value.TypeId is MinamoTypeCodes.Char)
        {
            return value;
        }

        if (value is MinamoString str)
        {
            return str.Value.Length > 0 ? new(str.Value[0]) : MinamoChar.Empty;
        }

        if (value is MinamoInteger i)
        {
            return new MinamoChar((char)i.Value);
        }

        if (value is MinamoFloat f)
        {
            return new MinamoChar((char)f.Value);
        }

        throw new MinamoCodeException(MinamoError.InvalidCast, value.TypeName, nameof(MinamoTypeCodes.Char));
    }

    [MinamoStaticProperty(BuiltinMethodNames.Max)]
    internal static MinamoChar Max() => MinamoChar.Max;

    [MinamoStaticProperty(BuiltinMethodNames.Min)]
    internal static MinamoChar Min() => MinamoChar.Min;

    [MinamoStaticProperty(BuiltinMethodNames.Default)]
    internal static MinamoChar Default() => MinamoChar.Empty;
}
