using Spellkit.Compiler;
using System.Collections.Generic;

namespace Spellkit.Runtime.Types;

public sealed class SpkClass : SpkObject, IProduction
{
    public string Constructor { get; }

    public SpkTuple Fields { get; }

    internal Unit DeclaringUnit { get; }

    internal SpkTuple Inits { get; }

    internal SpkTypeInfo DecType { get; }

    public override string TypeName => DecType.ReflectedTypeName;
    
    internal SpkClass(SpkTypeInfo type, string ctor, SpkTuple fields, SpkTuple inits, Unit unit) : base(type.ReflectedTypeId) =>
        (DecType, Constructor, Fields, Inits, DeclaringUnit) = (type, ctor, fields, inits, unit);

    public override object ToObject() => this;

    public override int GetHashCode() => HashCode.Combine(Constructor, Fields);

    public override bool Equals(SpkObject? other) =>
        other is not null && DecType.TypeId == other.TypeId && other is SpkClass t 
            && t.Constructor == Constructor && t.Fields.Equals(Fields);

    public override SpkObject Clone() => new SpkClass(DecType, Constructor, Fields, Inits, DeclaringUnit);

    internal SpkObject GetPrivate(ExecutionContext ctx, string field)
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

    internal SpkObject SetPrivate(ExecutionContext ctx, string field, SpkObject value)
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

internal sealed class SpkClassInfo : SpkTypeInfo
{
    public override string ReflectedTypeName { get; }

    public override int ReflectedTypeId { get; }

    public SpkClassInfo(string typeName, int typeCode) =>
        (ReflectedTypeName, ReflectedTypeId) = (typeName, typeCode);

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject self, SpkObject format)
    {
        var value = (SpkClass)self;

        IEnumerable<SpkObject> Iterate()
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
                return new SpkString($"{self.TypeName}()");
            }

            if (self.TypeName == value.Constructor)
            {
                return new SpkString($"{self.TypeName}({Iterate().ToLiteral(ctx)})");
            }

            if (value.Fields.Count == 0)
            {
                return new SpkString($"{self.TypeName}.{value.Constructor}()");
            }

            return new SpkString($"{self.TypeName}.{value.Constructor}({Iterate().ToLiteral(ctx)})");
        }
        catch (SpkCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            Spk.Dictionary => new SpkDictionary(((SpkClass)self).Fields.ConvertToDictionary()),
            Spk.Tuple => ((SpkClass)self).Fields,
            Spk.Array => new SpkArray(((SpkClass)self).Fields.ToArray()),
            _ => base.CastOp(ctx, self, targetType)
        };
}
