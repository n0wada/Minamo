using Minamo.Debug;
using Minamo.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Minamo.Runtime.Types;

internal sealed class MinamoForeignConstructor : MinamoForeignFunction
{
    private readonly Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject> fun;

    public MinamoForeignConstructor(Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject> fun)
        : base("new", new Par[] { new Par("values", ParKind.VarArg) }, 0) => this.fun = fun;

    private MinamoForeignConstructor(Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject> fun, Par[] pars) : base("new", pars, 0) => this.fun = fun;

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args) => fun(ctx, Self!, args[0]);

    protected override MinamoFunction Clone(ExecutionContext ctx) => new MinamoForeignConstructor(fun, Parameters);

    protected override bool Equals(MinamoFunction func) => func is MinamoForeignConstructor c && c.fun.Equals(fun);
}

public sealed class MinamoInterop : MinamoObject
{
    internal readonly Type Type;
    internal readonly object Object;

    public override string TypeName => $"{nameof(MinamoTypeCodes.Interop)}<{Type.FullName ?? Type.Name}>";
    
    public MinamoInterop(Type type, object obj) : base(MinamoTypeCodes.Interop) => (Type, Object) = (type, obj);

    public MinamoInterop(Type obj) : base(MinamoTypeCodes.Interop) => (Type, Object) = (obj, obj);

    public override int GetHashCode() => Object.GetHashCode();

    public override object ToObject() => Object;

    public override string ToString() => Object.ToString() ?? "";

    public override bool Equals(MinamoObject? other) => other is MinamoInterop i && ReferenceEquals(Object, i.Object);
}

internal sealed class MinamoInteropFunction : MinamoForeignFunction
{
    private readonly static Par[] pars = new Par[] { new Par("args", ParKind.VarArg) };
    private readonly string name;
    private readonly Type type;
    private readonly List<MethodInfo> methods;
    private readonly ParameterInfo[][] parameters;

    public override string FunctionName => name;

    public MinamoInteropFunction(string name, Type type, List<MethodInfo> methods, bool auto) : base(name, pars, 0) =>
        (this.name, this.type, this.methods, Attr, parameters) = (name, type, methods, auto ? FunAttr.Auto : FunAttr.None, new ParameterInfo[methods.Count][]);

    protected override MinamoObject BindOrRun(ExecutionContext ctx, MinamoObject arg)
    {
        if (Auto)
        {
            return CallInteropMethod(ctx, arg, Array.Empty<MinamoObject>());
        }

        return base.BindOrRun(ctx, arg);
    }

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args) => CallInteropMethod(ctx, Self!, args);

    private MinamoObject CallInteropMethod(ExecutionContext ctx, MinamoObject self, MinamoObject[] args)
    {
        var tupleArgs = args.Length > 0 ? ((MinamoTuple)args[0]).UnsafeAccess() : null;
        var arguments = tupleArgs is null ? Array.Empty<object>() : tupleArgs.Select(a => a.ToObject()).ToArray();
        var argumentTypes = tupleArgs is null ? Array.Empty<Type>() : arguments.Select(a => a is null ? MinamoNil.Instance.GetType() : a.GetType()).ToArray();

        if (!ResolveMethod(self, arguments, argumentTypes, false, out var result))
        {
            if (!ResolveMethod(self, arguments, argumentTypes, true, out result))
            {
                return ctx.MethodNotFound(name, type, tupleArgs);
            }
        }

        return result;
    }

    private bool ResolveMethod(MinamoObject self, object[] arguments, Type[] argumentTypes, bool generalize, out MinamoObject result)
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

    protected override MinamoFunction Clone(ExecutionContext ctx) => new MinamoInteropFunction(name, type, methods, Attr == FunAttr.Auto);

    protected override bool Equals(MinamoFunction func) => func is MinamoInteropFunction f && f.name == name && f.type.Equals(type);
}
