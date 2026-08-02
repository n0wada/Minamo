using Spellkit.Compiler;
using Spellkit.Debug;
using System.Collections.Generic;
using System.Linq;

namespace Spellkit.Runtime.Types;

internal sealed class SpellkitCollectionMixin : SpellkitMixin<SpellkitCollectionMixin>
{
    public SpellkitCollectionMixin() : base(SpellkitTypeCodes.Collection)
    {
        AddMixins(SpellkitTypeCodes.Lookup);
        Members.Add(Builtins.Length, Unary(Builtins.Length, SpellkitLookupMixin.GetLength));
        Members.Add(Builtins.Get, Binary(Builtins.Get, SpellkitLookupMixin.Getter, "index"));
        Members.Add(Builtins.Set, Ternary(Builtins.Set, Setter, "index", "value"));
        SetSupportedOperations(Ops.Set);
    }

    private static SpellkitObject Setter(ExecutionContext ctx, SpellkitObject self, SpellkitObject index, SpellkitObject value)
    {
        ((SpellkitClass)self).Fields.SetItem(ctx, index, value);
        return Nil;
    }
}
internal sealed class SpellkitContainerMixin : SpellkitMixin<SpellkitContainerMixin>
{
    public SpellkitContainerMixin() : base(SpellkitTypeCodes.Container)
    {
        Members.Add(Builtins.In, Binary(Builtins.In, IsIn, "value"));
        SetSupportedOperations(Ops.In);
    }

    private static SpellkitObject IsIn(ExecutionContext _, SpellkitObject self, SpellkitObject field)
    {
        if (field.TypeId is not SpellkitTypeCodes.String and not SpellkitTypeCodes.Char)
        {
            return False;
        }

        return ((SpellkitClass)self).Fields.GetOrdinal(field.ToString()) is not -1 ? True : False;
    }
}

internal sealed class SpellkitDisposableMixin : SpellkitMixin<SpellkitDisposableMixin>
{
    public SpellkitDisposableMixin() : base(SpellkitTypeCodes.Disposable)
    {
        Members.Add(Builtins.Dispose, Unary(Builtins.Dispose, Dispose));
    }

    private static SpellkitObject Dispose(ExecutionContext ctx, SpellkitObject self) =>
        ctx.NotImplemented(Builtins.Dispose);
}

internal sealed class SpellkitEquatableMixin : SpellkitMixin<SpellkitEquatableMixin>
{
    public SpellkitEquatableMixin() : base(SpellkitTypeCodes.Equatable)
    {
        Members.Add(Builtins.Eq, Binary(Builtins.Eq, Equatable));
    }

    private static SpellkitObject Equatable(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        var self = (SpellkitClass)left;

        if (self.TypeId == right.TypeId && right is SpellkitClass t && t.Constructor == self.Constructor)
        {
            try
            {
                return SpellkitTuple.Equals(ctx, self.Fields, t.Fields);
            }
            catch (SpellkitCodeException ex)
            {
                ctx.Error = ex.Error;
                return Nil;
            }
        }

        return False;
    }
}

internal sealed class SpellkitFunctorMixin : SpellkitMixin<SpellkitFunctorMixin>
{
    public SpellkitFunctorMixin() : base(SpellkitTypeCodes.Functor) =>
        Members.Add(Builtins.Call, Unary(Builtins.Call, SelfCall));

    private static SpellkitObject SelfCall(ExecutionContext ctx, SpellkitObject self) =>
        ctx.NotImplemented(Builtins.Call);
}

internal sealed class SpellkitIdentityMixin : SpellkitMixin<SpellkitIdentityMixin>
{
    public SpellkitIdentityMixin() : base(SpellkitTypeCodes.Identity) =>
        Members.Add(Builtins.Clone, Unary(Builtins.Clone, GetIdentity));

    private static SpellkitObject GetIdentity(ExecutionContext _, SpellkitObject arg) => arg;
}

internal sealed class SpellkitLookupMixin : SpellkitMixin<SpellkitLookupMixin>
{
    public SpellkitLookupMixin() : base(SpellkitTypeCodes.Lookup)
    {
        Members.Add(Builtins.Length, Unary(Builtins.Length, GetLength));
        Members.Add(Builtins.Get, Binary(Builtins.Get, Getter, "index"));
        SetSupportedOperations(Ops.Get | Ops.Len);
    }

    public static SpellkitObject GetLength(ExecutionContext ctx, SpellkitObject self) =>
        SpellkitInteger.Get(((SpellkitClass)self).Fields.Count);

    public static SpellkitObject Getter(ExecutionContext ctx, SpellkitObject self, SpellkitObject index) =>
        ((SpellkitClass)self).Fields.GetItem(ctx, index);
}

public abstract class SpellkitMixin<T> : SpellkitTypeInfo
    where T : SpellkitMixin<T>, new()
{
    public static T Instance { get; } = new T();

    public override string ReflectedTypeName { get; }

    public override int ReflectedTypeId { get; }

    protected SpellkitMixin(int typeId) =>
        (ReflectedTypeId, ReflectedTypeName, Closed) = (typeId, SpellkitTypeCodes.GetTypeNameByCode(typeId), true);
}

internal sealed class SpellkitNumberMixin : SpellkitMixin<SpellkitNumberMixin>
{
    public SpellkitNumberMixin() : base(SpellkitTypeCodes.Number)
    {
        Members.Add(Builtins.Add, Binary(Builtins.Add, Sum));
        Members.Add(Builtins.Sub, Binary(Builtins.Sub, Subtract));
        Members.Add(Builtins.Mul, Binary(Builtins.Mul, Multiply));
        Members.Add(Builtins.Div, Binary(Builtins.Div, Divide));
        Members.Add(Builtins.Rem, Binary(Builtins.Rem, Remainder));
        Members.Add(Builtins.Neg, Unary(Builtins.Neg, Negate));
        Members.Add(Builtins.Plus, Unary(Builtins.Plus, MakePlus));
        SetSupportedOperations(Ops.Add | Ops.Sub | Ops.Div | Ops.Mul | Ops.Rem | Ops.Neg | Ops.Plus);
    }

    private static SpellkitObject Sum(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ctx.NotImplemented(Builtins.Add);

    private static SpellkitObject Subtract(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ctx.NotImplemented(Builtins.Sub);

    private static SpellkitObject Multiply(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ctx.NotImplemented(Builtins.Mul);

    private static SpellkitObject Divide(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ctx.NotImplemented(Builtins.Div);

    private static SpellkitObject Remainder(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ctx.NotImplemented(Builtins.Rem);

    private static SpellkitObject Negate(ExecutionContext ctx, SpellkitObject left) =>
        ctx.NotImplemented(Builtins.Neg);

    private static SpellkitObject MakePlus(ExecutionContext ctx, SpellkitObject left) =>
        ctx.NotImplemented(Builtins.Plus);
}

internal sealed class SpellkitObjectMixin : SpellkitMixin<SpellkitObjectMixin>
{
    public SpellkitObjectMixin() : base(SpellkitTypeCodes.Object) { }
}

internal sealed class SpellkitOrderMixin : SpellkitMixin<SpellkitOrderMixin>
{
    public SpellkitOrderMixin() : base(SpellkitTypeCodes.Order)
    {
        Members.Add(Builtins.Gt, Binary(Builtins.Gt, Greater));
        Members.Add(Builtins.Lt, Binary(Builtins.Lt, Lesser));
        SetSupportedOperations(Ops.Gt | Ops.Lt | Ops.Gte | Ops.Lte);
    }

    private static SpellkitObject Greater(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        try
        {
            return SpellkitTuple.Greater(ctx, ((SpellkitClass)left).Fields, ((SpellkitClass)right).Fields);
        }
        catch(SpellkitCodeException e)
        {
            ctx.Error = e.Error;
            return Nil;
        }
    }

    private static SpellkitObject Lesser(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        try
        {
            return SpellkitTuple.Lesser(ctx, ((SpellkitClass)left).Fields, ((SpellkitClass)right).Fields);
        }
        catch (SpellkitCodeException e)
        {
            ctx.Error = e.Error;
            return Nil;
        }
    }
}

internal sealed class SpellkitSequenceMixin : SpellkitMixin<SpellkitSequenceMixin>
{
    public SpellkitSequenceMixin() : base(SpellkitTypeCodes.Sequence)
    {
        Members.Add(Builtins.Iterate, Unary(Builtins.Iterate, Iterate));
        Members.Add(BuiltinMethodNames.Map, new SpellkitExternalFunction(BuiltinMethodNames.Map, false, Map, new Par("converter")));
        Members.Add(BuiltinMethodNames.Filter, new SpellkitExternalFunction(BuiltinMethodNames.Filter, false, Filter, new Par("predicate")));
        Members.Add(BuiltinMethodNames.Take, new SpellkitExternalFunction(BuiltinMethodNames.Take, false, Take, new Par("count")));
        Members.Add(BuiltinMethodNames.Skip, new SpellkitExternalFunction(BuiltinMethodNames.Skip, false, Skip, new Par("count")));
        Members.Add(BuiltinMethodNames.Reduce, new SpellkitExternalFunction(BuiltinMethodNames.Reduce, false, Reduce, new Par("converter"), new Par("initial", 0)));
        Members.Add(BuiltinMethodNames.Any, new SpellkitExternalFunction(BuiltinMethodNames.Any, false, Any, new Par("predicate")));
        Members.Add(BuiltinMethodNames.All, new SpellkitExternalFunction(BuiltinMethodNames.All, false, All, new Par("predicate")));
        Members.Add(BuiltinMethodNames.ToArray, new SpellkitExternalFunction(BuiltinMethodNames.ToArray, false, ToArray));
        Members.Add(BuiltinMethodNames.ToSet, new SpellkitExternalFunction(BuiltinMethodNames.ToSet, false, ToSet));
        SetSupportedOperations(Ops.Iter);
    }

    private static SpellkitObject Iterate(ExecutionContext _, SpellkitObject self) => self switch
    {
        SpellkitIterator iterator => iterator,
        IEnumerable<SpellkitObject> sequence => SpellkitIterator.Create(sequence),
        SpellkitClass instance => SpellkitIterator.Create(instance.Fields),
        _ => Nil
    };

    private static SpellkitObject Map(ExecutionContext ctx, SpellkitObject? self, SpellkitObject[] args)
    {
        var converter = args[0].ToFunction(ctx);
        return converter is null
            ? Nil
            : SpellkitIterator.Create(new MapEnumerable(ctx, Source(ctx, self), converter));
    }

    private static SpellkitObject Filter(ExecutionContext ctx, SpellkitObject? self, SpellkitObject[] args)
    {
        var predicate = args[0].ToFunction(ctx);
        return predicate is null
            ? Nil
            : SpellkitIterator.Create(new FilterEnumerable(ctx, Source(ctx, self), predicate));
    }

    private static SpellkitObject Take(ExecutionContext ctx, SpellkitObject? self, SpellkitObject[] args)
    {
        if (args[0] is not SpellkitInteger count || !count.TryGetInt32(out var value))
        {
            return ctx.InvalidType(args[0]);
        }

        return SpellkitIterator.Create(Source(ctx, self).Take(value < 0 ? 0 : value));
    }

    private static SpellkitObject Skip(ExecutionContext ctx, SpellkitObject? self, SpellkitObject[] args)
    {
        if (args[0] is not SpellkitInteger count || !count.TryGetInt32(out var value))
        {
            return ctx.InvalidType(args[0]);
        }

        return SpellkitIterator.Create(Source(ctx, self).Skip(value < 0 ? 0 : value));
    }

    private static SpellkitObject Reduce(ExecutionContext ctx, SpellkitObject? self, SpellkitObject[] args)
    {
        var converter = args[0].ToFunction(ctx);
        if (converter is null)
        {
            return Nil;
        }

        var result = args[1];
        foreach (var item in Source(ctx, self))
        {
            result = converter.Call(ctx, result, item);
            if (ctx.HasErrors)
            {
                return Nil;
            }
        }

        return result;
    }

    private static SpellkitObject Any(ExecutionContext ctx, SpellkitObject? self, SpellkitObject[] args)
    {
        var predicate = args[0].ToFunction(ctx);
        if (predicate is null)
        {
            return Nil;
        }

        foreach (var item in Source(ctx, self))
        {
            var result = predicate.Call(ctx, item);
            if (ctx.HasErrors)
            {
                return Nil;
            }

            if (result.IsTrue())
            {
                return True;
            }
        }

        return False;
    }

    private static SpellkitObject All(ExecutionContext ctx, SpellkitObject? self, SpellkitObject[] args)
    {
        var predicate = args[0].ToFunction(ctx);
        if (predicate is null)
        {
            return Nil;
        }

        foreach (var item in Source(ctx, self))
        {
            var result = predicate.Call(ctx, item);
            if (ctx.HasErrors)
            {
                return Nil;
            }

            if (!result.IsTrue())
            {
                return False;
            }
        }

        return True;
    }

    private static SpellkitObject ToArray(ExecutionContext ctx, SpellkitObject? self, SpellkitObject[] _) =>
        new SpellkitArray(Source(ctx, self).ToArray());

    private static SpellkitObject ToSet(ExecutionContext ctx, SpellkitObject? self, SpellkitObject[] _)
    {
        var values = new HashSet<SpellkitObject>();
        values.UnionWith(Source(ctx, self));
        return new SpellkitSet(values);
    }

    private static IEnumerable<SpellkitObject> Source(ExecutionContext ctx, SpellkitObject? self) =>
        SpellkitIterator.ToEnumerable(ctx, self!);
}
