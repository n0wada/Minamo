using System.Linq;
using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Library.Uuid;

[MinamoType]
public sealed partial class MinamoGuidTypeInfo : MinamoForeignTypeInfo<UuidModule>
{
    private const string GuidType = "Guid";

    public override string ReflectedTypeName => GuidType;

    public MinamoGuidTypeInfo() => AddMixins(MinamoTypeCodes.Order, MinamoTypeCodes.Equatable);

    #region Operations
    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) =>
        new MinamoString("{" + arg.ToString().ToUpper() + "}");

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return False;
        }

        return ((MinamoGuid)left).Value == ((MinamoGuid)right).Value ? True : False;
    }

    protected override MinamoObject GtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (MinamoBool)(((MinamoGuid)left).Value.CompareTo(((MinamoGuid)right).Value) > 0);
    }

    protected override MinamoObject GteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (MinamoBool)(((MinamoGuid)left).Value.CompareTo(((MinamoGuid)right).Value) >= 0);
    }

    protected override MinamoObject LtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (MinamoBool)(((MinamoGuid)left).Value.CompareTo(((MinamoGuid)right).Value) < 0);
    }

    protected override MinamoObject LteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (left.TypeId != right.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return (MinamoBool)(((MinamoGuid)left).Value.CompareTo(((MinamoGuid)right).Value) <= 0);
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == MinamoTypeCodes.String)
        {
            return self.ToString(ctx);
        }

        return base.CastOp(ctx, self, targetType);
    }
    #endregion

    [MinamoMethod]
    internal static MinamoObject ToByteArray(ExecutionContext ctx, MinamoGuid self) =>
        new MinamoArray(self.Value.ToByteArray()
            .Select(value => (MinamoObject)MinamoInteger.Get(value))
            .ToArray());

    [MinamoStaticMethod]
    internal static MinamoObject Parse(ExecutionContext ctx, string value)
    {
        try
        {
            return new MinamoGuid(ctx.Type<MinamoGuidTypeInfo>(), Guid.Parse(value));
        }
        catch (FormatException)
        {
            return ctx.InvalidValue(value);
        }
    }

    [MinamoStaticMethod]
    internal static MinamoObject FromByteArray(ExecutionContext ctx, MinamoObject value)
    {
        try
        {
            var bytes = (byte[]?)TypeConverter.ConvertTo(ctx, value, typeof(byte[]));
            return ctx.HasErrors || bytes is null
                ? Nil
                : new MinamoGuid(ctx.Type<MinamoGuidTypeInfo>(), new(bytes));
        }
        catch (ArgumentException)
        {
            return ctx.InvalidValue(value);
        }
    }

    [MinamoStaticMethod(GuidType)]
    internal static MinamoObject NewGuid(ExecutionContext ctx) => new MinamoGuid(ctx.Type<MinamoGuidTypeInfo>(), Guid.NewGuid());

    [MinamoStaticProperty]
    internal static MinamoObject Default(ExecutionContext ctx) => new MinamoGuid(ctx.Type<MinamoGuidTypeInfo>(), Guid.Empty);

    [MinamoStaticProperty]
    internal static MinamoObject Empty(ExecutionContext ctx) => Default(ctx);
}
