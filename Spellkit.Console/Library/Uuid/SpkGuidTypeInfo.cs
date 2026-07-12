using System.Linq;
using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Uuid;

[SpkType]
public sealed partial class SpkGuidTypeInfo : SpkForeignTypeInfo<UuidModule>
{
    private const string GuidType = "Guid";

    public override string ReflectedTypeName => GuidType;

    public SpkGuidTypeInfo() => AddMixins(Spk.Order, Spk.Equatable);

    #region Operations
    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format) =>
        new SpkString("{" + arg.ToString().ToUpper() + "}");

    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return False;
        }

        return ((SpkGuid)left).Value == ((SpkGuid)right).Value ? True : False;
    }

    protected override SpkObject GtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (SpkBool)(((SpkGuid)left).Value.CompareTo(((SpkGuid)right).Value) > 0);
    }

    protected override SpkObject GteOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (SpkBool)(((SpkGuid)left).Value.CompareTo(((SpkGuid)right).Value) >= 0);
    }

    protected override SpkObject LtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (SpkBool)(((SpkGuid)left).Value.CompareTo(((SpkGuid)right).Value) < 0);
    }

    protected override SpkObject LteOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (SpkBool)(((SpkGuid)left).Value.CompareTo(((SpkGuid)right).Value) <= 0);
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == Spk.String)
        {
            return self.ToString(ctx);
        }

        return base.CastOp(ctx, self, targetType);
    }
    #endregion

    [SpkMethod]
    internal static SpkObject ToByteArray(ExecutionContext ctx, SpkGuid self) =>
        new SpkArray(self.Value.ToByteArray()
            .Select(value => (SpkObject)SpkInteger.Get(value))
            .ToArray());

    [SpkStaticMethod]
    internal static SpkObject Parse(ExecutionContext ctx, string value)
    {
        try
        {
            return new SpkGuid(ctx.Type<SpkGuidTypeInfo>(), Guid.Parse(value));
        }
        catch (FormatException)
        {
            return ctx.InvalidValue(value);
        }
    }

    [SpkStaticMethod]
    internal static SpkObject FromByteArray(ExecutionContext ctx, SpkObject value)
    {
        try
        {
            var bytes = (byte[]?)TypeConverter.ConvertTo(ctx, value, typeof(byte[]));
            return ctx.HasErrors || bytes is null
                ? Nil
                : new SpkGuid(ctx.Type<SpkGuidTypeInfo>(), new(bytes));
        }
        catch (ArgumentException)
        {
            return ctx.InvalidValue(value);
        }
    }

    [SpkStaticMethod(GuidType)]
    internal static SpkObject NewGuid(ExecutionContext ctx) => new SpkGuid(ctx.Type<SpkGuidTypeInfo>(), Guid.NewGuid());

    [SpkStaticProperty]
    internal static SpkObject Default(ExecutionContext ctx) => new SpkGuid(ctx.Type<SpkGuidTypeInfo>(), Guid.Empty);

    [SpkStaticProperty]
    internal static SpkObject Empty(ExecutionContext ctx) => Default(ctx);
}
