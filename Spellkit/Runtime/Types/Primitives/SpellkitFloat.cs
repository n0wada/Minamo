using Spellkit.Codegen;
using System.Globalization;

namespace Spellkit.Runtime.Types;

public sealed class SpellkitFloat : SpellkitObject
{
    public static readonly SpellkitFloat Zero = new(0D);
    public static readonly SpellkitFloat One = new(1D);
    public static readonly SpellkitFloat NaN = new(double.NaN);
    public static readonly SpellkitFloat PositiveInfinity = new(double.PositiveInfinity);
    public static readonly SpellkitFloat NegativeInfinity = new(double.NegativeInfinity);
    public static readonly SpellkitFloat Epsilon = new(double.Epsilon);
    public static readonly SpellkitFloat Min = new(double.MinValue);
    public static readonly SpellkitFloat Max = new(double.MaxValue);

    public readonly double Value;

    public override string TypeName => nameof(SpellkitTypeCodes.Float);

    public SpellkitFloat(double value) : base(SpellkitTypeCodes.Float) => Value = value;

    public override int GetHashCode() => Value.GetHashCode();

    public override bool Equals(SpellkitObject? obj) => obj is SpellkitFloat f && Value == f.Value;

    public override string ToString() => Value.ToString(InvariantCulture.NumberFormat);

    public override object ToObject() => Value;

    public override SpellkitObject Clone() => this;
}

[SpellkitType]
internal sealed partial class SpellkitFloatTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Float);

    public override int ReflectedTypeId => SpellkitTypeCodes.Float;

    public SpellkitFloatTypeInfo() => AddMixins(SpellkitTypeCodes.Number, SpellkitTypeCodes.Order, SpellkitTypeCodes.Equatable);

    #region Operations
    protected override SpellkitObject AddOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.Float)
        {
            return new SpellkitFloat(((SpellkitFloat)left).Value + ((SpellkitFloat)right).Value);
        }

        if (right.TypeId is SpellkitTypeCodes.Integer)
        {
            return new SpellkitFloat(((SpellkitFloat)left).Value + ((SpellkitInteger)right).Value);
        }

        return base.AddOp(ctx, left, right);
    }

    protected override SpellkitObject SubOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.Float)
        {
            return new SpellkitFloat(((SpellkitFloat)left).Value - ((SpellkitFloat)right).Value);
        }

        if (right.TypeId is SpellkitTypeCodes.Integer)
        {
            return new SpellkitFloat(((SpellkitFloat)left).Value - ((SpellkitInteger)right).Value);
        }

        return base.SubOp(ctx, left, right);
    }

    protected override SpellkitObject MulOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.Float)
        {
            return new SpellkitFloat(((SpellkitFloat)left).Value * ((SpellkitFloat)right).Value);
        }

        if (right.TypeId is SpellkitTypeCodes.Integer)
        {
            return new SpellkitFloat(((SpellkitFloat)left).Value * ((SpellkitInteger)right).Value);
        }

        return base.MulOp(ctx, left, right);
    }

    protected override SpellkitObject DivOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.Float)
        {
            return new SpellkitFloat(((SpellkitFloat)left).Value / ((SpellkitFloat)right).Value);
        }

        if (right.TypeId is SpellkitTypeCodes.Integer)
        {
            return new SpellkitFloat(((SpellkitFloat)left).Value / ((SpellkitInteger)right).Value);
        }

        return base.DivOp(ctx, left, right);
    }

    protected override SpellkitObject RemOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.Float)
        {
            return new SpellkitFloat(((SpellkitFloat)left).Value % ((SpellkitFloat)right).Value);
        }

        if (right.TypeId is SpellkitTypeCodes.Integer)
        {
            return new SpellkitFloat(((SpellkitFloat)left).Value % ((SpellkitInteger)right).Value);
        }

        return base.RemOp(ctx, left, right);
    }

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.Float)
        {
            return ((SpellkitFloat)left).Value == ((SpellkitFloat)right).Value ? True : False;
        }

        if (right.TypeId is SpellkitTypeCodes.Integer)
        {
            return ((SpellkitFloat)left).Value == ((SpellkitInteger)right).Value ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override SpellkitObject NeqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.Float)
        {
            return ((SpellkitFloat)left).Value != ((SpellkitFloat)right).Value ? True : False;
        }

        if (right.TypeId is SpellkitTypeCodes.Integer)
        {
            return ((SpellkitFloat)left).Value != ((SpellkitInteger)right).Value ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override SpellkitObject GtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.Float)
        {
            return ((SpellkitFloat)left).Value > ((SpellkitFloat)right).Value ? True : False;
        }

        if (right.TypeId is SpellkitTypeCodes.Integer)
        {
            return ((SpellkitFloat)left).Value > ((SpellkitInteger)right).Value ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override SpellkitObject LtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.Float)
        {
            return ((SpellkitFloat)left).Value < ((SpellkitFloat)right).Value ? True : False;
        }

        if (right.TypeId is SpellkitTypeCodes.Integer)
        {
            return ((SpellkitFloat)left).Value < ((SpellkitInteger)right).Value ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override SpellkitObject GteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.Float)
        {
            return ((SpellkitFloat)left).Value >= ((SpellkitFloat)right).Value ? True : False;
        }

        if (right.TypeId is SpellkitTypeCodes.Integer)
        {
            return ((SpellkitFloat)left).Value >= ((SpellkitInteger)right).Value ? True : False;
        }

        return base.GteOp(ctx, left, right);
    }

    protected override SpellkitObject LteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId is SpellkitTypeCodes.Float)
        {
            return ((SpellkitFloat)left).Value <= ((SpellkitFloat)right).Value ? True : False;
        }

        if (right.TypeId is SpellkitTypeCodes.Integer)
        {
            return ((SpellkitFloat)left).Value <= ((SpellkitInteger)right).Value ? True : False;
        }

        return base.LteOp(ctx, left, right);
    }

    protected override SpellkitObject NegOp(ExecutionContext ctx, SpellkitObject arg) => new SpellkitFloat(-((SpellkitFloat)arg).Value);

    protected override SpellkitObject PlusOp(ExecutionContext ctx, SpellkitObject arg) => arg;

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject format)
    {
        if (format.TypeId is not SpellkitTypeCodes.String and not SpellkitTypeCodes.Char and not SpellkitTypeCodes.Nil)
        {
            return ctx.InvalidType(format);
        }

        try
        {
            var value = ((SpellkitFloat)self).Value;
            return new SpellkitString(format.TypeId is SpellkitTypeCodes.Nil
                ? value.ToString(SystemCulture.NumberFormat)
                : value.ToString(format.ToString(), SystemCulture.NumberFormat));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId != SpellkitTypeCodes.Integer)
        {
            return base.CastOp(ctx, self, targetType);
        }

        return SpellkitInteger.TryFromDouble(((SpellkitFloat)self).Value, out var converted)
            ? converted
            : ctx.Overflow();
    }
    #endregion

    [SpellkitMethod(BuiltinMethodNames.IsNaN)]
    internal static bool IsNaN(double self) => double.IsNaN(self);

    [SpellkitStaticProperty(BuiltinMethodNames.Max)]
    internal static SpellkitObject Max() => SpellkitFloat.Max;

    [SpellkitStaticProperty(BuiltinMethodNames.Min)]
    internal static SpellkitObject Min() => SpellkitFloat.Min;

    [SpellkitStaticProperty]
    internal static SpellkitObject Infinity() => SpellkitFloat.PositiveInfinity;

    [SpellkitStaticProperty(BuiltinMethodNames.Default)]
    internal static SpellkitObject Default() => SpellkitFloat.Zero;

    [SpellkitStaticMethod(BuiltinMethodNames.Parse)]
    internal static double? Parse(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, InvariantCulture.NumberFormat, out var i))
        {
            return i;
        }

        return default;
    }

    [SpellkitStaticMethod(BuiltinMethodNames.Float)]
    internal static double? Convert(SpellkitObject value)
    {
        if (value is SpellkitFloat f)
        {
            return f.Value;
        }

        if (value is SpellkitInteger i)
        {
            return i.Value;
        }

        if (value.TypeId is SpellkitTypeCodes.Char or SpellkitTypeCodes.String)
        {
            return Parse(value.ToString());
        }

        throw new SpellkitCodeException(SpellkitError.InvalidType, value);
    }
}
