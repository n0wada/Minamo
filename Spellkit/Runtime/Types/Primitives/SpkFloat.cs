using Spellkit.Codegen;
using System.Globalization;

namespace Spellkit.Runtime.Types;

public sealed class SpkFloat : SpkObject
{
    public static readonly SpkFloat Zero = new(0D);
    public static readonly SpkFloat One = new(1D);
    public static readonly SpkFloat NaN = new(double.NaN);
    public static readonly SpkFloat PositiveInfinity = new(double.PositiveInfinity);
    public static readonly SpkFloat NegativeInfinity = new(double.NegativeInfinity);
    public static readonly SpkFloat Epsilon = new(double.Epsilon);
    public static readonly SpkFloat Min = new(double.MinValue);
    public static readonly SpkFloat Max = new(double.MaxValue);

    public readonly double Value;

    public override string TypeName => nameof(Spk.Float);

    public SpkFloat(double value) : base(Spk.Float) => Value = value;

    public override int GetHashCode() => Value.GetHashCode();

    public override bool Equals(SpkObject? obj) => obj is SpkFloat f && Value == f.Value;

    public override string ToString() => Value.ToString(InvariantCulture.NumberFormat);

    public override object ToObject() => Value;

    public override SpkObject Clone() => this;
}

[SpkType]
internal sealed partial class SpkFloatTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Float);

    public override int ReflectedTypeId => Spk.Float;

    public SpkFloatTypeInfo() => AddMixins(Spk.Number, Spk.Order, Spk.Equatable);

    #region Operations
    protected override SpkObject AddOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.Float)
        {
            return new SpkFloat(((SpkFloat)left).Value + ((SpkFloat)right).Value);
        }

        if (right.TypeId is Spk.Integer)
        {
            return new SpkFloat(((SpkFloat)left).Value + ((SpkInteger)right).Value);
        }

        return base.AddOp(ctx, left, right);
    }

    protected override SpkObject SubOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.Float)
        {
            return new SpkFloat(((SpkFloat)left).Value - ((SpkFloat)right).Value);
        }

        if (right.TypeId is Spk.Integer)
        {
            return new SpkFloat(((SpkFloat)left).Value - ((SpkInteger)right).Value);
        }

        return base.SubOp(ctx, left, right);
    }

    protected override SpkObject MulOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.Float)
        {
            return new SpkFloat(((SpkFloat)left).Value * ((SpkFloat)right).Value);
        }

        if (right.TypeId is Spk.Integer)
        {
            return new SpkFloat(((SpkFloat)left).Value * ((SpkInteger)right).Value);
        }

        return base.MulOp(ctx, left, right);
    }

    protected override SpkObject DivOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.Float)
        {
            return new SpkFloat(((SpkFloat)left).Value / ((SpkFloat)right).Value);
        }

        if (right.TypeId is Spk.Integer)
        {
            return new SpkFloat(((SpkFloat)left).Value / ((SpkInteger)right).Value);
        }

        return base.DivOp(ctx, left, right);
    }

    protected override SpkObject RemOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.Float)
        {
            return new SpkFloat(((SpkFloat)left).Value % ((SpkFloat)right).Value);
        }

        if (right.TypeId is Spk.Integer)
        {
            return new SpkFloat(((SpkFloat)left).Value % ((SpkInteger)right).Value);
        }

        return base.RemOp(ctx, left, right);
    }

    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.Float)
        {
            return ((SpkFloat)left).Value == ((SpkFloat)right).Value ? True : False;
        }

        if (right.TypeId is Spk.Integer)
        {
            return ((SpkFloat)left).Value == ((SpkInteger)right).Value ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override SpkObject NeqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.Float)
        {
            return ((SpkFloat)left).Value != ((SpkFloat)right).Value ? True : False;
        }

        if (right.TypeId is Spk.Integer)
        {
            return ((SpkFloat)left).Value != ((SpkInteger)right).Value ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override SpkObject GtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.Float)
        {
            return ((SpkFloat)left).Value > ((SpkFloat)right).Value ? True : False;
        }

        if (right.TypeId is Spk.Integer)
        {
            return ((SpkFloat)left).Value > ((SpkInteger)right).Value ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override SpkObject LtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.Float)
        {
            return ((SpkFloat)left).Value < ((SpkFloat)right).Value ? True : False;
        }

        if (right.TypeId is Spk.Integer)
        {
            return ((SpkFloat)left).Value < ((SpkInteger)right).Value ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override SpkObject GteOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.Float)
        {
            return ((SpkFloat)left).Value >= ((SpkFloat)right).Value ? True : False;
        }

        if (right.TypeId is Spk.Integer)
        {
            return ((SpkFloat)left).Value >= ((SpkInteger)right).Value ? True : False;
        }

        return base.GteOp(ctx, left, right);
    }

    protected override SpkObject LteOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId is Spk.Float)
        {
            return ((SpkFloat)left).Value <= ((SpkFloat)right).Value ? True : False;
        }

        if (right.TypeId is Spk.Integer)
        {
            return ((SpkFloat)left).Value <= ((SpkInteger)right).Value ? True : False;
        }

        return base.LteOp(ctx, left, right);
    }

    protected override SpkObject NegOp(ExecutionContext ctx, SpkObject arg) => new SpkFloat(-((SpkFloat)arg).Value);

    protected override SpkObject PlusOp(ExecutionContext ctx, SpkObject arg) => arg;

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject self, SpkObject format)
    {
        if (format.TypeId is not Spk.String and not Spk.Char and not Spk.Nil)
        {
            return ctx.InvalidType(format);
        }

        try
        {
            var value = ((SpkFloat)self).Value;
            return new SpkString(format.TypeId is Spk.Nil
                ? value.ToString(SystemCulture.NumberFormat)
                : value.ToString(format.ToString(), SystemCulture.NumberFormat));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId != Spk.Integer)
        {
            return base.CastOp(ctx, self, targetType);
        }

        return SpkInteger.TryFromDouble(((SpkFloat)self).Value, out var converted)
            ? converted
            : ctx.Overflow();
    }
    #endregion

    [SpkMethod(BuiltinMethodNames.IsNaN)]
    internal static bool IsNaN(double self) => double.IsNaN(self);

    [SpkStaticProperty(BuiltinMethodNames.Max)]
    internal static SpkObject Max() => SpkFloat.Max;

    [SpkStaticProperty(BuiltinMethodNames.Min)]
    internal static SpkObject Min() => SpkFloat.Min;

    [SpkStaticProperty]
    internal static SpkObject Infinity() => SpkFloat.PositiveInfinity;

    [SpkStaticProperty(BuiltinMethodNames.Default)]
    internal static SpkObject Default() => SpkFloat.Zero;

    [SpkStaticMethod(BuiltinMethodNames.Parse)]
    internal static double? Parse(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, InvariantCulture.NumberFormat, out var i))
        {
            return i;
        }

        return default;
    }

    [SpkStaticMethod(BuiltinMethodNames.Float)]
    internal static double? Convert(SpkObject value)
    {
        if (value is SpkFloat f)
        {
            return f.Value;
        }

        if (value is SpkInteger i)
        {
            return i.Value;
        }

        if (value.TypeId is Spk.Char or Spk.String)
        {
            return Parse(value.ToString());
        }

        throw new SpkCodeException(SpkError.InvalidType, value);
    }
}
