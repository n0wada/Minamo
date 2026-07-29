using Spellkit.Compiler;
using Spellkit.Parser;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Spellkit.Runtime.Types;

public abstract class SpellkitObject : IEquatable<SpellkitObject>
{
    public virtual int TypeId { get; }

    public abstract string TypeName { get; }

    protected SpellkitObject(int typeCode) => TypeId = typeCode;

    public override string ToString() => $"[type:{TypeName}]";

    public abstract object ToObject();

    public virtual SpellkitObject Clone() => (SpellkitObject)MemberwiseClone();

    public abstract bool Equals(SpellkitObject? other);

    public sealed override bool Equals(object? obj) => obj is SpellkitObject other && Equals(other);

    public abstract override int GetHashCode();
}

public static class Extensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTrue(this SpellkitObject self) =>
        !ReferenceEquals(self, False) && !ReferenceEquals(self, Nil);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFalse(this SpellkitObject self) =>
        ReferenceEquals(self, False) || ReferenceEquals(self, Nil);

    //Doesn't generate an error if type check fails
    public static bool Is(this SpellkitObject self, int typeId) => self.TypeId == typeId;

    //Generates error if type check fails
    public static bool Is(this SpellkitObject self, ExecutionContext ctx, int typeId)
    {
        if (self.TypeId != typeId)
        {
            ctx.InvalidType(typeId, self);
            return false;
        }

        return true;
    }

    public static SpellkitTypeInfo GetTypeInfo(this SpellkitObject self, ExecutionContext ctx) => ctx.RuntimeContext.Types[self.TypeId];

    internal static SpellkitObject ToError(this SpellkitObject self)
    {
        if (self is SpellkitExceptionObject)
        {
            return self;
        }

        return ErrorGenerators.RuntimeException(SpellkitError.Failure, self);
    }

    //Returns a function encapsulated by an iterator, accepts: an iterator, a function
    //which is already an iterator function, a function that might return an iterator function,
    //an object that implements built-in "Iterator" method, an object that implements
    //built-in "Call" method (which supposedly returns an iterator, acts in same way as
    //"Iterator" method
    internal static SpellkitFunction? GetIterator(this SpellkitObject self, ExecutionContext ctx)
    {
        if (self.TypeId is SpellkitTypeCodes.Iterator)
        {
            return ((SpellkitIterator)self).GetIteratorFunction();
        }

        if (self.TypeId is SpellkitTypeCodes.Function)
        {
            if (self is SpellkitNativeIteratorFunction)
            {
                return (SpellkitFunction)self;
            }

            var obj = ((SpellkitFunction)self).Call(ctx);
            var ret = obj as SpellkitFunction;

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
    public static SpellkitString ToString(this SpellkitObject self, ExecutionContext ctx, SpellkitString? format = null)
    {
        if (self is SpellkitString str)
        {
            return str;
        }
        else if (self is SpellkitTypeInfo ti)
        {
            return new SpellkitString(ti.ReflectedTypeName);
        }
        else
        {
            var t = ctx.RuntimeContext.Types[self.TypeId];
            var ret = format is null ? t.ToString(ctx, self) : t.ToStringWithFormat(ctx, self, format);

            if (ReferenceEquals(ctx.Error, SpellkitFunction.CallbackPending))
            {
                ret = format is null ? ctx.InvokeCallBackFunction() : ctx.InvokeCallBackFunction(format);
            }

            return ret is SpellkitString s ? s : new SpellkitString(ret.ToString());
        }
    }

    public static bool Equals(this SpellkitObject left, SpellkitObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Eq(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpellkitFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static bool NotEquals(this SpellkitObject left, SpellkitObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Neq(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpellkitFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static bool Lesser(this SpellkitObject left, SpellkitObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Lt(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpellkitFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static bool LesserOrEquals(this SpellkitObject left, SpellkitObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Lte(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpellkitFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static bool Greater(this SpellkitObject left, SpellkitObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Gt(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpellkitFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static bool GreaterOrEquals(this SpellkitObject left, SpellkitObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Gte(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpellkitFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret.IsTrue();
    }

    public static SpellkitObject Concat(this SpellkitObject left, SpellkitObject right, ExecutionContext ctx)
    {
        var self = left.ToString(ctx);
        var type = ctx.RuntimeContext.String;
        var ret = type.Add(ctx, self, right);

        if (ReferenceEquals(ctx.Error, SpellkitFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret;
    }

    public static SpellkitObject Add(this SpellkitObject left, SpellkitObject right, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[left.TypeId];
        var ret = type.Add(ctx, left, right);

        if (ReferenceEquals(ctx.Error, SpellkitFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction(right);
        }

        return ret;
    }

    public static SpellkitObject Negate(this SpellkitObject self, ExecutionContext ctx)
    {
        var type = ctx.RuntimeContext.Types[self.TypeId];
        var ret = type.Neg(ctx, self);

        if (ReferenceEquals(ctx.Error, SpellkitFunction.CallbackPending))
        {
            ret = ctx.InvokeCallBackFunction();
        }

        return ret;
    }

    internal static bool TryGetFunction(this SpellkitObject self, ExecutionContext ctx, out SpellkitFunction? function)
    {
        if (self is SpellkitFunction func)
        {
            function = func;
            return true;
        }

        if (self.Is(SpellkitTypeCodes.TypeInfo))
        {
            var ti = (SpellkitTypeInfo)self;

            if (ti.TryGetStaticMember(ctx, ti.ReflectedTypeName, out var value))
            {
                function = value as SpellkitFunction;
                return function is not null;
            }
        }
        else
        {
            var typ = ctx.RuntimeContext.Types[self.TypeId];

            if (typ.TryGetInstanceMember(ctx, self, Builtins.Call, out var value))
            {
                function = value as SpellkitFunction;
                return function is not null;
            }
        }

        function = null;
        return false;
    }

    //Returns a function if an object is a function or implements "Call".
    public static SpellkitFunction? ToFunction(this SpellkitObject self, ExecutionContext ctx)
    {
        if (self.TryGetFunction(ctx, out var function))
        {
            return function;
        }

        ctx.InvalidType(SpellkitTypeCodes.Function, self);
        return default;
    }

    //Invokes a function obtained from "ToFunction"
    public static SpellkitObject Invoke(this SpellkitObject self, ExecutionContext ctx)
    {
        var func = self.ToFunction(ctx);

        if (func is null)
        {
            return Nil;
        }

        return func.Call(ctx);
    }

    public static SpellkitObject Invoke(this SpellkitObject self, ExecutionContext ctx, SpellkitObject arg)
    {
        var func = self.ToFunction(ctx);

        if (func is null)
        {
            return Nil;
        }

        return func.Call(ctx, arg);
    }

    public static SpellkitObject Invoke(this SpellkitObject self, ExecutionContext ctx, SpellkitObject arg1, SpellkitObject arg2)
    {
        var func = self.ToFunction(ctx);

        if (func is null)
        {
            return Nil;
        }

        return func.Call(ctx, arg1, arg2);
    }

    public static SpellkitObject Invoke(this SpellkitObject self, ExecutionContext ctx, params SpellkitObject[] args)
    {
        var func = self.ToFunction(ctx);

        if (func is null)
        {
            return Nil;
        }

        return func.Call(ctx, args);
    }

    private static readonly char[] invalidChars = new[] { ' ', '\t', '\n', '\r', '\'', '"' };

    public static string ToLiteral(this SpellkitObject obj, ExecutionContext ctx)
    {
        if (obj.TypeId is SpellkitTypeCodes.Char)
        {
            return StringUtil.Escape(obj.ToString(ctx).ToString(), "'");
        }
        else if (obj.TypeId is SpellkitTypeCodes.String)
        {
            return StringUtil.Escape(obj.ToString(ctx).ToString());
        }
        else
        {
            return obj.ToString(ctx).ToString();
        }
    }

    public static string ToLiteral(this IEnumerable<SpellkitObject> seq, ExecutionContext ctx)
    {
        var c = 0;
        var sb = new StringBuilder();

        foreach (SpellkitObject o in seq)
        {
            if (c > 0)
            {
                sb.Append(", ");
            }

            if (o is SpellkitLabel lab)
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
