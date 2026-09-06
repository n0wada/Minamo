using Minamo.Codegen;
using System.Globalization;

namespace Minamo.Runtime.Types;

public sealed class MinamoInteger : MinamoObject
{
    public static readonly MinamoInteger Zero = new(0L);
    public static readonly MinamoInteger MinusOne = new(-1L);
    public static readonly MinamoInteger One = new(1L);
    public static readonly MinamoInteger Two = new(2L);
    public static readonly MinamoInteger Three = new(3L);
    public static readonly MinamoInteger Max = new(long.MaxValue);
    public static readonly MinamoInteger Min = new(long.MinValue);

    public readonly long Value;

    public override string TypeName => nameof(MinamoTypeCodes.Integer);

    public static MinamoInteger Get(long i) =>
        i switch
        {
            -1 => MinusOne,
            0 => Zero,
            1 => One,
            2 => Two,
            3 => Three,
            _ => new MinamoInteger(i)
        };

    public MinamoInteger(long value) : base(MinamoTypeCodes.Integer) => this.Value = value;

    internal bool TryGetInt32(out int value)
    {
        if (Value < int.MinValue || Value > int.MaxValue)
        {
            value = default;
            return false;
        }

        value = (int)Value;
        return true;
    }

    internal static bool TryFromDouble(double value, out MinamoInteger result)
    {
        if (!double.IsFinite(value)
            || value < long.MinValue
            || value >= 9_223_372_036_854_775_808d)
        {
            result = Zero;
            return false;
        }

        result = Get((long)value);
        return true;
    }

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(InvariantCulture);

    public override bool Equals(MinamoObject? obj) => obj is MinamoInteger i && Value == i.Value;

    public override object ToObject() => Value == (int)Value ? Convert.ChangeType(Value, BCL.Int32) : Value;

    public override MinamoObject Clone() => this;
}

[MinamoType]
internal sealed partial class MinamoIntegerTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Integer);

    public override int ReflectedTypeId => MinamoTypeCodes.Integer;

    public MinamoIntegerTypeInfo()
    {
        AddMixins(MinamoTypeCodes.Number, MinamoTypeCodes.Order, MinamoTypeCodes.Equatable);
    }

    #region Operations
    protected override MinamoObject AddOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i8)
        {
            return new MinamoInteger(((MinamoInteger)left).Value + i8.Value);
        }

        if (right is MinamoFloat r8)
        {
            return new MinamoFloat(((MinamoInteger)left).Value + r8.Value);
        }

        return base.AddOp(ctx, left, right);
    }

    protected override MinamoObject SubOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i8)
        {
            return new MinamoInteger(((MinamoInteger)left).Value - i8.Value);
        }

        if (right is MinamoFloat r8)
        {
            return new MinamoFloat(((MinamoInteger)left).Value - r8.Value);
        }

        return base.SubOp(ctx, left, right);
    }

    protected override MinamoObject MulOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i8)
        {
            return new MinamoInteger(((MinamoInteger)left).Value * i8.Value);
        }

        if (right is MinamoFloat r8)
        {
            return new MinamoFloat(((MinamoInteger)left).Value * r8.Value);
        }

        return base.MulOp(ctx, left, right);
    }

    protected override MinamoObject DivOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i8)
        {
            if (i8.Value == 0)
            {
                return ctx.DivideByZero();
            }

            if (((MinamoInteger)left).Value == long.MinValue && i8.Value == -1)
            {
                return ctx.Overflow();
            }

            return new MinamoInteger(((MinamoInteger)left).Value / i8.Value);
        }

        if (right is MinamoFloat r8)
        {
            return new MinamoFloat(((MinamoInteger)left).Value / r8.Value);
        }

        return base.DivOp(ctx, left, right);
    }

    protected override MinamoObject RemOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i8)
        {
            if (i8.Value == 0)
            {
                return ctx.DivideByZero();
            }

            if (((MinamoInteger)left).Value == long.MinValue && i8.Value == -1)
            {
                return ctx.Overflow();
            }

            return new MinamoInteger(((MinamoInteger)left).Value % i8.Value);
        }

        if (right is MinamoFloat r8)
        {
            return new MinamoFloat(((MinamoInteger)left).Value % r8.Value);
        }

        return base.RemOp(ctx, left, right);
    }

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i8)
        {
            return ((MinamoInteger)left).Value == i8.Value ? True : False;
        }

        if (right is MinamoFloat r8)
        {
            return ((MinamoInteger)left).Value == r8.Value ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override MinamoObject NeqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i8)
        {
            return ((MinamoInteger)left).Value != i8.Value ? True : False;
        }

        if (right is MinamoFloat r8)
        {
            return ((MinamoInteger)left).Value != r8.Value ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override MinamoObject GtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i8)
        {
            return ((MinamoInteger)left).Value > i8.Value ? True : False;
        }

        if (right is MinamoFloat r8)
        {
            return ((MinamoInteger)left).Value > r8.Value ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override MinamoObject LtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i8)
        {
            return ((MinamoInteger)left).Value < i8.Value ? True : False;
        }

        if (right is MinamoFloat r8)
        {
            return ((MinamoInteger)left).Value < r8.Value ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override MinamoObject GteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i8)
        {
            return ((MinamoInteger)left).Value >= i8.Value ? True : False;
        }

        if (right is MinamoFloat r8)
        {
            return ((MinamoInteger)left).Value >= r8.Value ? True : False;
        }

        return base.GteOp(ctx, left, right);
    }

    protected override MinamoObject LteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoInteger i8)
        {
            return ((MinamoInteger)left).Value <= i8.Value ? True : False;
        }

        if (right is MinamoFloat r8)
        {
            return ((MinamoInteger)left).Value <= r8.Value ? True : False;
        }

        return base.LteOp(ctx, left, right);
    }

    protected override MinamoObject NegOp(ExecutionContext ctx, MinamoObject arg) => new MinamoInteger(-((MinamoInteger)arg).Value);

    protected override MinamoObject PlusOp(ExecutionContext ctx, MinamoObject arg) => arg;

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject self, MinamoObject format)
    {
        if (format.TypeId is not MinamoTypeCodes.String and not MinamoTypeCodes.Char and not MinamoTypeCodes.Nil)
        {
            return ctx.InvalidType(format);
        }

        try
        {
            var value = ((MinamoInteger)self).Value;
            return new MinamoString(format.TypeId is MinamoTypeCodes.Nil
                ? value.ToString(SystemCulture.NumberFormat)
                : value.ToString(format.ToString(), SystemCulture.NumberFormat));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            MinamoTypeCodes.Float => new MinamoFloat(((MinamoInteger)self).Value),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [MinamoMethod(BuiltinMethodNames.IsMultipleOf)]
    internal static bool IsMultipleOf(long self, long value) => (self % value) == 0;

    [MinamoStaticMethod(BuiltinMethodNames.Parse)]
    internal static long? Parse(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, InvariantCulture.NumberFormat, out var i))
        {
            return i;
        }

        return default;
    }

    [MinamoStaticMethod(BuiltinMethodNames.Integer)]
    internal static MinamoObject CreateNew(ExecutionContext ctx, MinamoObject value)
    {
        if (value is MinamoInteger i8)
        {
            return i8;
        }

        if (value is MinamoFloat r8)
        {
            return MinamoInteger.TryFromDouble(r8.Value, out var converted)
                ? converted
                : ctx.Overflow();
        }

        if (value.TypeId is MinamoTypeCodes.Char or MinamoTypeCodes.String)
        {
            var parsed = Parse(value.ToString());
            return parsed is null ? Nil : MinamoInteger.Get(parsed.Value);
        }

        throw new MinamoCodeException(MinamoError.InvalidType, value);
    }

    [MinamoStaticProperty(BuiltinMethodNames.Max)]
    internal static MinamoObject Max() => MinamoInteger.Max;

    [MinamoStaticProperty(BuiltinMethodNames.Min)]
    internal static MinamoObject Min() => MinamoInteger.Min;

    [MinamoStaticProperty(BuiltinMethodNames.Default)]
    internal static MinamoObject Default() => MinamoInteger.Zero;
}
