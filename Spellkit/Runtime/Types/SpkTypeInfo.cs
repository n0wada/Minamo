using Spellkit.Compiler;
using Spellkit.Debug;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Spellkit.Runtime.Types;

public abstract partial class SpkTypeInfo : SpkObject
{
    private Ops ops;

    internal bool Closed { get; set; }

    public override string TypeName => nameof(Spk.TypeInfo);

    protected void SetSupportedOperations(Ops ops) => this.ops |= ops;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Support(Ops op) => (ops & op) == op;

    public override object ToObject() => this;

    public override string ToString() => $"TypeInfo<{ReflectedTypeName}>";

    public sealed override bool Equals(SpkObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => HashCode.Combine(TypeId, ReflectedTypeId);

    public abstract string ReflectedTypeName { get; }

    public abstract int ReflectedTypeId { get; }

    protected SpkTypeInfo() : base(Spk.TypeInfo) => mixins.Add(Spk.Object);

    #region Binary Operations
    //x + y
    private SpkFunction? add;
    protected virtual SpkObject AddOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId == Spk.String && left.TypeId != Spk.String)
        {
            try
            {
                return left.Concat(right, ctx);
            }
            catch (SpkCodeException ex)
            {
                ctx.Error = ex.Error;
                return Nil;
            }
        }

        return ctx.OperationNotSupported(Builtins.Add, left, right);
    }
    public SpkObject Add(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (add is not null)
        {
            return add.PrepareFunction(ctx, left, right);
        }

        return AddOp(ctx, left, right);
    }

    //x - y
    private SpkFunction? sub;
    protected virtual SpkObject SubOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ctx.OperationNotSupported(Builtins.Sub, left, right);
    public SpkObject Sub(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (sub is not null)
        {
            return sub.PrepareFunction(ctx, left, right);
        }

        return SubOp(ctx, left, right);
    }

    //x * y
    private SpkFunction? mul;
    protected virtual SpkObject MulOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ctx.OperationNotSupported(Builtins.Mul, left, right);
    public SpkObject Mul(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (mul is not null)
        {
            return mul.PrepareFunction(ctx, left, right);
        }

        return MulOp(ctx, left, right);
    }

    //x / y
    private SpkFunction? div;
    protected virtual SpkObject DivOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ctx.OperationNotSupported(Builtins.Div, left, right);
    public SpkObject Div(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (div is not null)
        {
            return div.PrepareFunction(ctx, left, right);
        }

        return DivOp(ctx, left, right);
    }

    //x % y
    private SpkFunction? rem;
    protected virtual SpkObject RemOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ctx.OperationNotSupported(Builtins.Rem, left, right);
    public SpkObject Rem(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (rem is not null)
        {
            return rem.PrepareFunction(ctx, left, right);
        }

        return RemOp(ctx, left, right);
    }

    //x == y
    private SpkFunction? eq;
    protected virtual SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ReferenceEquals(left, right) ? True : False;
    public SpkObject Eq(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (eq is not null)
        {
            return eq.PrepareFunction(ctx, left, right);
        }

        if (right.TypeId == Spk.Bool)
        {
            return ReferenceEquals(left, right) ? True : False;
        }

        return EqOp(ctx, left, right);
    }

    //x != y
    private SpkFunction? neq;
    protected virtual SpkObject NeqOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        EqOp(ctx, left, right).IsFalse() ? True : False;
    public SpkObject Neq(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (neq is not null)
        {
            return neq.PrepareFunction(ctx, left, right);
        }

        return NeqOp(ctx, left, right);
    }

    //x > y
    private SpkFunction? gt;
    protected virtual SpkObject GtOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ctx.OperationNotSupported(Builtins.Gt, left, right);
    public SpkObject Gt(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (gt is not null)
        {
            return gt.PrepareFunction(ctx, left, right);
        }

        return GtOp(ctx, left, right);
    }

    //x < y
    private SpkFunction? lt;
    protected virtual SpkObject LtOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        ctx.OperationNotSupported(Builtins.Lt, left, right);
    public SpkObject Lt(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (lt is not null)
        {
            return lt.PrepareFunction(ctx, left, right);
        }

        return LtOp(ctx, left, right);
    }

    //x >= y
    private SpkFunction? gte;
    protected virtual SpkObject GteOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        left.Greater(right, ctx) || left.Equals(right, ctx) ? True : False;
    public SpkObject Gte(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (gte is not null)
        {
            return gte.PrepareFunction(ctx, left, right);
        }

        return GteOp(ctx, left, right);
    }

    //x <= y
    private SpkFunction? lte;
    protected virtual SpkObject LteOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        left.Lesser(right, ctx) || left.Equals(right, ctx) ? True : False;
    public SpkObject Lte(ExecutionContext ctx, SpkObject left, SpkObject right)
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
    private SpkFunction? neg;
    protected virtual SpkObject NegOp(ExecutionContext ctx, SpkObject arg) =>
        ctx.OperationNotSupported(Builtins.Neg, arg);
    public SpkObject Neg(ExecutionContext ctx, SpkObject arg)
    {
        if (neg is not null)
        {
            return neg.PrepareFunction(ctx, arg);
        }

        return NegOp(ctx, arg);
    }

    //+x
    private SpkFunction? plus;
    protected virtual SpkObject PlusOp(ExecutionContext ctx, SpkObject arg) =>
        ctx.OperationNotSupported(Builtins.Plus, arg);
    public SpkObject Plus(ExecutionContext ctx, SpkObject arg)
    {
        if (plus is not null)
        {
            return plus.PrepareFunction(ctx, arg);
        }

        return PlusOp(ctx, arg);
    }

    //!x
    private SpkFunction? not;
    protected virtual SpkObject NotOp(ExecutionContext ctx, SpkObject arg) =>
        arg.IsFalse() ? True : False;
    public SpkObject Not(ExecutionContext ctx, SpkObject arg)
    {
        if (not is not null)
        {
            return not.PrepareFunction(ctx, arg);
        }

        return NotOp(ctx, arg);
    }

    //x.Length
    private SpkFunction? len;
    protected virtual SpkObject LengthOp(ExecutionContext ctx, SpkObject arg) =>
        ctx.OperationNotSupported(Builtins.Length, arg);
    public SpkObject Length(ExecutionContext ctx, SpkObject arg)
    {
        if (len is not null)
        {
            return len.PrepareFunction(ctx, arg);
        }

        return LengthOp(ctx, arg);
    }

    //x.ToString
    private SpkFunction? tos;
    protected virtual SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format) => new SpkString(arg.ToString());
    public SpkObject ToString(ExecutionContext ctx, SpkObject arg)
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
        catch (SpkCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }
    internal SpkObject ToStringWithFormat(ExecutionContext ctx, SpkObject arg, SpkString format)
    {
        if (tos is not null)
        {
            return tos.PrepareFunction(ctx, arg);
        }

        try
        {
            return ToStringOp(ctx, arg, format);
        }
        catch (SpkCodeException ex)
        {
            ctx.Error = ex.Error;
            return Nil;
        }
    }

    //x.Clone
    private SpkFunction? clone;
    protected virtual SpkObject CloneOp(ExecutionContext ctx, SpkObject self) => self.Clone();
    private SpkObject Clone(ExecutionContext ctx, SpkObject self)
    {
        if (clone is not null)
        {
            return clone.PrepareFunction(ctx, self);
        }

        return CloneOp(ctx, self);
    }

    //x.Iterate
    private SpkFunction? iter;
    protected virtual SpkObject IterateOp(ExecutionContext ctx, SpkObject self) =>
        ctx.OperationNotSupported(Builtins.Iterate, self);
    private SpkObject GetIterator(ExecutionContext ctx, SpkObject self)
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
    private SpkFunction? get;
    protected virtual SpkObject GetOp(ExecutionContext ctx, SpkObject self, SpkObject index) =>
        ctx.OperationNotSupported(Builtins.Get, self);
    internal SpkObject RawGet(ExecutionContext ctx, SpkObject self, SpkObject index) => GetOp(ctx, self, index);
    public SpkObject Get(ExecutionContext ctx, SpkObject self, SpkObject index)
    {
        if (index.TypeId is Spk.String or Spk.Char && TryGetInstanceMember(ctx, self, index.ToString(), out var value))
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
    private SpkFunction? set;
    protected virtual SpkObject SetOp(ExecutionContext ctx, SpkObject self, SpkObject index, SpkObject value) =>
        ctx.OperationNotSupported(Builtins.Set, self);
    internal SpkObject RawSet(ExecutionContext ctx, SpkObject self, SpkObject index, SpkObject value) => SetOp(ctx, self, index, value);
    public SpkObject Set(ExecutionContext ctx, SpkObject self, SpkObject index, SpkObject value)
    {
        if (index.TypeId is Spk.String or Spk.Char
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
    private SpkFunction? @in;
    protected virtual SpkObject InOp(ExecutionContext ctx, SpkObject self, SpkObject field) =>
        ctx.OperationNotSupported(Builtins.In, self);
    public SpkObject In(ExecutionContext ctx, SpkObject self, SpkObject field)
    {
        if (@in is not null)
        {
            return @in.PrepareFunction(ctx, self, field);
        }

        return InOp(ctx, self, field);
    }

    //as
    private readonly Dictionary<int, SpkFunction> conversions = new();
    protected virtual SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType) =>
        targetType.ReflectedTypeId switch
        {
            _ when targetType.ReflectedTypeId == self.TypeId => self,
            Spk.Bool => self.IsFalse() ? False : True,
            Spk.String => self.ToString(ctx),
            Spk.Char => new SpkChar(self.ToString(ctx).Value[0]),
            _ => ctx.InvalidCast(self.TypeName, targetType.ReflectedTypeName)
        };
    public SpkObject Cast(ExecutionContext ctx, SpkObject self, SpkObject targetType)
    {
        if (targetType.TypeId != Spk.TypeInfo)
        {
            ctx.Error = ErrorGenerators.RuntimeException(SpkError.InvalidType, Spk.TypeInfo, targetType);
            return Nil;
        }

        var ti = (SpkTypeInfo)targetType;

        if (ti.ReflectedTypeId == self.TypeId)
        {
            return self;
        }

        if (conversions.TryGetValue(ti.ReflectedTypeId, out var func))
        {
            return func.BindToInstance(ctx, self).Call(ctx);
        }

        return CastOp(ctx, self, (SpkTypeInfo)targetType);
    }
    public void SetCastFunction(SpkTypeInfo type, SpkFunction func)
    {
        conversions.Remove(type.ReflectedTypeId);
        conversions.Add(type.ReflectedTypeId, func);
    }
    #endregion
}
