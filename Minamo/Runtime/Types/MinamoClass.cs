using Minamo.Compiler;
using System.Collections.Generic;

namespace Minamo.Runtime.Types;

public sealed class MinamoClass : MinamoObject, IProduction
{
    public string Constructor { get; }

    public MinamoTuple Fields { get; }

    internal Unit DeclaringUnit { get; }

    internal MinamoTuple Inits { get; }

    internal MinamoTypeInfo DecType { get; }

    public override string TypeName => DecType.ReflectedTypeName;

    internal MinamoClass(MinamoTypeInfo type, string ctor, MinamoTuple fields, MinamoTuple inits, Unit unit) : base(type.ReflectedTypeId) =>
        (DecType, Constructor, Fields, Inits, DeclaringUnit) = (type, ctor, fields, inits, unit);

    public override object ToObject() => this;

    public override int GetHashCode() => HashCode.Combine(Constructor, Fields);

    public override bool Equals(MinamoObject? other) =>
        other is not null && DecType.TypeId == other.TypeId && other is MinamoClass t
            && t.Constructor == Constructor && t.Fields.Equals(Fields);

    public override MinamoObject Clone() => new MinamoClass(DecType, Constructor, Fields, Inits, DeclaringUnit);

    internal MinamoObject GetPrivate(ExecutionContext ctx, string field)
    {
        if (!Inits.TryGetItem(field, out var item))
        {
            if (!Fields.TryGetItem(field, out item))
            {
                if (DecType.TryGetInstanceMember(ctx, this, field, out item))
                {
                    return item!;
                }

                return ctx.IndexOutOfRange(field);
            }
        }

        return item;
    }

    internal MinamoObject SetPrivate(ExecutionContext ctx, string field, MinamoObject value)
    {
        if (!Inits.TrySetItem(field, value))
        {
            if (!Fields.TrySetItem(field, value))
            {
                return ctx.IndexOutOfRange(field);
            }
        }

        return Nil;
    }
}

internal sealed class MinamoClassInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName { get; }

    public override int ReflectedTypeId { get; }

    public MinamoClassInfo(string typeName, int typeCode) =>
        (ReflectedTypeName, ReflectedTypeId) = (typeName, typeCode);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject self, MinamoObject format)
    {
        var value = (MinamoClass)self;

        IEnumerable<MinamoObject> Iterate()
        {
            var fields = value.Fields.UnsafeAccess();
            for (var i = 0; i < value.Fields.Count; i++)
            {
                yield return fields[i];
            }
        }

        try
        {
            if (self.TypeName == value.Constructor && value.Fields.Count == 0)
            {
                return new MinamoString($"{self.TypeName}()");
            }

            if (self.TypeName == value.Constructor)
            {
                return new MinamoString($"{self.TypeName}({Iterate().ToLiteral(ctx)})");
            }

            if (value.Fields.Count == 0)
            {
                return new MinamoString($"{self.TypeName}.{value.Constructor}()");
            }

            return new MinamoString($"{self.TypeName}.{value.Constructor}({Iterate().ToLiteral(ctx)})");
        }
        catch (MinamoCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            MinamoTypeCodes.Dictionary => ((MinamoClass)self).Fields.ToMinamoDictionary(),
            MinamoTypeCodes.Tuple => ((MinamoClass)self).Fields,
            MinamoTypeCodes.Array => new MinamoArray(((MinamoClass)self).Fields.ToArray()),
            _ => base.CastOp(ctx, self, targetType)
        };
}
