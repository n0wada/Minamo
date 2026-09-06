using Minamo.Compiler;
using Minamo.Debug;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Minamo.Runtime.Types;

public abstract partial class MinamoTypeInfo : MinamoObject
{
    #region Statics
    protected readonly Dictionary<HashString, MinamoFunction> StaticMembers = new();

    internal bool HasStaticMember(HashString name, ExecutionContext ctx) => LookupStaticMember(ctx, name) is not null;

    internal virtual MinamoObject GetStaticMember(HashString name, ExecutionContext ctx)
    {
        var ret = LookupStaticMember(ctx, name);

        if (ret is null)
        {
            if (name != MinamoMissingMethod.Name)
            {
                if (TryGetStaticMember(ctx, MinamoMissingMethod.Name, out var meth))
                {
                    return new MinamoMissingMethod((string)name, (MinamoNativeFunction)meth!);
                }
            }

            return ctx.StaticOperationNotSupported((string)name, ReflectedTypeId);
        }

        if (ret is MinamoFunction f && f.Auto)
        {
            ret = f.TryInvokeProperty(ctx, this);
        }

        return ret;
    }

    internal bool TryGetStaticMember(ExecutionContext ctx, HashString name, out MinamoObject? value)
    {
        var func = LookupStaticMember(ctx, name);

        if (func is not null)
        {
            if (func is MinamoFunction f && f.Auto)
            {
                value = f.TryInvokeProperty(ctx, this);
            }
            else
            {
                value = func;
            }

            return true;
        }

        value = null;
        return false;
    }

    internal MinamoObject? LookupStaticMember(ExecutionContext ctx, HashString name)
    {
        if (!StaticMembers.TryGetValue(name, out var value))
        {
            value = InitializeStaticMembers((string)name, ctx);

            if (value is not null)
            {
                StaticMembers.Add(name, value);
            }
        }

        return value;
    }

    internal virtual void SetStaticMember(ExecutionContext ctx, HashString name, MinamoFunction func)
    {
        if (Builtins.IsSetter(name))
        {
            var set = Builtins.GetSetterName(name);

            if (StaticMembers.TryGetValue(set, out var old) && !old.Auto)
            {
                ctx.InvalidOverload(set);
                return;
            }
        }

        if (StaticMembers.TryGetValue(name, out var oldfun))
        {
            //A non-property cannot be overriden by a property and vice versa
            if (oldfun.Auto != func.Auto)
            {
                ctx.InvalidOverload(name);
                return;
            }

            StaticMembers.Remove(name);
        }

        StaticMembers.Add(name, func);
    }

    private MinamoFunction? InitializeStaticMembers(string name, ExecutionContext ctx) =>
        name switch
        {
            "TypeInfo" => Binary(name, (c, _, obj) => c.RuntimeContext.Types[obj.TypeId], "value"),
            Builtins.Has => Binary(name, Has, "member"),
            Builtins.DelMember => Binary(name,
                (context, _, strObj) =>
                {
                    var nm = strObj.ToString();
                    SetBuiltin(context, nm, null);
                    Members.Remove(nm);
                    StaticMembers.Remove(nm);
                    return Nil;
                }, "name"),
            _ => InitializeStaticMember(name, ctx)
        };

    protected virtual MinamoFunction? InitializeStaticMember(string name, ExecutionContext ctx) => null;
    #endregion

    #region Instance
    protected readonly Dictionary<HashString, MinamoFunction> Members = new();
    
    internal virtual bool HasInstanceMember(MinamoObject self, HashString name, ExecutionContext ctx) =>
        LookupInstanceMember(ctx, self, Builtins.OperatorToName((string)name)) is not null;

    internal virtual MinamoObject GetInstanceMember(MinamoObject self, HashString name, ExecutionContext ctx)
    {
        var value = LookupInstanceMember(ctx, self, name);

        if (value is not null)
        {
            return value.TryInvokeProperty(ctx, self);
        }

        if (name != MinamoMissingMethod.Name)
        {
            if (TryGetInstanceMember(ctx, self, MinamoMissingMethod.Name, out var meth))
            {
                return new MinamoMissingMethod((string)name, (MinamoNativeFunction)meth!);
            }
        }

        return ctx.OperationNotSupported((string)name, self);
    }

    internal bool TryGetInstanceMember(ExecutionContext ctx, MinamoObject self, HashString name, out MinamoObject? value)
    {
        var func = LookupInstanceMember(ctx, self, name);

        if (func is not null)
        {
            value = func.TryInvokeProperty(ctx, self);
            return true;
        }

        value = null;
        return false;
    }

    internal MinamoFunction? LookupInstanceMember(ExecutionContext ctx, MinamoObject self, HashString name)
    {
        if (!Members.TryGetValue(name, out var value))
        {
            value = InitializeInstanceMembers(self, (string)name, ctx);

            if (value is not null)
            {
                Members.Add(name, value);
            }
        }

        return value;
    }

    internal virtual void SetInstanceMember(ExecutionContext ctx, HashString name, MinamoFunction func)
    {
        if (Closed)
        {
            ctx.TypeClosed(this);
            return;
        }

        SetBuiltin(ctx, (string)name, func);

        if (ctx.HasErrors)
        {
            return;
        }

        if (Builtins.IsSetter(name))
        {
            var set = Builtins.GetSetterName(name);

            if (
                //Length cannot be a property if a type supports Len
                (set == Builtins.Length && Support(Ops.Len))
                //ToString can never be a property
                || set == Builtins.String
                //A non-property cannot be overriden by a property
                || (Members.TryGetValue(set, out var get) && !get.Auto)
                )
            {
                ctx.InvalidOverload(set);
                return;
            }
        }

        if (Members.TryGetValue(name, out var oldfun))
        {
            //A non-property cannot be overriden by a property and vice versa
            if (oldfun.Auto != func.Auto)
            {
                ctx.InvalidOverload(name);
                return;
            }

        }

        Members[name] = func;
    }

    private void SetBuiltin(ExecutionContext ctx, string name, MinamoFunction? func)
    {
        switch (name)
        {
            case Builtins.Add:
                ops |= Ops.Add;
                add = func;
                break;
            case Builtins.Sub:
                ops |= Ops.Sub;
                sub = func;
                break;
            case Builtins.Mul:
                ops |= Ops.Mul;
                mul = func;
                break;
            case Builtins.Div:
                ops |= Ops.Div;
                div = func;
                break;
            case Builtins.Rem:
                ops |= Ops.Rem;
                rem = func;
                break;
            case Builtins.Eq:
                eq = func;
                break;
            case Builtins.Neq:
                neq = func;
                break;
            case Builtins.Gt:
                ops |= Ops.Gt;
                gt = func;
                break;
            case Builtins.Lt:
                ops |= Ops.Lt;
                lt = func;
                break;
            case Builtins.Gte:
                ops |= Ops.Gte;
                gte = func;
                break;
            case Builtins.Lte:
                ops |= Ops.Lte;
                lte = func;
                break;
            case Builtins.Neg:
                ops |= Ops.Neg;
                neg = func;
                break;
            case Builtins.Not:
                not = func;
                break;
            case Builtins.Plus:
                ops |= Ops.Plus;
                plus = func;
                break;
            case Builtins.Set:
                ops |= Ops.Set;
                set = func;
                break;
            case Builtins.Get:
                ops |= Ops.Get;
                get = func;
                break;
            case Builtins.Iterate:
                if (func is not null && func.Auto)
                {
                    ctx.InvalidOverload(name);
                    break;
                }
                ops |= Ops.Iter;
                iter = func;
                break;
            case Builtins.In:
                if (func is not null && func.Auto)
                {
                    ctx.InvalidOverload(name);
                    break;
                }
                ops |= Ops.In;
                @in = func;
                break;
            case Builtins.Clone:
                if (func is not null && func.Auto)
                {
                    ctx.InvalidOverload(name);
                    break;
                }
                clone = func;
                break;
            case Builtins.Length:
                if (func is not null && func.Auto)
                {
                    ctx.InvalidOverload(name);
                    break;
                }
                ops |= Ops.Len;
                len = func;
                break;
            case Builtins.String:
                if (func is not null && func.Auto)
                {
                    ctx.InvalidOverload(name);
                    break;
                }
                tos = func;
                break;
        }
    }

    private MinamoObject Has(ExecutionContext ctx, MinamoObject self, MinamoObject member)
    {
        if (member.TypeId is not MinamoTypeCodes.String and not MinamoTypeCodes.Char)
        {
            ctx.Error = ErrorGenerators.RuntimeException(MinamoError.InvalidType, member);
            return Nil;
        }

        var name = member.ToString();

        //We're calling against type itself, it means that we need to check
        // a presence of a static member
        if (self is null)
        {
            return HasStaticMember(name, ctx) ? True : False;
        }

        return HasInstanceMember(self, name, ctx) ? True : False;
    }

    protected static MinamoFunction Ternary(string name, Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject, MinamoObject> fun, Par par1, Par par2) =>
        new MinamoTernaryFunction(name, fun, par1, par2);

    protected static MinamoFunction Binary(string name, Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject> fun, Par par = default) =>
        new MinamoBinaryFunction(name, fun, par.Name is null ? new Par("other") : par);

    protected static MinamoFunction Unary(string name, Func<ExecutionContext, MinamoObject, MinamoObject> fun) =>
        new MinamoUnaryFunction(name, fun);

    private MinamoFunction? InitializeInstanceMembers(MinamoObject self, string name, ExecutionContext ctx) =>
        name switch
        {
            Builtins.Add => Support(Ops.Add) ? (add is null ? Binary(name, AddOp) : add) : null,
            Builtins.Sub => Support(Ops.Sub) ? (sub is null ? Binary(name, SubOp) : sub) : null,
            Builtins.Mul => Support(Ops.Mul) ? (mul is null ? Binary(name, MulOp) : mul) : null,
            Builtins.Div => Support(Ops.Div) ? (div is null ? Binary(name, DivOp) : div) : null,
            Builtins.Rem => Support(Ops.Rem) ? (rem is null ? Binary(name, RemOp) : rem) : null,
            Builtins.Eq => eq is null ? Binary(name, EqOp) : eq,
            Builtins.Neq => neq is null ? Binary(name, NeqOp) : neq,
            Builtins.Gt => Support(Ops.Gt) ? (gt is null ? Binary(name, GtOp) : gt) : null,
            Builtins.Lt => Support(Ops.Lt) ? (lt is null ? Binary(name, LtOp) : lt) : null,
            Builtins.Gte => Support(Ops.Gte) ? (gte is null ? Binary(name, GteOp) : gte) : null,
            Builtins.Lte => Support(Ops.Lte) ? (lte is null ? Binary(name, LteOp) : lte) : null,
            Builtins.Neg => Support(Ops.Neg) ? (neg is null ? Unary(name, NegOp) : neg) : null,
            Builtins.Not => not is null ? Unary(name, NotOp) : not,
            Builtins.Plus => Support(Ops.Plus) ? (plus is null ? Unary(name, PlusOp) : plus) : null,
            Builtins.Get => Support(Ops.Get) ? (get is null ? Binary(name, GetOp, "index") : get) : null,
            Builtins.Set => Support(Ops.Set) ? (set is null ? Ternary(name, SetOp, "index", "value") : set) : null,
            Builtins.Length => Support(Ops.Len) ? (len is null ? Unary(name, LengthOp) : len) : null,
            Builtins.String => tos is null ? Binary(name, ToStringOp, new Par("format", Nil)) : tos,
            Builtins.Iterate => Support(Ops.Iter) ? (iter is null ? Unary(name, GetIterator) : iter) : null,
            Builtins.Clone => clone is null ? Unary(name, Clone) : clone,
            Builtins.Has => Binary(name, Has, "member"),
            Builtins.Type => Unary(name, (ct, o) => ct.RuntimeContext.Types[o.TypeId]),
            Builtins.In => Support(Ops.In) ? (@in is null ? Binary(name, InOp, "value") : @in) : null,
            _ => InitializeInstanceMember(self, name, ctx)
        };

    protected virtual MinamoFunction? InitializeInstanceMember(MinamoObject self, string name, ExecutionContext ctx) => null;
    #endregion

    #region Mixins
    private readonly HashSet<int> mixins = new();
    internal void Mixin(ExecutionContext ctx, MinamoTypeInfo typeInfo)
    {
        if (mixins.Contains(typeInfo.ReflectedTypeId))
        {
            return;
        }

        foreach (var kv in typeInfo.Members)
        {
            SetBuiltin(ctx, (string)kv.Key, kv.Value);
            Members[kv.Key] = kv.Value;
        }

        ops |= typeInfo.ops;
        mixins.Add(typeInfo.ReflectedTypeId);
        typeInfo.Closed = true;
        mixins.UnionWith(typeInfo.mixins);
    }

    protected void AddMixins(params int[] typeInfos)
    {
        for (var i = 0; i < typeInfos.Length; i++)
        {
            AddSingleMixin(typeInfos[i]);
        }
    }

    private void AddSingleMixin(int typeId)
    {
        var ti = MinamoTypeCodes.GetMixinByCode(typeId);
        mixins.Add(ti.ReflectedTypeId);

        if (typeId == MinamoTypeCodes.Sequence)
        {
            foreach (var member in ti.Members)
            {
                Members.TryAdd(member.Key, member.Value);
            }
        }

        foreach (var mj in ti.mixins)
        {
            if (mj != MinamoTypeCodes.Object)
            {
                AddSingleMixin(mj);
            }
        }

        ops |= ti.ops;
    }

    internal IEnumerable<int> GetMixins() => mixins;

    internal bool CheckType(MinamoTypeInfo typeInfo) =>
        ReflectedTypeId == typeInfo.ReflectedTypeId || mixins.Contains(typeInfo.ReflectedTypeId);
    #endregion
}
