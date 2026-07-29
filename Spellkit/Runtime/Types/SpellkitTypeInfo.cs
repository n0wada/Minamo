using Spellkit.Compiler;
using Spellkit.Debug;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Spellkit.Runtime.Types;

public abstract partial class SpellkitTypeInfo : SpellkitObject
{
    private Ops ops;

    internal bool Closed { get; set; }

    public override string TypeName => nameof(SpellkitTypeCodes.TypeInfo);

    protected void SetSupportedOperations(Ops ops) => this.ops |= ops;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Support(Ops op) => (ops & op) == op;

    public override object ToObject() => this;

    public override string ToString() => $"TypeInfo<{ReflectedTypeName}>";

    public sealed override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => HashCode.Combine(TypeId, ReflectedTypeId);

    public abstract string ReflectedTypeName { get; }

    public abstract int ReflectedTypeId { get; }

    protected SpellkitTypeInfo() : base(SpellkitTypeCodes.TypeInfo) => mixins.Add(SpellkitTypeCodes.Object);

    #region Binary Operations
    //x + y
    private SpellkitFunction? add;
    protected virtual SpellkitObject AddOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId == SpellkitTypeCodes.String && left.TypeId != SpellkitTypeCodes.String)
        {
            try
            {
                return left.Concat(right, ctx);
            }
            catch (SpellkitCodeException ex)
            {
                ctx.Error = ex.Error;
                return Nil;
            }
        }

        return ctx.OperationNotSupported(Builtins.Add, left, right);
    }
    public SpellkitObject Add(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (add is not null)
        {
            return add.PrepareFunction(ctx, left, right);
        }

        return AddOp(ctx, left, right);
    }

    //x - y
    private SpellkitFunction? sub;
    protected virtual SpellkitObject SubOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ctx.OperationNotSupported(Builtins.Sub, left, right);
    public SpellkitObject Sub(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (sub is not null)
        {
            return sub.PrepareFunction(ctx, left, right);
        }

        return SubOp(ctx, left, right);
    }

    //x * y
    private SpellkitFunction? mul;
    protected virtual SpellkitObject MulOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ctx.OperationNotSupported(Builtins.Mul, left, right);
    public SpellkitObject Mul(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (mul is not null)
        {
            return mul.PrepareFunction(ctx, left, right);
        }

        return MulOp(ctx, left, right);
    }

    //x / y
    private SpellkitFunction? div;
    protected virtual SpellkitObject DivOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ctx.OperationNotSupported(Builtins.Div, left, right);
    public SpellkitObject Div(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (div is not null)
        {
            return div.PrepareFunction(ctx, left, right);
        }

        return DivOp(ctx, left, right);
    }

    //x % y
    private SpellkitFunction? rem;
    protected virtual SpellkitObject RemOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ctx.OperationNotSupported(Builtins.Rem, left, right);
    public SpellkitObject Rem(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (rem is not null)
        {
            return rem.PrepareFunction(ctx, left, right);
        }

        return RemOp(ctx, left, right);
    }

    //x == y
    private SpellkitFunction? eq;
    protected virtual SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ReferenceEquals(left, right) ? True : False;
    public SpellkitObject Eq(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (eq is not null)
        {
            return eq.PrepareFunction(ctx, left, right);
        }

        if (right.TypeId == SpellkitTypeCodes.Bool)
        {
            return ReferenceEquals(left, right) ? True : False;
        }

        return EqOp(ctx, left, right);
    }

    //x != y
    private SpellkitFunction? neq;
    protected virtual SpellkitObject NeqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        EqOp(ctx, left, right).IsFalse() ? True : False;
    public SpellkitObject Neq(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (neq is not null)
        {
            return neq.PrepareFunction(ctx, left, right);
        }

        return NeqOp(ctx, left, right);
    }

    //x > y
    private SpellkitFunction? gt;
    protected virtual SpellkitObject GtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ctx.OperationNotSupported(Builtins.Gt, left, right);
    public SpellkitObject Gt(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (gt is not null)
        {
            return gt.PrepareFunction(ctx, left, right);
        }

        return GtOp(ctx, left, right);
    }

    //x < y
    private SpellkitFunction? lt;
    protected virtual SpellkitObject LtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        ctx.OperationNotSupported(Builtins.Lt, left, right);
    public SpellkitObject Lt(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (lt is not null)
        {
            return lt.PrepareFunction(ctx, left, right);
        }

        return LtOp(ctx, left, right);
    }

    //x >= y
    private SpellkitFunction? gte;
    protected virtual SpellkitObject GteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        left.Greater(right, ctx) || left.Equals(right, ctx) ? True : False;
    public SpellkitObject Gte(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (gte is not null)
        {
            return gte.PrepareFunction(ctx, left, right);
        }

        return GteOp(ctx, left, right);
    }

    //x <= y
    private SpellkitFunction? lte;
    protected virtual SpellkitObject LteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        left.Lesser(right, ctx) || left.Equals(right, ctx) ? True : False;
    public SpellkitObject Lte(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
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
    private SpellkitFunction? neg;
    protected virtual SpellkitObject NegOp(ExecutionContext ctx, SpellkitObject arg) =>
        ctx.OperationNotSupported(Builtins.Neg, arg);
    public SpellkitObject Neg(ExecutionContext ctx, SpellkitObject arg)
    {
        if (neg is not null)
        {
            return neg.PrepareFunction(ctx, arg);
        }

        return NegOp(ctx, arg);
    }

    //+x
    private SpellkitFunction? plus;
    protected virtual SpellkitObject PlusOp(ExecutionContext ctx, SpellkitObject arg) =>
        ctx.OperationNotSupported(Builtins.Plus, arg);
    public SpellkitObject Plus(ExecutionContext ctx, SpellkitObject arg)
    {
        if (plus is not null)
        {
            return plus.PrepareFunction(ctx, arg);
        }

        return PlusOp(ctx, arg);
    }

    //!x
    private SpellkitFunction? not;
    protected virtual SpellkitObject NotOp(ExecutionContext ctx, SpellkitObject arg) =>
        arg.IsFalse() ? True : False;
    public SpellkitObject Not(ExecutionContext ctx, SpellkitObject arg)
    {
        if (not is not null)
        {
            return not.PrepareFunction(ctx, arg);
        }

        return NotOp(ctx, arg);
    }

    //x.Length
    private SpellkitFunction? len;
    protected virtual SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        ctx.OperationNotSupported(Builtins.Length, arg);
    public SpellkitObject Length(ExecutionContext ctx, SpellkitObject arg)
    {
        if (len is not null)
        {
            return len.PrepareFunction(ctx, arg);
        }

        return LengthOp(ctx, arg);
    }

    //x.ToString
    private SpellkitFunction? tos;
    protected virtual SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) => new SpellkitString(arg.ToString());
    public SpellkitObject ToString(ExecutionContext ctx, SpellkitObject arg)
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
        catch (SpellkitCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }
    internal SpellkitObject ToStringWithFormat(ExecutionContext ctx, SpellkitObject arg, SpellkitString format)
    {
        if (tos is not null)
        {
            return tos.PrepareFunction(ctx, arg);
        }

        try
        {
            return ToStringOp(ctx, arg, format);
        }
        catch (SpellkitCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    //x.Clone
    private SpellkitFunction? clone;
    protected virtual SpellkitObject CloneOp(ExecutionContext ctx, SpellkitObject self) => self.Clone();
    private SpellkitObject Clone(ExecutionContext ctx, SpellkitObject self)
    {
        if (clone is not null)
        {
            return clone.PrepareFunction(ctx, self);
        }

        return CloneOp(ctx, self);
    }

    //x.Iterate
    private SpellkitFunction? iter;
    protected virtual SpellkitObject IterateOp(ExecutionContext ctx, SpellkitObject self) =>
        ctx.OperationNotSupported(Builtins.Iterate, self);
    private SpellkitObject GetIterator(ExecutionContext ctx, SpellkitObject self)
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
    private SpellkitFunction? get;
    protected virtual SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index) =>
        ctx.OperationNotSupported(Builtins.Get, self);
    internal SpellkitObject RawGet(ExecutionContext ctx, SpellkitObject self, SpellkitObject index) => GetOp(ctx, self, index);
    public SpellkitObject Get(ExecutionContext ctx, SpellkitObject self, SpellkitObject index)
    {
        if (index.TypeId is SpellkitTypeCodes.String or SpellkitTypeCodes.Char && TryGetInstanceMember(ctx, self, index.ToString(), out var value))
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
    private SpellkitFunction? set;
    protected virtual SpellkitObject SetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index, SpellkitObject value) =>
        ctx.OperationNotSupported(Builtins.Set, self);
    internal SpellkitObject RawSet(ExecutionContext ctx, SpellkitObject self, SpellkitObject index, SpellkitObject value) => SetOp(ctx, self, index, value);
    public SpellkitObject Set(ExecutionContext ctx, SpellkitObject self, SpellkitObject index, SpellkitObject value)
    {
        if (index.TypeId is SpellkitTypeCodes.String or SpellkitTypeCodes.Char
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
    private SpellkitFunction? @in;
    protected virtual SpellkitObject InOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject field) =>
        ctx.OperationNotSupported(Builtins.In, self);
    public SpellkitObject In(ExecutionContext ctx, SpellkitObject self, SpellkitObject field)
    {
        if (@in is not null)
        {
            return @in.PrepareFunction(ctx, self, field);
        }

        return InOp(ctx, self, field);
    }

    //as
    private readonly Dictionary<int, SpellkitFunction> conversions = new();
    protected virtual SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            _ when targetType.ReflectedTypeId == self.TypeId => self,
            SpellkitTypeCodes.Bool => self.IsFalse() ? False : True,
            SpellkitTypeCodes.String => self.ToString(ctx),
            SpellkitTypeCodes.Char => new SpellkitChar(self.ToString(ctx).Value[0]),
            _ => ctx.InvalidCast(self.TypeName, targetType.ReflectedTypeName)
        };
    public SpellkitObject Cast(ExecutionContext ctx, SpellkitObject self, SpellkitObject targetType)
    {
        if (targetType.TypeId != SpellkitTypeCodes.TypeInfo)
        {
            ctx.Error = ErrorGenerators.RuntimeException(SpellkitError.InvalidType, SpellkitTypeCodes.TypeInfo, targetType);
            return Nil;
        }

        var ti = (SpellkitTypeInfo)targetType;

        if (ti.ReflectedTypeId == self.TypeId)
        {
            return self;
        }

        if (conversions.TryGetValue(ti.ReflectedTypeId, out var func))
        {
            return func.BindToInstance(ctx, self).Call(ctx);
        }

        return CastOp(ctx, self, (SpellkitTypeInfo)targetType);
    }
    public void SetCastFunction(SpellkitTypeInfo type, SpellkitFunction func)
    {
        conversions.Remove(type.ReflectedTypeId);
        conversions.Add(type.ReflectedTypeId, func);
    }
    #endregion
}
