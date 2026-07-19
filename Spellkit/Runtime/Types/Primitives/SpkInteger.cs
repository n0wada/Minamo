using Spellkit.Codegen;
using System.Globalization;

namespace Spellkit.Runtime.Types;

public sealed class SpkInteger : SpkObject
{
    public static readonly SpkInteger Zero = new(0L);
    public static readonly SpkInteger MinusOne = new(-1L);
    public static readonly SpkInteger One = new(1L);
    public static readonly SpkInteger Two = new(2L);
    public static readonly SpkInteger Three = new(3L);
    public static readonly SpkInteger Max = new(long.MaxValue);
    public static readonly SpkInteger Min = new(long.MinValue);

    public readonly long Value;

    public override string TypeName => nameof(Spk.Integer);

    public static SpkInteger Get(long i) =>
        i switch
        {
            -1 => MinusOne,
            0 => Zero,
            1 => One,
            2 => Two,
            3 => Three,
            _ => new SpkInteger(i)
        };

    public SpkInteger(long value) : base(Spk.Integer) => this.Value = value;

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

    internal static bool TryFromDouble(double value, out SpkInteger result)
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

    public override bool Equals(SpkObject? obj) => obj is SpkInteger i && Value == i.Value;

    public override object ToObject() => Value == (int)Value ? Convert.ChangeType(Value, BCL.Int32) : Value;

    public override SpkObject Clone() => this;
}

[SpkType]
internal sealed partial class SpkIntegerTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Integer);

    public override int ReflectedTypeId => Spk.Integer;

    public SpkIntegerTypeInfo()
    {
        AddMixins(Spk.Number, Spk.Order, Spk.Equatable);
    }

    #region Operations
    protected override SpkObject AddOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i8)
        {
            return new SpkInteger(((SpkInteger)left).Value + i8.Value);
        }

        if (right is SpkFloat r8)
        {
            return new SpkFloat(((SpkInteger)left).Value + r8.Value);
        }

        return base.AddOp(ctx, left, right);
    }

    protected override SpkObject SubOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i8)
        {
            return new SpkInteger(((SpkInteger)left).Value - i8.Value);
        }

        if (right is SpkFloat r8)
        {
            return new SpkFloat(((SpkInteger)left).Value - r8.Value);
        }

        return base.SubOp(ctx, left, right);
    }

    protected override SpkObject MulOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i8)
        {
            return new SpkInteger(((SpkInteger)left).Value * i8.Value);
        }

        if (right is SpkFloat r8)
        {
            return new SpkFloat(((SpkInteger)left).Value * r8.Value);
        }

        return base.MulOp(ctx, left, right);
    }

    protected override SpkObject DivOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i8)
        {
            if (i8.Value == 0)
            {
                return ctx.DivideByZero();
            }

            if (((SpkInteger)left).Value == long.MinValue && i8.Value == -1)
            {
                return ctx.Overflow();
            }

            return new SpkInteger(((SpkInteger)left).Value / i8.Value);
        }

        if (right is SpkFloat r8)
        {
            return new SpkFloat(((SpkInteger)left).Value / r8.Value);
        }

        return base.DivOp(ctx, left, right);
    }

    protected override SpkObject RemOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i8)
        {
            if (i8.Value == 0)
            {
                return ctx.DivideByZero();
            }

            if (((SpkInteger)left).Value == long.MinValue && i8.Value == -1)
            {
                return ctx.Overflow();
            }

            return new SpkInteger(((SpkInteger)left).Value % i8.Value);
        }

        if (right is SpkFloat r8)
        {
            return new SpkFloat(((SpkInteger)left).Value % r8.Value);
        }

        return base.RemOp(ctx, left, right);
    }

    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i8)
        {
            return ((SpkInteger)left).Value == i8.Value ? True : False;
        }

        if (right is SpkFloat r8)
        {
            return ((SpkInteger)left).Value == r8.Value ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override SpkObject NeqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i8)
        {
            return ((SpkInteger)left).Value != i8.Value ? True : False;
        }

        if (right is SpkFloat r8)
        {
            return ((SpkInteger)left).Value != r8.Value ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override SpkObject GtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i8)
        {
            return ((SpkInteger)left).Value > i8.Value ? True : False;
        }

        if (right is SpkFloat r8)
        {
            return ((SpkInteger)left).Value > r8.Value ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override SpkObject LtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i8)
        {
            return ((SpkInteger)left).Value < i8.Value ? True : False;
        }

        if (right is SpkFloat r8)
        {
            return ((SpkInteger)left).Value < r8.Value ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override SpkObject GteOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i8)
        {
            return ((SpkInteger)left).Value >= i8.Value ? True : False;
        }

        if (right is SpkFloat r8)
        {
            return ((SpkInteger)left).Value >= r8.Value ? True : False;
        }

        return base.GteOp(ctx, left, right);
    }

    protected override SpkObject LteOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkInteger i8)
        {
            return ((SpkInteger)left).Value <= i8.Value ? True : False;
        }

        if (right is SpkFloat r8)
        {
            return ((SpkInteger)left).Value <= r8.Value ? True : False;
        }

        return base.LteOp(ctx, left, right);
    }

    protected override SpkObject NegOp(ExecutionContext ctx, SpkObject arg) => new SpkInteger(-((SpkInteger)arg).Value);

    protected override SpkObject PlusOp(ExecutionContext ctx, SpkObject arg) => arg;

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject self, SpkObject format)
    {
        if (format.TypeId is not Spk.String and not Spk.Char and not Spk.Nil)
        {
            return ctx.InvalidType(format);
        }

        try
        {
            var value = ((SpkInteger)self).Value;
            return new SpkString(format.TypeId is Spk.Nil
                ? value.ToString(SystemCulture.NumberFormat)
                : value.ToString(format.ToString(), SystemCulture.NumberFormat));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            Spk.Float => new SpkFloat(((SpkInteger)self).Value),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [SpkMethod(BuiltinMethodNames.IsMultipleOf)]
    internal static bool IsMultipleOf(long self, long value) => (self % value) == 0;

    [SpkStaticMethod(BuiltinMethodNames.Parse)]
    internal static long? Parse(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, InvariantCulture.NumberFormat, out var i))
        {
            return i;
        }

        return default;
    }

    [SpkStaticMethod(BuiltinMethodNames.Integer)]
    internal static SpkObject CreateNew(ExecutionContext ctx, SpkObject value)
    {
        if (value is SpkInteger i8)
        {
            return i8;
        }

        if (value is SpkFloat r8)
        {
            return SpkInteger.TryFromDouble(r8.Value, out var converted)
                ? converted
                : ctx.Overflow();
        }

        if (value.TypeId is Spk.Char or Spk.String)
        {
            var parsed = Parse(value.ToString());
            return parsed is null ? Nil : SpkInteger.Get(parsed.Value);
        }

        throw new SpkCodeException(SpkError.InvalidType, value);
    }

    [SpkStaticProperty(BuiltinMethodNames.Max)]
    internal static SpkObject Max() => SpkInteger.Max;

    [SpkStaticProperty(BuiltinMethodNames.Min)]
    internal static SpkObject Min() => SpkInteger.Min;

    [SpkStaticProperty(BuiltinMethodNames.Default)]
    internal static SpkObject Default() => SpkInteger.Zero;
}
