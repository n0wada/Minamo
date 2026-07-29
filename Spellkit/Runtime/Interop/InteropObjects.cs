using Spellkit.Debug;
using Spellkit.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Spellkit.Runtime.Types;

internal sealed class SpellkitForeignConstructor : SpellkitForeignFunction
{
    private readonly Func<ExecutionContext, SpellkitObject, SpellkitObject, SpellkitObject> fun;

    public SpellkitForeignConstructor(Func<ExecutionContext, SpellkitObject, SpellkitObject, SpellkitObject> fun)
        : base("new", new Par[] { new Par("values", ParKind.VarArg) }, 0) => this.fun = fun;

    private SpellkitForeignConstructor(Func<ExecutionContext, SpellkitObject, SpellkitObject, SpellkitObject> fun, Par[] pars) : base("new", pars, 0) => this.fun = fun;

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] args) => fun(ctx, Self!, args[0]);

    protected override SpellkitFunction Clone(ExecutionContext ctx) => new SpellkitForeignConstructor(fun, Parameters);

    protected override bool Equals(SpellkitFunction func) => func is SpellkitForeignConstructor c && c.fun.Equals(fun);
}

public sealed class SpellkitInterop : SpellkitObject
{
    internal readonly Type Type;
    internal readonly object Object;

    public override string TypeName => $"{nameof(SpellkitTypeCodes.Interop)}<{Type.FullName ?? Type.Name}>";
    
    public SpellkitInterop(Type type, object obj) : base(SpellkitTypeCodes.Interop) => (Type, Object) = (type, obj);

    public SpellkitInterop(Type obj) : base(SpellkitTypeCodes.Interop) => (Type, Object) = (obj, obj);

    public override int GetHashCode() => Object.GetHashCode();

    public override object ToObject() => Object;

    public override string ToString() => Object.ToString() ?? "";

    public override bool Equals(SpellkitObject? other) => other is SpellkitInterop i && ReferenceEquals(Object, i.Object);
}

internal sealed class SpellkitInteropFunction : SpellkitForeignFunction
{
    private readonly static Par[] pars = new Par[] { new Par("args", ParKind.VarArg) };
    private readonly string name;
    private readonly Type type;
    private readonly List<MethodInfo> methods;
    private readonly ParameterInfo[][] parameters;

    public override string FunctionName => name;

    public SpellkitInteropFunction(string name, Type type, List<MethodInfo> methods, bool auto) : base(name, pars, 0) =>
        (this.name, this.type, this.methods, Attr, parameters) = (name, type, methods, auto ? FunAttr.Auto : FunAttr.None, new ParameterInfo[methods.Count][]);

    protected override SpellkitObject BindOrRun(ExecutionContext ctx, SpellkitObject arg)
    {
        if (Auto)
        {
            return CallInteropMethod(ctx, arg, Array.Empty<SpellkitObject>());
        }

        return base.BindOrRun(ctx, arg);
    }

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] args) => CallInteropMethod(ctx, Self!, args);

    private SpellkitObject CallInteropMethod(ExecutionContext ctx, SpellkitObject self, SpellkitObject[] args)
    {
        var tupleArgs = args.Length > 0 ? ((SpellkitTuple)args[0]).UnsafeAccess() : null;
        var arguments = tupleArgs is null ? Array.Empty<object>() : tupleArgs.Select(a => a.ToObject()).ToArray();
        var argumentTypes = tupleArgs is null ? Array.Empty<Type>() : arguments.Select(a => a is null ? SpellkitNil.Instance.GetType() : a.GetType()).ToArray();

        if (!ResolveMethod(self, arguments, argumentTypes, false, out var result))
        {
            if (!ResolveMethod(self, arguments, argumentTypes, true, out result))
            {
                return ctx.MethodNotFound(name, type, tupleArgs);
            }
        }

        return result;
    }

    private bool ResolveMethod(SpellkitObject self, object[] arguments, Type[] argumentTypes, bool generalize, out SpellkitObject result)
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

    protected override SpellkitFunction Clone(ExecutionContext ctx) => new SpellkitInteropFunction(name, type, methods, Attr == FunAttr.Auto);

    protected override bool Equals(SpellkitFunction func) => func is SpellkitInteropFunction f && f.name == name && f.type.Equals(type);
}
