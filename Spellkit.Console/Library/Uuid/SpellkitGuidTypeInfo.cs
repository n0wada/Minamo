using System.Linq;
using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Uuid;

[SpellkitType]
public sealed partial class SpellkitGuidTypeInfo : SpellkitForeignTypeInfo<UuidModule>
{
    private const string GuidType = "Guid";

    public override string ReflectedTypeName => GuidType;

    public SpellkitGuidTypeInfo() => AddMixins(SpellkitTypeCodes.Order, SpellkitTypeCodes.Equatable);

    #region Operations
    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        new SpellkitString("{" + arg.ToString().ToUpper() + "}");

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return False;
        }

        return ((SpellkitGuid)left).Value == ((SpellkitGuid)right).Value ? True : False;
    }

    protected override SpellkitObject GtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (SpellkitBool)(((SpellkitGuid)left).Value.CompareTo(((SpellkitGuid)right).Value) > 0);
    }

    protected override SpellkitObject GteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (SpellkitBool)(((SpellkitGuid)left).Value.CompareTo(((SpellkitGuid)right).Value) >= 0);
    }

    protected override SpellkitObject LtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (SpellkitBool)(((SpellkitGuid)left).Value.CompareTo(((SpellkitGuid)right).Value) < 0);
    }

    protected override SpellkitObject LteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (SpellkitBool)(((SpellkitGuid)left).Value.CompareTo(((SpellkitGuid)right).Value) <= 0);
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == SpellkitTypeCodes.String)
        {
            return self.ToString(ctx);
        }

        return base.CastOp(ctx, self, targetType);
    }
    #endregion

    [SpellkitMethod]
    internal static SpellkitObject ToByteArray(ExecutionContext ctx, SpellkitGuid self) =>
        new SpellkitArray(self.Value.ToByteArray()
            .Select(value => (SpellkitObject)SpellkitInteger.Get(value))
            .ToArray());

    [SpellkitStaticMethod]
    internal static SpellkitObject Parse(ExecutionContext ctx, string value)
    {
        try
        {
            return new SpellkitGuid(ctx.Type<SpellkitGuidTypeInfo>(), Guid.Parse(value));
        }
        catch (FormatException)
        {
            return ctx.InvalidValue(value);
        }
    }

    [SpellkitStaticMethod]
    internal static SpellkitObject FromByteArray(ExecutionContext ctx, SpellkitObject value)
    {
        try
        {
            var bytes = (byte[]?)TypeConverter.ConvertTo(ctx, value, typeof(byte[]));
            return ctx.HasErrors || bytes is null
                ? Nil
                : new SpellkitGuid(ctx.Type<SpellkitGuidTypeInfo>(), new(bytes));
        }
        catch (ArgumentException)
        {
            return ctx.InvalidValue(value);
        }
    }

    [SpellkitStaticMethod(GuidType)]
    internal static SpellkitObject NewGuid(ExecutionContext ctx) => new SpellkitGuid(ctx.Type<SpellkitGuidTypeInfo>(), Guid.NewGuid());

    [SpellkitStaticProperty]
    internal static SpellkitObject Default(ExecutionContext ctx) => new SpellkitGuid(ctx.Type<SpellkitGuidTypeInfo>(), Guid.Empty);

    [SpellkitStaticProperty]
    internal static SpellkitObject Empty(ExecutionContext ctx) => Default(ctx);
}
