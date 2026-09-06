using Minamo.Compiler;
using Minamo.Debug;
using System.Collections.Generic;
using System.Linq;

namespace Minamo.Runtime.Types;

internal sealed class MinamoCollectionMixin : MinamoMixin<MinamoCollectionMixin>
{
    public MinamoCollectionMixin() : base(MinamoTypeCodes.Collection)
    {
        AddMixins(MinamoTypeCodes.Lookup);
        Members.Add(Builtins.Length, Unary(Builtins.Length, MinamoLookupMixin.GetLength));
        Members.Add(Builtins.Get, Binary(Builtins.Get, MinamoLookupMixin.Getter, "index"));
        Members.Add(Builtins.Set, Ternary(Builtins.Set, Setter, "index", "value"));
        SetSupportedOperations(Ops.Set);
    }

    private static MinamoObject Setter(ExecutionContext ctx, MinamoObject self, MinamoObject index, MinamoObject value)
    {
        ((MinamoClass)self).Fields.SetItem(ctx, index, value);
        return Nil;
    }
}
internal sealed class MinamoContainerMixin : MinamoMixin<MinamoContainerMixin>
{
    public MinamoContainerMixin() : base(MinamoTypeCodes.Container)
    {
        Members.Add(Builtins.In, Binary(Builtins.In, IsIn, "value"));
        SetSupportedOperations(Ops.In);
    }

    private static MinamoObject IsIn(ExecutionContext _, MinamoObject self, MinamoObject field)
    {
        if (field.TypeId is not MinamoTypeCodes.String and not MinamoTypeCodes.Char)
        {
            return False;
        }

        return ((MinamoClass)self).Fields.GetOrdinal(field.ToString()) is not -1 ? True : False;
    }
}

internal sealed class MinamoDisposableMixin : MinamoMixin<MinamoDisposableMixin>
{
    public MinamoDisposableMixin() : base(MinamoTypeCodes.Disposable) { }
}

internal sealed class MinamoEquatableMixin : MinamoMixin<MinamoEquatableMixin>
{
    public MinamoEquatableMixin() : base(MinamoTypeCodes.Equatable)
    {
        Members.Add(Builtins.Eq, Binary(Builtins.Eq, Equatable));
    }

    private static MinamoObject Equatable(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        var self = (MinamoClass)left;

        if (self.TypeId == right.TypeId && right is MinamoClass t && t.Constructor == self.Constructor)
        {
            try
            {
                return MinamoTuple.Equals(ctx, self.Fields, t.Fields);
            }
            catch (MinamoCodeException ex)
            {
                ctx.Error = ex.Error;
                return Nil;
            }
        }

        return False;
    }
}

internal sealed class MinamoFunctorMixin : MinamoMixin<MinamoFunctorMixin>
{
    public MinamoFunctorMixin() : base(MinamoTypeCodes.Functor) { }
}

internal sealed class MinamoIdentityMixin : MinamoMixin<MinamoIdentityMixin>
{
    public MinamoIdentityMixin() : base(MinamoTypeCodes.Identity) =>
        Members.Add(Builtins.Clone, Unary(Builtins.Clone, GetIdentity));

    private static MinamoObject GetIdentity(ExecutionContext _, MinamoObject arg) => arg;
}

internal sealed class MinamoLookupMixin : MinamoMixin<MinamoLookupMixin>
{
    public MinamoLookupMixin() : base(MinamoTypeCodes.Lookup)
    {
        Members.Add(Builtins.Length, Unary(Builtins.Length, GetLength));
        Members.Add(Builtins.Get, Binary(Builtins.Get, Getter, "index"));
        SetSupportedOperations(Ops.Get | Ops.Len);
    }

    public static MinamoObject GetLength(ExecutionContext ctx, MinamoObject self) =>
        MinamoInteger.Get(((MinamoClass)self).Fields.Count);

    public static MinamoObject Getter(ExecutionContext ctx, MinamoObject self, MinamoObject index) =>
        ((MinamoClass)self).Fields.GetItem(ctx, index);
}

public abstract class MinamoMixin<T> : MinamoTypeInfo
    where T : MinamoMixin<T>, new()
{
    public static T Instance { get; } = new T();

    public override string ReflectedTypeName { get; }

    public override int ReflectedTypeId { get; }

    protected MinamoMixin(int typeId) =>
        (ReflectedTypeId, ReflectedTypeName, Closed) = (typeId, MinamoTypeCodes.GetTypeNameByCode(typeId), true);
}

internal sealed class MinamoNumberMixin : MinamoMixin<MinamoNumberMixin>
{
    public MinamoNumberMixin() : base(MinamoTypeCodes.Number) =>
        SetSupportedOperations(Ops.Add | Ops.Sub | Ops.Div | Ops.Mul | Ops.Rem | Ops.Neg | Ops.Plus);
}

internal static class MinamoMixinContracts
{
    private static readonly IReadOnlyList<string> disposable = [Builtins.Dispose];
    private static readonly IReadOnlyList<string> functor = [Builtins.Call];
    private static readonly IReadOnlyList<string> number = [
        Builtins.Add,
        Builtins.Sub,
        Builtins.Mul,
        Builtins.Div,
        Builtins.Rem,
        Builtins.Neg,
        Builtins.Plus
    ];
    private static readonly IReadOnlyList<string> none = [];

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> defaults =
        new Dictionary<string, IReadOnlySet<string>>
        {
            ["Collection"] = new HashSet<string>
            {
                Builtins.Length, Builtins.Get, Builtins.Set
            },
            ["Container"] = new HashSet<string>
            {
                Builtins.In
            },
            ["Equatable"] = new HashSet<string>
            {
                Builtins.Eq
            },
            ["Identity"] = new HashSet<string>
            {
                Builtins.Clone
            },
            ["Lookup"] = new HashSet<string>
            {
                Builtins.Length, Builtins.Get
            },
            ["Order"] = new HashSet<string>
            {
                Builtins.Gt, Builtins.Lt, Builtins.Gte, Builtins.Lte
            },
            ["Sequence"] = new HashSet<string>
            {
                Builtins.Iterate,
                BuiltinMethodNames.Map,
                BuiltinMethodNames.Filter,
                BuiltinMethodNames.Take,
                BuiltinMethodNames.Skip,
                BuiltinMethodNames.Reduce,
                BuiltinMethodNames.Any,
                BuiltinMethodNames.All,
                BuiltinMethodNames.ToArray,
                BuiltinMethodNames.ToSet
            }
        };

    public static IReadOnlyList<string> GetRequiredMembers(string mixinName) => mixinName switch
    {
        "Disposable" => disposable,
        "Functor" => functor,
        "Number" => number,
        _ => none
    };

    public static bool ProvidesDefaultMember(string mixinName, string memberName) =>
        defaults.TryGetValue(mixinName, out var members) && members.Contains(memberName);
}

internal sealed class MinamoObjectMixin : MinamoMixin<MinamoObjectMixin>
{
    public MinamoObjectMixin() : base(MinamoTypeCodes.Object) { }
}

internal sealed class MinamoOrderMixin : MinamoMixin<MinamoOrderMixin>
{
    public MinamoOrderMixin() : base(MinamoTypeCodes.Order)
    {
        Members.Add(Builtins.Gt, Binary(Builtins.Gt, Greater));
        Members.Add(Builtins.Lt, Binary(Builtins.Lt, Lesser));
        SetSupportedOperations(Ops.Gt | Ops.Lt | Ops.Gte | Ops.Lte);
    }

    private static MinamoObject Greater(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        try
        {
            return MinamoTuple.Greater(ctx, ((MinamoClass)left).Fields, ((MinamoClass)right).Fields);
        }
        catch(MinamoCodeException e)
        {
            ctx.Error = e.Error;
            return Nil;
        }
    }

    private static MinamoObject Lesser(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        try
        {
            return MinamoTuple.Lesser(ctx, ((MinamoClass)left).Fields, ((MinamoClass)right).Fields);
        }
        catch (MinamoCodeException e)
        {
            ctx.Error = e.Error;
            return Nil;
        }
    }
}

internal sealed class MinamoSequenceMixin : MinamoMixin<MinamoSequenceMixin>
{
    public MinamoSequenceMixin() : base(MinamoTypeCodes.Sequence)
    {
        Members.Add(Builtins.Iterate, Unary(Builtins.Iterate, Iterate));
        Members.Add(BuiltinMethodNames.Map, new MinamoExternalFunction(BuiltinMethodNames.Map, false, Map, new Par("converter")));
        Members.Add(BuiltinMethodNames.Filter, new MinamoExternalFunction(BuiltinMethodNames.Filter, false, Filter, new Par("predicate")));
        Members.Add(BuiltinMethodNames.Take, new MinamoExternalFunction(BuiltinMethodNames.Take, false, Take, new Par("count")));
        Members.Add(BuiltinMethodNames.Skip, new MinamoExternalFunction(BuiltinMethodNames.Skip, false, Skip, new Par("count")));
        Members.Add(BuiltinMethodNames.Reduce, new MinamoExternalFunction(BuiltinMethodNames.Reduce, false, Reduce, new Par("converter"), new Par("initial", 0)));
        Members.Add(BuiltinMethodNames.Any, new MinamoExternalFunction(BuiltinMethodNames.Any, false, Any, new Par("predicate")));
        Members.Add(BuiltinMethodNames.All, new MinamoExternalFunction(BuiltinMethodNames.All, false, All, new Par("predicate")));
        Members.Add(BuiltinMethodNames.ToArray, new MinamoExternalFunction(BuiltinMethodNames.ToArray, false, ToArray));
        Members.Add(BuiltinMethodNames.ToSet, new MinamoExternalFunction(BuiltinMethodNames.ToSet, false, ToSet));
        SetSupportedOperations(Ops.Iter);
    }

    private static MinamoObject Iterate(ExecutionContext _, MinamoObject self) => self switch
    {
        MinamoIterator iterator => iterator,
        IEnumerable<MinamoObject> sequence => MinamoIterator.Create(sequence),
        MinamoClass instance => MinamoIterator.Create(instance.Fields),
        _ => Nil
    };

    private static MinamoObject Map(ExecutionContext ctx, MinamoObject? self, MinamoObject[] args)
    {
        var converter = args[0].ToFunction(ctx);
        return converter is null
            ? Nil
            : MinamoIterator.Create(new MapEnumerable(ctx, Source(ctx, self), converter));
    }

    private static MinamoObject Filter(ExecutionContext ctx, MinamoObject? self, MinamoObject[] args)
    {
        var predicate = args[0].ToFunction(ctx);
        return predicate is null
            ? Nil
            : MinamoIterator.Create(new FilterEnumerable(ctx, Source(ctx, self), predicate));
    }

    private static MinamoObject Take(ExecutionContext ctx, MinamoObject? self, MinamoObject[] args)
    {
        if (args[0] is not MinamoInteger count || !count.TryGetInt32(out var value))
        {
            return ctx.InvalidType(args[0]);
        }

        return MinamoIterator.Create(Source(ctx, self).Take(value < 0 ? 0 : value));
    }

    private static MinamoObject Skip(ExecutionContext ctx, MinamoObject? self, MinamoObject[] args)
    {
        if (args[0] is not MinamoInteger count || !count.TryGetInt32(out var value))
        {
            return ctx.InvalidType(args[0]);
        }

        return MinamoIterator.Create(Source(ctx, self).Skip(value < 0 ? 0 : value));
    }

    private static MinamoObject Reduce(ExecutionContext ctx, MinamoObject? self, MinamoObject[] args)
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

    private static MinamoObject Any(ExecutionContext ctx, MinamoObject? self, MinamoObject[] args)
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

    private static MinamoObject All(ExecutionContext ctx, MinamoObject? self, MinamoObject[] args)
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

    private static MinamoObject ToArray(ExecutionContext ctx, MinamoObject? self, MinamoObject[] _) =>
        new MinamoArray(Source(ctx, self).ToArray());

    private static MinamoObject ToSet(ExecutionContext ctx, MinamoObject? self, MinamoObject[] _)
    {
        return new MinamoSet(Source(ctx, self));
    }

    private static IEnumerable<MinamoObject> Source(ExecutionContext ctx, MinamoObject? self) =>
        MinamoIterator.ToEnumerable(ctx, self!);
}
