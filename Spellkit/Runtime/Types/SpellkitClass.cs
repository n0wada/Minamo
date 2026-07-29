using Spellkit.Compiler;
using System.Collections.Generic;

namespace Spellkit.Runtime.Types;

public sealed class SpellkitClass : SpellkitObject, IProduction
{
    public string Constructor { get; }

    public SpellkitTuple Fields { get; }

    internal Unit DeclaringUnit { get; }

    internal SpellkitTuple Inits { get; }

    internal SpellkitTypeInfo DecType { get; }

    public override string TypeName => DecType.ReflectedTypeName;

    internal SpellkitClass(SpellkitTypeInfo type, string ctor, SpellkitTuple fields, SpellkitTuple inits, Unit unit) : base(type.ReflectedTypeId) =>
        (DecType, Constructor, Fields, Inits, DeclaringUnit) = (type, ctor, fields, inits, unit);

    public override object ToObject() => this;

    public override int GetHashCode() => HashCode.Combine(Constructor, Fields);

    public override bool Equals(SpellkitObject? other) =>
        other is not null && DecType.TypeId == other.TypeId && other is SpellkitClass t
            && t.Constructor == Constructor && t.Fields.Equals(Fields);

    public override SpellkitObject Clone() => new SpellkitClass(DecType, Constructor, Fields, Inits, DeclaringUnit);

    internal SpellkitObject GetPrivate(ExecutionContext ctx, string field)
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

    internal SpellkitObject SetPrivate(ExecutionContext ctx, string field, SpellkitObject value)
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

internal sealed class SpellkitClassInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName { get; }

    public override int ReflectedTypeId { get; }

    public SpellkitClassInfo(string typeName, int typeCode) =>
        (ReflectedTypeName, ReflectedTypeId) = (typeName, typeCode);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject format)
    {
        var value = (SpellkitClass)self;

        IEnumerable<SpellkitObject> Iterate()
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
                return new SpellkitString($"{self.TypeName}()");
            }

            if (self.TypeName == value.Constructor)
            {
                return new SpellkitString($"{self.TypeName}({Iterate().ToLiteral(ctx)})");
            }

            if (value.Fields.Count == 0)
            {
                return new SpellkitString($"{self.TypeName}.{value.Constructor}()");
            }

            return new SpellkitString($"{self.TypeName}.{value.Constructor}({Iterate().ToLiteral(ctx)})");
        }
        catch (SpellkitCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            SpellkitTypeCodes.Dictionary => ((SpellkitClass)self).Fields.ToSpellkitDictionary(),
            SpellkitTypeCodes.Tuple => ((SpellkitClass)self).Fields,
            SpellkitTypeCodes.Array => new SpellkitArray(((SpellkitClass)self).Fields.ToArray()),
            _ => base.CastOp(ctx, self, targetType)
        };
}
