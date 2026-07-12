using Spellkit.Compiler;
using Spellkit.Parser;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Spellkit.Runtime.Types;

public abstract class SpkObject : IEquatable<SpkObject>
{
    public virtual int TypeId { get; }

    public abstract string TypeName { get; }

    protected SpkObject(int typeCode) => TypeId = typeCode;

    public override string ToString() => $"[type:{TypeName}]";

    public abstract object ToObject();

    public virtual SpkObject Clone() => (SpkObject)MemberwiseClone();

    public abstract bool Equals(SpkObject? other);

    public sealed override bool Equals(object? obj) => obj is SpkObject other && Equals(other);

    public abstract override int GetHashCode();
}

public static class Extensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTrue(this SpkObject self) =>
        !ReferenceEquals(self, False) && !ReferenceEquals(self, Nil);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFalse(this SpkObject self) =>
        ReferenceEquals(self, False) || ReferenceEquals(self, Nil);

    //Doesn't generate an error if type check fails
    public static bool Is(this SpkObject self, int typeId) => self.TypeId == typeId;

    //Generates error if type check fails
    public static bool Is(this SpkObject self, ExecutionContext ctx, int typeId)
    {
        if (self.TypeId != typeId)
        {
            ctx.InvalidType(typeId, self);
            return false;
        }

        return true;
    }

    public static SpkTypeInfo GetTypeInfo(this SpkObject self, ExecutionContext ctx) => ctx.RuntimeContext.Types[self.TypeId];

    internal static SpkObject ToError(this SpkObject self)
    {
        if (self is SpkExceptionObject)
        {
            return self;
        }

        return ErrorGenerators.RuntimeException(SpkError.Failure, self);
    }

    //Returns a function encapsulated by an iterator, accepts: an iterator, a function
    //which is already an iterator function, a function that might return an iterator function,
    //an object that implements built-in "Iterator" method, an object that implements
    //built-in "Call" method (which supposedly returns an iterator, acts in same way as
    //"Iterator" method
    internal static SpkFunction? GetIterator(this SpkObject self, ExecutionContext ctx)
    {
        if (self.TypeId is Spk.Iterator)
        {
            return ((SpkIterator)self).GetIteratorFunction();
        }

        if (self.TypeId is Spk.Function)
        {
            if (self is SpkNativeIteratorFunction)
            {
                return (SpkFunction)self;
            }

            var obj = ((SpkFunction)self).Call(ctx);
            var ret = obj as SpkFunction;

            if (ret is null)
            {
                ctx.InvalidType();
            }

            return ret;
        }

        var type = ctx.RuntimeContext.Types[self.TypeId];

        if (type.HasInstanceMember(self, Builtins.Iterate, ctx))
        {
            var inst = type.GetInstanceMember(self, Builtins.Iterate, ctx);
            return inst.GetIterator(ctx);
        }
        else
        {
            var member = type.GetInstanceMember(self, Builtins.Call, ctx);

            if (ctx.HasErrors)
            {
                ctx.Error = null;
                ctx.OperationNotSupported(Builtins.Iterate, self);
                return null;
            }

            return member.GetIterator(ctx);
        }
    }

    //Calls a native implementation of "ToString" for a given object with an exception
    //for string and TypeInfo (no native implementation of ToString for TypeInfo)
    public static SpkString ToString(this SpkObject self, ExecutionContext ctx, SpkString? format = null)
    {
        if (self is SpkString str)
        {
            return str;
        }
        else if (self is SpkTypeInfo ti)
        {
            return new SpkString(ti.ReflectedTypeName);
        }
        else
        {
            var t = ctx.RuntimeContext.Types[self.TypeId];
            var ret = format is null ? t.ToString(ctx, self) : t.ToStringWithFormat(ctx, self, format);

            if (ReferenceEquals(ctx.Error, SpkFunction.CallbackPending))
            {
                ret = format is null ? ctx.InvokeCallBackFunction() : ctx.InvokeCallBackFunction(format);
            }

            return ret is SpkString s ? s : new SpkString(ret.ToString());
        }
    }

    public static bool Equals(this SpkObject left, SpkObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Eq(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpkFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static bool NotEquals(this SpkObject left, SpkObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Neq(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpkFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static bool Lesser(this SpkObject left, SpkObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Lt(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpkFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static bool LesserOrEquals(this SpkObject left, SpkObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Lte(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpkFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static bool Greater(this SpkObject left, SpkObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Gt(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpkFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static bool GreaterOrEquals(this SpkObject left, SpkObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Gte(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpkFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static SpkObject Concat(this SpkObject left, SpkObject right, ExecutionContext ctx)
    {
        var self = left.ToString(ctx);
        var type = ctx.RuntimeContext.String;
        var ret = type.Add(ctx, self, right);

        if (ReferenceEquals(ctx.Error, SpkFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret;
    }

    public static SpkObject Add(this SpkObject left, SpkObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Add(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpkFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret;
    }

    public static SpkObject Negate(this SpkObject self, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[self.TypeId];
        var ret = type.Neg(ctx, self);

        if (ReferenceEquals(ctx.Error, SpkFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction();
        }

        return ret;
    }

    //Returns a function if an objects is a function or implements "Call" method
    public static SpkFunction? ToFunction(this SpkObject self, ExecutionContext ctx)
    {
        if (self is SpkFunction func)
        {
            return func;
        }

        if (self.Is(Spk.TypeInfo))
        {
            var ti = (SpkTypeInfo)self;

            if (ti.TryGetStaticMember(ctx, ti.ReflectedTypeName, out var value))
            {
                return value as SpkFunction;
            }
        }
        else
        {
            var typ = ctx.RuntimeContext.Types[self.TypeId];

            if (typ.TryGetInstanceMember(ctx, self, Builtins.Call, out var value))
            {
                return value as SpkFunction;
            }
        }

        ctx.InvalidType(Spk.Function, self);
        return default;
    }

    //Invokes a function obtained from "ToFunction"
    public static SpkObject Invoke(this SpkObject self, ExecutionContext ctx)
    {
        var func = self.ToFunction(ctx);

        if (func is null)
        {
            return Nil;
        }

        return func.Call(ctx);
    }

    public static SpkObject Invoke(this SpkObject self, ExecutionContext ctx, SpkObject arg)
    {
        var func = self.ToFunction(ctx);

        if (func is null)
        {
            return Nil;
        }

        return func.Call(ctx, arg);
    }

    public static SpkObject Invoke(this SpkObject self, ExecutionContext ctx, SpkObject arg1, SpkObject arg2)
    {
        var func = self.ToFunction(ctx);

        if (func is null)
        {
            return Nil;
        }

        return func.Call(ctx, arg1, arg2);
    }

    public static SpkObject Invoke(this SpkObject self, ExecutionContext ctx, params SpkObject[] args)
    {
        var func = self.ToFunction(ctx);

        if (func is null)
        {
            return Nil;
        }

        return func.Call(ctx, args);
    }

    private static readonly char[] invalidChars = new[] { ' ', '\t', '\n', '\r', '\'', '"' };

    public static string ToLiteral(this SpkObject obj, ExecutionContext ctx)
    {
        if (obj.TypeId is Spk.Char)
        {
            return StringUtil.Escape(obj.ToString(ctx).ToString(), "'");
        }
        else if (obj.TypeId is Spk.String)
        {
            return StringUtil.Escape(obj.ToString(ctx).ToString());
        }
        else
        {
            return obj.ToString(ctx).ToString();
        }
    }

    public static string ToLiteral(this IEnumerable<SpkObject> seq, ExecutionContext ctx)
    {
        var c = 0;
        var sb = new StringBuilder();

        foreach (SpkObject o in seq)
        {
            if (c > 0)
            {
                sb.Append(", ");
            }

            if (o is SpkLabel lab)
            {
                if (lab.Mutable)
                {
                    sb.Append("mut ");
                }

                if (!char.IsLower(lab.Label[0]) || lab.Label.IndexOfAny(invalidChars) != -1)
                {
                    sb.Append(StringUtil.Escape(lab.Label));
                }
                else
                {
                    sb.Append(lab.Label);
                }

                sb.Append(':');
                sb.Append(' ');
                sb.Append(lab.Value.ToLiteral(ctx));
            }
            else
            {
                sb.Append(o.ToLiteral(ctx));
            }

            c++;
        }

        return sb.ToString();
    }
}
