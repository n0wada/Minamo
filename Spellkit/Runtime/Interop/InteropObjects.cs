using Spellkit.Debug;
using Spellkit.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Spellkit.Runtime.Types;

internal sealed class SpkForeignConstructor : SpkForeignFunction
{
    private readonly Func<ExecutionContext, SpkObject, SpkObject, SpkObject> fun;

    public SpkForeignConstructor(Func<ExecutionContext, SpkObject, SpkObject, SpkObject> fun)
        : base("new", new Par[] { new Par("values", ParKind.VarArg) }, 0) => this.fun = fun;

    private SpkForeignConstructor(Func<ExecutionContext, SpkObject, SpkObject, SpkObject> fun, Par[] pars) : base("new", pars, 0) => this.fun = fun;

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] args) => fun(ctx, Self!, args[0]);

    protected override SpkFunction Clone(ExecutionContext ctx) => new SpkForeignConstructor(fun, Parameters);

    protected override bool Equals(SpkFunction func) => func is SpkForeignConstructor c && c.fun.Equals(fun);
}

public sealed class SpkInterop : SpkObject
{
    internal readonly Type Type;
    internal readonly object Object;

    public override string TypeName => $"{nameof(Spk.Interop)}<{Type.FullName ?? Type.Name}>";
    
    public SpkInterop(Type type, object obj) : base(Spk.Interop) => (Type, Object) = (type, obj);

    public SpkInterop(Type obj) : base(Spk.Interop) => (Type, Object) = (obj, obj);

    public override int GetHashCode() => Object.GetHashCode();

    public override object ToObject() => Object;

    public override string ToString() => Object.ToString() ?? "";

    public override bool Equals(SpkObject? other) => other is SpkInterop i && ReferenceEquals(Object, i.Object);
}

internal sealed class SpkInteropFunction : SpkForeignFunction
{
    private readonly static Par[] pars = new Par[] { new Par("args", ParKind.VarArg) };
    private readonly string name;
    private readonly Type type;
    private readonly List<MethodInfo> methods;
    private readonly ParameterInfo[][] parameters;

    public override string FunctionName => name;

    public SpkInteropFunction(string name, Type type, List<MethodInfo> methods, bool auto) : base(name, pars, 0) =>
        (this.name, this.type, this.methods, Attr, parameters) = (name, type, methods, auto ? FunAttr.Auto : FunAttr.None, new ParameterInfo[methods.Count][]);

    protected override SpkObject BindOrRun(ExecutionContext ctx, SpkObject arg)
    {
        if (Auto)
        {
            return CallInteropMethod(ctx, arg, Array.Empty<SpkObject>());
        }

        return base.BindOrRun(ctx, arg);
    }

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] args) => CallInteropMethod(ctx, Self!, args);

    private SpkObject CallInteropMethod(ExecutionContext ctx, SpkObject self, SpkObject[] args)
    {
        var tupleArgs = args.Length > 0 ? ((SpkTuple)args[0]).UnsafeAccess() : null;
        var arguments = tupleArgs is null ? Array.Empty<object>() : tupleArgs.Select(a => a.ToObject()).ToArray();
        var argumentTypes = tupleArgs is null ? Array.Empty<Type>() : arguments.Select(a => a is null ? SpkNil.Instance.GetType() : a.GetType()).ToArray();

        if (!ResolveMethod(self, arguments, argumentTypes, false, out var result))
        {
            if (!ResolveMethod(self, arguments, argumentTypes, true, out result))
            {
                return ctx.MethodNotFound(name, type, tupleArgs);
            }
        }

        return result;
    }

    private bool ResolveMethod(SpkObject self, object[] arguments, Type[] argumentTypes, bool generalize, out SpkObject result)
    {
        result = Nil;

        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            var pars = parameters[i] is null
                ? parameters[i] = m.GetParameters() : parameters[i];

            if (pars.Length != arguments.Length || !CheckArguments(arguments, argumentTypes, generalize, pars))
            {
                continue;
            }

            var ret = m.Invoke(m.IsStatic ? null : self.ToObject(), arguments);
            result = TypeConverter.ConvertFrom(ret, m.ReturnType);

            return true;
        }

        return false;
    }

    private bool CheckArguments(object[] arguments, Type[] argumentTypes, bool generalize, ParameterInfo[] pars)
    {
        for (var i = 0; i < arguments.Length; i++)
        {
            var (pt, at) = (pars[i].ParameterType, argumentTypes[i]);

            if (!at.Equals(pt) && (!generalize || !pt.IsAssignableFrom(at)) && (arguments[i] is not null || !pt.IsClass))
            {
                return false;
            }
        }

        return true;
    }

    protected override SpkFunction Clone(ExecutionContext ctx) => new SpkInteropFunction(name, type, methods, Attr == FunAttr.Auto);

    protected override bool Equals(SpkFunction func) => func is SpkInteropFunction f && f.name == name && f.type.Equals(type);
}
