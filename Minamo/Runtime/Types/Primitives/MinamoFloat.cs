using Minamo.Codegen;
using System.Globalization;

namespace Minamo.Runtime.Types;

public sealed class MinamoFloat : MinamoObject
{
    public static readonly MinamoFloat Zero = new(0D);
    public static readonly MinamoFloat One = new(1D);
    public static readonly MinamoFloat NaN = new(double.NaN);
    public static readonly MinamoFloat PositiveInfinity = new(double.PositiveInfinity);
    public static readonly MinamoFloat NegativeInfinity = new(double.NegativeInfinity);
    public static readonly MinamoFloat Epsilon = new(double.Epsilon);
    public static readonly MinamoFloat Min = new(double.MinValue);
    public static readonly MinamoFloat Max = new(double.MaxValue);

    public readonly double Value;

    public override string TypeName => nameof(MinamoTypeCodes.Float);

    public MinamoFloat(double value) : base(MinamoTypeCodes.Float) => Value = value;

    public override int GetHashCode() => Value.GetHashCode();

    public override bool Equals(MinamoObject? obj) => obj is MinamoFloat f && Value == f.Value;

    public override string ToString() => Value.ToString(InvariantCulture.NumberFormat);

    public override object ToObject() => Value;

    public override MinamoObject Clone() => this;
}

[MinamoType]
internal sealed partial class MinamoFloatTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Float);

    public override int ReflectedTypeId => MinamoTypeCodes.Float;

    public MinamoFloatTypeInfo() => AddMixins(MinamoTypeCodes.Number, MinamoTypeCodes.Order, MinamoTypeCodes.Equatable);

    #region Operations
    protected override MinamoObject AddOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.Float)
        {
            return new MinamoFloat(((MinamoFloat)left).Value + ((MinamoFloat)right).Value);
        }

        if (right.TypeId is MinamoTypeCodes.Integer)
        {
            return new MinamoFloat(((MinamoFloat)left).Value + ((MinamoInteger)right).Value);
        }

        return base.AddOp(ctx, left, right);
    }

    protected override MinamoObject SubOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.Float)
        {
            return new MinamoFloat(((MinamoFloat)left).Value - ((MinamoFloat)right).Value);
        }

        if (right.TypeId is MinamoTypeCodes.Integer)
        {
            return new MinamoFloat(((MinamoFloat)left).Value - ((MinamoInteger)right).Value);
        }

        return base.SubOp(ctx, left, right);
    }

    protected override MinamoObject MulOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.Float)
        {
            return new MinamoFloat(((MinamoFloat)left).Value * ((MinamoFloat)right).Value);
        }

        if (right.TypeId is MinamoTypeCodes.Integer)
        {
            return new MinamoFloat(((MinamoFloat)left).Value * ((MinamoInteger)right).Value);
        }

        return base.MulOp(ctx, left, right);
    }

    protected override MinamoObject DivOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.Float)
        {
            return new MinamoFloat(((MinamoFloat)left).Value / ((MinamoFloat)right).Value);
        }

        if (right.TypeId is MinamoTypeCodes.Integer)
        {
            return new MinamoFloat(((MinamoFloat)left).Value / ((MinamoInteger)right).Value);
        }

        return base.DivOp(ctx, left, right);
    }

    protected override MinamoObject RemOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.Float)
        {
            return new MinamoFloat(((MinamoFloat)left).Value % ((MinamoFloat)right).Value);
        }

        if (right.TypeId is MinamoTypeCodes.Integer)
        {
            return new MinamoFloat(((MinamoFloat)left).Value % ((MinamoInteger)right).Value);
        }

        return base.RemOp(ctx, left, right);
    }

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.Float)
        {
            return ((MinamoFloat)left).Value == ((MinamoFloat)right).Value ? True : False;
        }

        if (right.TypeId is MinamoTypeCodes.Integer)
        {
            return ((MinamoFloat)left).Value == ((MinamoInteger)right).Value ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override MinamoObject NeqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.Float)
        {
            return ((MinamoFloat)left).Value != ((MinamoFloat)right).Value ? True : False;
        }

        if (right.TypeId is MinamoTypeCodes.Integer)
        {
            return ((MinamoFloat)left).Value != ((MinamoInteger)right).Value ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override MinamoObject GtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.Float)
        {
            return ((MinamoFloat)left).Value > ((MinamoFloat)right).Value ? True : False;
        }

        if (right.TypeId is MinamoTypeCodes.Integer)
        {
            return ((MinamoFloat)left).Value > ((MinamoInteger)right).Value ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override MinamoObject LtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.Float)
        {
            return ((MinamoFloat)left).Value < ((MinamoFloat)right).Value ? True : False;
        }

        if (right.TypeId is MinamoTypeCodes.Integer)
        {
            return ((MinamoFloat)left).Value < ((MinamoInteger)right).Value ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override MinamoObject GteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.Float)
        {
            return ((MinamoFloat)left).Value >= ((MinamoFloat)right).Value ? True : False;
        }

        if (right.TypeId is MinamoTypeCodes.Integer)
        {
            return ((MinamoFloat)left).Value >= ((MinamoInteger)right).Value ? True : False;
        }

        return base.GteOp(ctx, left, right);
    }

    protected override MinamoObject LteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId is MinamoTypeCodes.Float)
        {
            return ((MinamoFloat)left).Value <= ((MinamoFloat)right).Value ? True : False;
        }

        if (right.TypeId is MinamoTypeCodes.Integer)
        {
            return ((MinamoFloat)left).Value <= ((MinamoInteger)right).Value ? True : False;
        }

        return base.LteOp(ctx, left, right);
    }

    protected override MinamoObject NegOp(ExecutionContext ctx, MinamoObject arg) => new MinamoFloat(-((MinamoFloat)arg).Value);

    protected override MinamoObject PlusOp(ExecutionContext ctx, MinamoObject arg) => arg;

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject self, MinamoObject format)
    {
        if (format.TypeId is not MinamoTypeCodes.String and not MinamoTypeCodes.Char and not MinamoTypeCodes.Nil)
        {
            return ctx.InvalidType(format);
        }

        try
        {
            var value = ((MinamoFloat)self).Value;
            return new MinamoString(format.TypeId is MinamoTypeCodes.Nil
                ? value.ToString(SystemCulture.NumberFormat)
                : value.ToString(format.ToString(), SystemCulture.NumberFormat));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId != MinamoTypeCodes.Integer)
        {
            return base.CastOp(ctx, self, targetType);
        }

        return MinamoInteger.TryFromDouble(((MinamoFloat)self).Value, out var converted)
            ? converted
            : ctx.Overflow();
    }
    #endregion

    [MinamoMethod(BuiltinMethodNames.IsNaN)]
    internal static bool IsNaN(double self) => double.IsNaN(self);

    [MinamoStaticProperty(BuiltinMethodNames.Max)]
    internal static MinamoObject Max() => MinamoFloat.Max;

    [MinamoStaticProperty(BuiltinMethodNames.Min)]
    internal static MinamoObject Min() => MinamoFloat.Min;

    [MinamoStaticProperty]
    internal static MinamoObject Infinity() => MinamoFloat.PositiveInfinity;

    [MinamoStaticProperty(BuiltinMethodNames.Default)]
    internal static MinamoObject Default() => MinamoFloat.Zero;

    [MinamoStaticMethod(BuiltinMethodNames.Parse)]
    internal static double? Parse(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, InvariantCulture.NumberFormat, out var i))
        {
            return i;
        }

        return default;
    }

    [MinamoStaticMethod(BuiltinMethodNames.Float)]
    internal static double? Convert(MinamoObject value)
    {
        if (value is MinamoFloat f)
        {
            return f.Value;
        }

        if (value is MinamoInteger i)
        {
            return i.Value;
        }

        if (value.TypeId is MinamoTypeCodes.Char or MinamoTypeCodes.String)
        {
            return Parse(value.ToString());
        }

        throw new MinamoCodeException(MinamoError.InvalidType, value);
    }
}
