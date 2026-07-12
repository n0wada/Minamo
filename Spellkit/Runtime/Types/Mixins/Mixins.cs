using Spellkit.Compiler;
using System.Collections.Generic;

namespace Spellkit.Runtime.Types;

internal sealed class SpkCollectionMixin : SpkMixin<SpkCollectionMixin>
{
    public SpkCollectionMixin() : base(Spk.Collection)
    {
        AddMixins(Spk.Lookup);
        Members.Add(Builtins.Length, Unary(Builtins.Length, SpkLookupMixin.GetLength));
        Members.Add(Builtins.Get, Binary(Builtins.Get, SpkLookupMixin.Getter, "index"));
        Members.Add(Builtins.Set, Ternary(Builtins.Set, Setter, "index", "value"));
        SetSupportedOperations(Ops.Set);
    }

    private static SpkObject Setter(ExecutionContext ctx, SpkObject self, SpkObject index, SpkObject value)
    {
        ((SpkClass)self).Fields.SetItem(ctx, index, value);
        return Nil;
    }
}
internal sealed class SpkContainerMixin : SpkMixin<SpkContainerMixin>
{
    public SpkContainerMixin() : base(Spk.Container)
    {
        Members.Add(Builtins.In, Binary(Builtins.In, IsIn, "value"));
        SetSupportedOperations(Ops.In);
    }

    private static SpkObject IsIn(ExecutionContext _, SpkObject self, SpkObject field)
    {
        if (field.TypeId is not Spk.String and not Spk.Char)
        {
            return False;
        }

        return ((SpkClass)self).Fields.GetOrdinal(field.ToString()) is not -1 ? True : False;
    }
}

internal sealed class SpkDisposableMixin : SpkMixin<SpkDisposableMixin>
{
    public SpkDisposableMixin() : base(Spk.Disposable)
    {
        Members.Add(Builtins.Dispose, Unary(Builtins.Dispose, Dispose));
    }

    private static SpkObject Dispose(ExecutionContext ctx, SpkObject self) =>
        ctx.NotImplemented(Builtins.Dispose);
}

internal sealed class SpkEquatableMixin : SpkMixin<SpkEquatableMixin>
{
    public SpkEquatableMixin() : base(Spk.Equatable)
    {
        Members.Add(Builtins.Eq, Binary(Builtins.Eq, Equatable));
    }

    private static SpkObject Equatable(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        var self = (SpkClass)left;

        if (self.TypeId == right.TypeId && right is SpkClass t && t.Constructor == self.Constructor)
        {
            try
            {
                return SpkTuple.Equals(ctx, self.Fields, t.Fields);
            }
            catch (SpkCodeException ex)
            {
                ctx.Error = ex.Error;
                return Nil;
            }
        }

        return False;
    }
}

internal sealed class SpkFunctorMixin : SpkMixin<SpkFunctorMixin>
{
    public SpkFunctorMixin() : base(Spk.Functor) =>
        Members.Add(Builtins.Call, Unary(Builtins.Call, SelfCall));

    private static SpkObject SelfCall(ExecutionContext ctx, SpkObject self) =>
        ctx.NotImplemented(Builtins.Call);
}

internal sealed class SpkIdentityMixin : SpkMixin<SpkIdentityMixin>
{
    public SpkIdentityMixin() : base(Spk.Identity) =>
        Members.Add(Builtins.Clone, Unary(Builtins.Clone, GetIdentity));

    private static SpkObject GetIdentity(ExecutionContext _, SpkObject arg) => arg;
}

internal sealed class SpkLookupMixin : SpkMixin<SpkLookupMixin>
{
    public SpkLookupMixin() : base(Spk.Lookup)
    {
        Members.Add(Builtins.Length, Unary(Builtins.Length, GetLength));
        Members.Add(Builtins.Get, Binary(Builtins.Get, Getter, "index"));
        SetSupportedOperations(Ops.Get | Ops.Len);
    }

    public static SpkObject GetLength(ExecutionContext ctx, SpkObject self) =>
        SpkInteger.Get(((SpkClass)self).Fields.Count);

    public static SpkObject Getter(ExecutionContext ctx, SpkObject self, SpkObject index) =>
        ((SpkClass)self).Fields.GetItem(ctx, index);
}

public abstract class SpkMixin<T> : SpkTypeInfo
    where T : SpkMixin<T>, new()
{
    public static T Instance { get; } = new T();

    public override string ReflectedTypeName { get; }

    public override int ReflectedTypeId { get; }

    protected SpkMixin(int typeId) =>
        (ReflectedTypeId, ReflectedTypeName, Closed) = (typeId, Spk.GetTypeNameByCode(typeId), true);
}

internal sealed class SpkNumberMixin : SpkMixin<SpkNumberMixin>
{
    public SpkNumberMixin() : base(Spk.Number)
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

    private static SpkObject Sum(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ctx.NotImplemented(Builtins.Add);

    private static SpkObject Subtract(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ctx.NotImplemented(Builtins.Sub);

    private static SpkObject Multiply(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ctx.NotImplemented(Builtins.Mul);

    private static SpkObject Divide(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ctx.NotImplemented(Builtins.Div);

    private static SpkObject Remainder(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ctx.NotImplemented(Builtins.Rem);

    private static SpkObject Negate(ExecutionContext ctx, SpkObject left) =>
        ctx.NotImplemented(Builtins.Neg);

    private static SpkObject MakePlus(ExecutionContext ctx, SpkObject left) =>
        ctx.NotImplemented(Builtins.Plus);
}

internal sealed class SpkObjectMixin : SpkMixin<SpkObjectMixin>
{
    public SpkObjectMixin() : base(Spk.Object) { }
}

internal sealed class SpkOrderMixin : SpkMixin<SpkOrderMixin>
{
    public SpkOrderMixin() : base(Spk.Order)
    {
        Members.Add(Builtins.Gt, Binary(Builtins.Gt, Greater));
        Members.Add(Builtins.Lt, Binary(Builtins.Lt, Lesser));
        SetSupportedOperations(Ops.Gt | Ops.Lt | Ops.Gte | Ops.Lte);
    }

    private static SpkObject Greater(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        try
        {
            return SpkTuple.Greater(ctx, ((SpkClass)left).Fields, ((SpkClass)right).Fields);
        }
        catch(SpkCodeException e)
        {
            ctx.Error = e.Error;
            return Nil;
        }
    }

    private static SpkObject Lesser(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        try
        {
            return SpkTuple.Lesser(ctx, ((SpkClass)left).Fields, ((SpkClass)right).Fields);
        }
        catch (SpkCodeException e)
        {
            ctx.Error = e.Error;
            return Nil;
        }
    }
}

internal sealed class SpkSequenceMixin : SpkMixin<SpkSequenceMixin>
{
    public SpkSequenceMixin() : base(Spk.Sequence)
    {
        Members.Add(Builtins.Iterate, Unary(Builtins.Iterate, Iterate));
        SetSupportedOperations(Ops.Iter);
    }

    private static SpkObject Iterate(ExecutionContext ctx, SpkObject self) =>
        SpkIterator.Create(((SpkClass)self).Fields);
}
