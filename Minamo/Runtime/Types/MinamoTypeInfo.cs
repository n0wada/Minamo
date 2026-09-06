using Minamo.Compiler;
using Minamo.Debug;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Minamo.Runtime.Types;

public abstract partial class MinamoTypeInfo : MinamoObject
{
    private Ops ops;

    internal bool Closed { get; set; }

    public override string TypeName => nameof(MinamoTypeCodes.TypeInfo);

    protected void SetSupportedOperations(Ops ops) => this.ops |= ops;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Support(Ops op) => (ops & op) == op;

    public override object ToObject() => this;

    public override string ToString() => $"TypeInfo<{ReflectedTypeName}>";

    public sealed override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => HashCode.Combine(TypeId, ReflectedTypeId);

    public abstract string ReflectedTypeName { get; }

    public abstract int ReflectedTypeId { get; }

    protected MinamoTypeInfo() : base(MinamoTypeCodes.TypeInfo) => mixins.Add(MinamoTypeCodes.Object);

    #region Binary Operations
    //x + y
    private MinamoFunction? add;
    protected virtual MinamoObject AddOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId == MinamoTypeCodes.String && left.TypeId != MinamoTypeCodes.String)
        {
            try
            {
                return left.Concat(right, ctx);
            }
            catch (MinamoCodeException ex)
            {
                ctx.Error = ex.Error;
                return Nil;
            }
        }

        return ctx.OperationNotSupported(Builtins.Add, left, right);
    }
    public MinamoObject Add(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (add is not null)
        {
            return add.PrepareFunction(ctx, left, right);
        }

        return AddOp(ctx, left, right);
    }

    //x - y
    private MinamoFunction? sub;
    protected virtual MinamoObject SubOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        ctx.OperationNotSupported(Builtins.Sub, left, right);
    public MinamoObject Sub(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (sub is not null)
        {
            return sub.PrepareFunction(ctx, left, right);
        }

        return SubOp(ctx, left, right);
    }

    //x * y
    private MinamoFunction? mul;
    protected virtual MinamoObject MulOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        ctx.OperationNotSupported(Builtins.Mul, left, right);
    public MinamoObject Mul(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (mul is not null)
        {
            return mul.PrepareFunction(ctx, left, right);
        }

        return MulOp(ctx, left, right);
    }

    //x / y
    private MinamoFunction? div;
    protected virtual MinamoObject DivOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        ctx.OperationNotSupported(Builtins.Div, left, right);
    public MinamoObject Div(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (div is not null)
        {
            return div.PrepareFunction(ctx, left, right);
        }

        return DivOp(ctx, left, right);
    }

    //x % y
    private MinamoFunction? rem;
    protected virtual MinamoObject RemOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        ctx.OperationNotSupported(Builtins.Rem, left, right);
    public MinamoObject Rem(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (rem is not null)
        {
            return rem.PrepareFunction(ctx, left, right);
        }

        return RemOp(ctx, left, right);
    }

    //x == y
    private MinamoFunction? eq;
    protected virtual MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        ReferenceEquals(left, right) ? True : False;
    public MinamoObject Eq(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (eq is not null)
        {
            return eq.PrepareFunction(ctx, left, right);
        }

        if (right.TypeId == MinamoTypeCodes.Bool)
        {
            return ReferenceEquals(left, right) ? True : False;
        }

        return EqOp(ctx, left, right);
    }

    //x != y
    private MinamoFunction? neq;
    protected virtual MinamoObject NeqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        EqOp(ctx, left, right).IsFalse() ? True : False;
    public MinamoObject Neq(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (neq is not null)
        {
            return neq.PrepareFunction(ctx, left, right);
        }

        return NeqOp(ctx, left, right);
    }

    //x > y
    private MinamoFunction? gt;
    protected virtual MinamoObject GtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        ctx.OperationNotSupported(Builtins.Gt, left, right);
    public MinamoObject Gt(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (gt is not null)
        {
            return gt.PrepareFunction(ctx, left, right);
        }

        return GtOp(ctx, left, right);
    }

    //x < y
    private MinamoFunction? lt;
    protected virtual MinamoObject LtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        ctx.OperationNotSupported(Builtins.Lt, left, right);
    public MinamoObject Lt(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (lt is not null)
        {
            return lt.PrepareFunction(ctx, left, right);
        }

        return LtOp(ctx, left, right);
    }

    //x >= y
    private MinamoFunction? gte;
    protected virtual MinamoObject GteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        left.Greater(right, ctx) || left.Equals(right, ctx) ? True : False;
    public MinamoObject Gte(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (gte is not null)
        {
            return gte.PrepareFunction(ctx, left, right);
        }

        return GteOp(ctx, left, right);
    }

    //x <= y
    private MinamoFunction? lte;
    protected virtual MinamoObject LteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        left.Lesser(right, ctx) || left.Equals(right, ctx) ? True : False;
    public MinamoObject Lte(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (lte is not null)
        {
            return lte.PrepareFunction(ctx, left, right);
        }

        return LteOp(ctx, left, right);
    }
    #endregion

    #region Unary Operations
    //-x
    private MinamoFunction? neg;
    protected virtual MinamoObject NegOp(ExecutionContext ctx, MinamoObject arg) =>
        ctx.OperationNotSupported(Builtins.Neg, arg);
    public MinamoObject Neg(ExecutionContext ctx, MinamoObject arg)
    {
        if (neg is not null)
        {
            return neg.PrepareFunction(ctx, arg);
        }

        return NegOp(ctx, arg);
    }

    //+x
    private MinamoFunction? plus;
    protected virtual MinamoObject PlusOp(ExecutionContext ctx, MinamoObject arg) =>
        ctx.OperationNotSupported(Builtins.Plus, arg);
    public MinamoObject Plus(ExecutionContext ctx, MinamoObject arg)
    {
        if (plus is not null)
        {
            return plus.PrepareFunction(ctx, arg);
        }

        return PlusOp(ctx, arg);
    }

    //!x
    private MinamoFunction? not;
    protected virtual MinamoObject NotOp(ExecutionContext ctx, MinamoObject arg) =>
        arg.IsFalse() ? True : False;
    public MinamoObject Not(ExecutionContext ctx, MinamoObject arg)
    {
        if (not is not null)
        {
            return not.PrepareFunction(ctx, arg);
        }

        return NotOp(ctx, arg);
    }

    //x.Length
    private MinamoFunction? len;
    protected virtual MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        ctx.OperationNotSupported(Builtins.Length, arg);
    public MinamoObject Length(ExecutionContext ctx, MinamoObject arg)
    {
        if (len is not null)
        {
            return len.PrepareFunction(ctx, arg);
        }

        return LengthOp(ctx, arg);
    }

    //x.ToString
    private MinamoFunction? tos;
    protected virtual MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) => new MinamoString(arg.ToString());
    public MinamoObject ToString(ExecutionContext ctx, MinamoObject arg)
    {
        if (tos is not null)
        {
            return tos.PrepareFunction(ctx, arg);
        }

        //Validate logic
        try
        {
            return ToStringOp(ctx, arg, Nil);
        }
        catch (MinamoCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }
    internal MinamoObject ToStringWithFormat(ExecutionContext ctx, MinamoObject arg, MinamoString format)
    {
        if (tos is not null)
        {
            return tos.PrepareFunction(ctx, arg);
        }

        try
        {
            return ToStringOp(ctx, arg, format);
        }
        catch (MinamoCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    //x.Clone
    private MinamoFunction? clone;
    protected virtual MinamoObject CloneOp(ExecutionContext ctx, MinamoObject self) => self.Clone();
    private MinamoObject Clone(ExecutionContext ctx, MinamoObject self)
    {
        if (clone is not null)
        {
            return clone.PrepareFunction(ctx, self);
        }

        return CloneOp(ctx, self);
    }

    //x.Iterate
    private MinamoFunction? iter;
    protected virtual MinamoObject IterateOp(ExecutionContext ctx, MinamoObject self) =>
        ctx.OperationNotSupported(Builtins.Iterate, self);
    private MinamoObject GetIterator(ExecutionContext ctx, MinamoObject self)
    {
        if (iter is not null)
        {
            return iter.PrepareFunction(ctx, self);
        }

        return IterateOp(ctx, self);
    }
    #endregion

    #region Other Operations
    //x[y]
    private MinamoFunction? get;
    protected virtual MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index) =>
        ctx.OperationNotSupported(Builtins.Get, self);
    internal MinamoObject RawGet(ExecutionContext ctx, MinamoObject self, MinamoObject index) => GetOp(ctx, self, index);
    public MinamoObject Get(ExecutionContext ctx, MinamoObject self, MinamoObject index)
    {
        if (index.TypeId is MinamoTypeCodes.String or MinamoTypeCodes.Char && TryGetInstanceMember(ctx, self, index.ToString(), out var value))
        {
            return value!;
        }

        if (get is not null)
        {
            return get.PrepareFunction(ctx, self, index);
        }

        return GetOp(ctx, self, index);
    }

    //x[y] = z
    private MinamoFunction? set;
    protected virtual MinamoObject SetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index, MinamoObject value) =>
        ctx.OperationNotSupported(Builtins.Set, self);
    internal MinamoObject RawSet(ExecutionContext ctx, MinamoObject self, MinamoObject index, MinamoObject value) => SetOp(ctx, self, index, value);
    public MinamoObject Set(ExecutionContext ctx, MinamoObject self, MinamoObject index, MinamoObject value)
    {
        if (index.TypeId is MinamoTypeCodes.String or MinamoTypeCodes.Char
            && TryGetInstanceMember(ctx, self, Builtins.Setter(index.ToString()), out var setter))
        {
            setter!.Invoke(ctx, value);
            return ctx.HasErrors ? Nil : Nil;
        }

        if (set is not null)
        {
            return set.PrepareFunction(ctx, self, index, value);
        }

        return SetOp(ctx, self, index, value);
    }

    //Contains
    private MinamoFunction? @in;
    protected virtual MinamoObject InOp(ExecutionContext ctx, MinamoObject self, MinamoObject field) =>
        ctx.OperationNotSupported(Builtins.In, self);
    public MinamoObject In(ExecutionContext ctx, MinamoObject self, MinamoObject field)
    {
        if (@in is not null)
        {
            return @in.PrepareFunction(ctx, self, field);
        }

        return InOp(ctx, self, field);
    }

    //as
    private readonly Dictionary<int, MinamoFunction> conversions = new();
    protected virtual MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            _ when targetType.ReflectedTypeId == self.TypeId => self,
            MinamoTypeCodes.Bool => self.IsFalse() ? False : True,
            MinamoTypeCodes.String => self.ToString(ctx),
            MinamoTypeCodes.Char => new MinamoChar(self.ToString(ctx).Value[0]),
            _ => ctx.InvalidCast(self.TypeName, targetType.ReflectedTypeName)
        };
    public MinamoObject Cast(ExecutionContext ctx, MinamoObject self, MinamoObject targetType)
    {
        if (targetType.TypeId != MinamoTypeCodes.TypeInfo)
        {
            ctx.Error = ErrorGenerators.RuntimeException(MinamoError.InvalidType, MinamoTypeCodes.TypeInfo, targetType);
            return Nil;
        }

        var ti = (MinamoTypeInfo)targetType;

        if (ti.ReflectedTypeId == self.TypeId)
        {
            return self;
        }

        if (conversions.TryGetValue(ti.ReflectedTypeId, out var func))
        {
            return func.BindToInstance(ctx, self).Call(ctx);
        }

        return CastOp(ctx, self, (MinamoTypeInfo)targetType);
    }
    public void SetCastFunction(MinamoTypeInfo type, MinamoFunction func)
    {
        conversions.Remove(type.ReflectedTypeId);
        conversions.Add(type.ReflectedTypeId, func);
    }
    #endregion
}
