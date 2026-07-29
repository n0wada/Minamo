using Spellkit.Codegen;
using System.Globalization;

namespace Spellkit.Runtime.Types;

public sealed class SpellkitInteger : SpellkitObject
{
    public static readonly SpellkitInteger Zero = new(0L);
    public static readonly SpellkitInteger MinusOne = new(-1L);
    public static readonly SpellkitInteger One = new(1L);
    public static readonly SpellkitInteger Two = new(2L);
    public static readonly SpellkitInteger Three = new(3L);
    public static readonly SpellkitInteger Max = new(long.MaxValue);
    public static readonly SpellkitInteger Min = new(long.MinValue);

    public readonly long Value;

    public override string TypeName => nameof(SpellkitTypeCodes.Integer);

    public static SpellkitInteger Get(long i) =>
        i switch
        {
            -1 => MinusOne,
            0 => Zero,
            1 => One,
            2 => Two,
            3 => Three,
            _ => new SpellkitInteger(i)
        };

    public SpellkitInteger(long value) : base(SpellkitTypeCodes.Integer) => this.Value = value;

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

    internal static bool TryFromDouble(double value, out SpellkitInteger result)
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

    public override bool Equals(SpellkitObject? obj) => obj is SpellkitInteger i && Value == i.Value;

    public override object ToObject() => Value == (int)Value ? Convert.ChangeType(Value, BCL.Int32) : Value;

    public override SpellkitObject Clone() => this;
}

[SpellkitType]
internal sealed partial class SpellkitIntegerTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Integer);

    public override int ReflectedTypeId => SpellkitTypeCodes.Integer;

    public SpellkitIntegerTypeInfo()
    {
        AddMixins(SpellkitTypeCodes.Number, SpellkitTypeCodes.Order, SpellkitTypeCodes.Equatable);
    }

    #region Operations
    protected override SpellkitObject AddOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i8)
        {
            return new SpellkitInteger(((SpellkitInteger)left).Value + i8.Value);
        }

        if (right is SpellkitFloat r8)
        {
            return new SpellkitFloat(((SpellkitInteger)left).Value + r8.Value);
        }

        return base.AddOp(ctx, left, right);
    }

    protected override SpellkitObject SubOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i8)
        {
            return new SpellkitInteger(((SpellkitInteger)left).Value - i8.Value);
        }

        if (right is SpellkitFloat r8)
        {
            return new SpellkitFloat(((SpellkitInteger)left).Value - r8.Value);
        }

        return base.SubOp(ctx, left, right);
    }

    protected override SpellkitObject MulOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i8)
        {
            return new SpellkitInteger(((SpellkitInteger)left).Value * i8.Value);
        }

        if (right is SpellkitFloat r8)
        {
            return new SpellkitFloat(((SpellkitInteger)left).Value * r8.Value);
        }

        return base.MulOp(ctx, left, right);
    }

    protected override SpellkitObject DivOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i8)
        {
            if (i8.Value == 0)
            {
                return ctx.DivideByZero();
            }

            if (((SpellkitInteger)left).Value == long.MinValue && i8.Value == -1)
            {
                return ctx.Overflow();
            }

            return new SpellkitInteger(((SpellkitInteger)left).Value / i8.Value);
        }

        if (right is SpellkitFloat r8)
        {
            return new SpellkitFloat(((SpellkitInteger)left).Value / r8.Value);
        }

        return base.DivOp(ctx, left, right);
    }

    protected override SpellkitObject RemOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i8)
        {
            if (i8.Value == 0)
            {
                return ctx.DivideByZero();
            }

            if (((SpellkitInteger)left).Value == long.MinValue && i8.Value == -1)
            {
                return ctx.Overflow();
            }

            return new SpellkitInteger(((SpellkitInteger)left).Value % i8.Value);
        }

        if (right is SpellkitFloat r8)
        {
            return new SpellkitFloat(((SpellkitInteger)left).Value % r8.Value);
        }

        return base.RemOp(ctx, left, right);
    }

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i8)
        {
            return ((SpellkitInteger)left).Value == i8.Value ? True : False;
        }

        if (right is SpellkitFloat r8)
        {
            return ((SpellkitInteger)left).Value == r8.Value ? True : False;
        }

        return base.EqOp(ctx, left, right);
    }

    protected override SpellkitObject NeqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i8)
        {
            return ((SpellkitInteger)left).Value != i8.Value ? True : False;
        }

        if (right is SpellkitFloat r8)
        {
            return ((SpellkitInteger)left).Value != r8.Value ? True : False;
        }

        return base.NeqOp(ctx, left, right);
    }

    protected override SpellkitObject GtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i8)
        {
            return ((SpellkitInteger)left).Value > i8.Value ? True : False;
        }

        if (right is SpellkitFloat r8)
        {
            return ((SpellkitInteger)left).Value > r8.Value ? True : False;
        }

        return base.GtOp(ctx, left, right);
    }

    protected override SpellkitObject LtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i8)
        {
            return ((SpellkitInteger)left).Value < i8.Value ? True : False;
        }

        if (right is SpellkitFloat r8)
        {
            return ((SpellkitInteger)left).Value < r8.Value ? True : False;
        }

        return base.LtOp(ctx, left, right);
    }

    protected override SpellkitObject GteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i8)
        {
            return ((SpellkitInteger)left).Value >= i8.Value ? True : False;
        }

        if (right is SpellkitFloat r8)
        {
            return ((SpellkitInteger)left).Value >= r8.Value ? True : False;
        }

        return base.GteOp(ctx, left, right);
    }

    protected override SpellkitObject LteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitInteger i8)
        {
            return ((SpellkitInteger)left).Value <= i8.Value ? True : False;
        }

        if (right is SpellkitFloat r8)
        {
            return ((SpellkitInteger)left).Value <= r8.Value ? True : False;
        }

        return base.LteOp(ctx, left, right);
    }

    protected override SpellkitObject NegOp(ExecutionContext ctx, SpellkitObject arg) => new SpellkitInteger(-((SpellkitInteger)arg).Value);

    protected override SpellkitObject PlusOp(ExecutionContext ctx, SpellkitObject arg) => arg;

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject format)
    {
        if (format.TypeId is not SpellkitTypeCodes.String and not SpellkitTypeCodes.Char and not SpellkitTypeCodes.Nil)
        {
            return ctx.InvalidType(format);
        }

        try
        {
            var value = ((SpellkitInteger)self).Value;
            return new SpellkitString(format.TypeId is SpellkitTypeCodes.Nil
                ? value.ToString(SystemCulture.NumberFormat)
                : value.ToString(format.ToString(), SystemCulture.NumberFormat));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Float => new SpellkitFloat(((SpellkitInteger)self).Value),
            _ => base.CastOp(ctx, self, targetType)
        };
    #endregion

    [SpellkitMethod(BuiltinMethodNames.IsMultipleOf)]
    internal static bool IsMultipleOf(long self, long value) => (self % value) == 0;

    [SpellkitStaticMethod(BuiltinMethodNames.Parse)]
    internal static long? Parse(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, InvariantCulture.NumberFormat, out var i))
        {
            return i;
        }

        return default;
    }

    [SpellkitStaticMethod(BuiltinMethodNames.Integer)]
    internal static SpellkitObject CreateNew(ExecutionContext ctx, SpellkitObject value)
    {
        if (value is SpellkitInteger i8)
        {
            return i8;
        }

        if (value is SpellkitFloat r8)
        {
            return SpellkitInteger.TryFromDouble(r8.Value, out var converted)
                ? converted
                : ctx.Overflow();
        }

        if (value.TypeId is SpellkitTypeCodes.Char or SpellkitTypeCodes.String)
        {
            var parsed = Parse(value.ToString());
            return parsed is null ? Nil : SpellkitInteger.Get(parsed.Value);
        }

        throw new SpellkitCodeException(SpellkitError.InvalidType, value);
    }

    [SpellkitStaticProperty(BuiltinMethodNames.Max)]
    internal static SpellkitObject Max() => SpellkitInteger.Max;

    [SpellkitStaticProperty(BuiltinMethodNames.Min)]
    internal static SpellkitObject Min() => SpellkitInteger.Min;

    [SpellkitStaticProperty(BuiltinMethodNames.Default)]
    internal static SpellkitObject Default() => SpellkitInteger.Zero;
}
