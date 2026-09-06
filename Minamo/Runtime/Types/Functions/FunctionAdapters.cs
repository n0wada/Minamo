using Minamo.Debug;
using Minamo.Compiler;

namespace Minamo.Runtime.Types;

internal sealed class CompositionContainer : MinamoForeignFunction
{
    private readonly MinamoFunction first;
    private readonly MinamoFunction second;

    public CompositionContainer(MinamoFunction first, MinamoFunction second) : base(null, first.Parameters, first.VarArgIndex)
    {
        this.first = first;
        this.second = second;
    }

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args)
    {
        var res = first.Call(ctx, args);

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return second.Call(ctx, res);
    }

    protected override bool Equals(MinamoFunction func) =>
           func is CompositionContainer cc
        && cc.first.Equals(first) && cc.second.Equals(second);
}

internal sealed class MinamoMissingMethod : MinamoForeignFunction
{
    private static readonly Par[] parameters = new Par[] { new Par("%args", ParKind.VarArg) };

    internal const string Name = "MissingMethod";
    private readonly MinamoNativeFunction fun;
    private readonly string missingMethodName;

    public MinamoMissingMethod(string name, MinamoNativeFunction fun) : base(Name, parameters, 0) =>
        (this.fun, missingMethodName) = (fun, name);

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args)
    {
        var fn = Self is not null ? fun.BindToInstance(ctx, Self) : fun;
        var pars = new MinamoObject[2];
        pars[0] = new MinamoString(missingMethodName);
        pars[1] = args.Length == 0 ? MinamoTuple.Empty : (MinamoTuple)args[0];
        return fn.Call(ctx, pars);
    }

    protected override MinamoFunction Clone(ExecutionContext ctx) => new MinamoMissingMethod(missingMethodName, fun);

    public override object ToObject() => fun.ToObject();

    protected override bool Equals(MinamoFunction func) =>
           func is MinamoMissingMethod mi && mi.fun.Equals(fun)
        && IsSameInstance(this, func);
}

internal class MinamoUnaryFunction : MinamoForeignFunction
{
    private readonly Func<ExecutionContext, MinamoObject, MinamoObject> fun;

    public MinamoUnaryFunction(string name, Func<ExecutionContext, MinamoObject, MinamoObject> fun)
        : base(name, Array.Empty<Par>(), -1) => this.fun = fun;

    public MinamoUnaryFunction(string name, Func<ExecutionContext, MinamoObject, MinamoObject> fun, bool isPropertyGetter)
        : this(name, fun)
    {
        if (isPropertyGetter)
        {
            Attr |= FunAttr.Auto;
        }
    }

    internal MinamoObject CallUnary(ExecutionContext ctx, MinamoObject self) => fun(ctx, self);

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args) => fun(ctx, Self!);

    protected override MinamoObject BindOrRun(ExecutionContext ctx, MinamoObject arg)
    {
        if (!Auto)
        {
            return BindToInstance(ctx, arg);
        }

        return fun(ctx, arg);
    }

    protected override MinamoFunction Clone(ExecutionContext ctx) => new MinamoUnaryFunction(FunctionName, fun);

    public override object ToObject() => fun;

    protected override bool Equals(MinamoFunction func) =>
           func is MinamoUnaryFunction bin && ReferenceEquals(bin.fun, fun)
        && IsSameInstance(this, func);
}

internal sealed class MinamoBinaryFunction : MinamoForeignFunction
{
    private readonly Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject> fun;

    public MinamoBinaryFunction(string name, Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject> fun, Par par)
        : base(name, new Par[] { par }, -1) => this.fun = fun;

    private MinamoBinaryFunction(string name, Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject> fun, Par[] pars)
        : base(name, pars, -1) => this.fun = fun;

    internal MinamoObject CallBinary(ExecutionContext ctx, MinamoObject self, MinamoObject arg) => fun(ctx, self, arg);

    protected override bool CanCallWithSingleArgumentDirectly => true;

    protected override MinamoObject CallWithSingleArgument(ExecutionContext ctx, MinamoObject arg) => fun(ctx, Self!, arg);

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args) => fun(ctx, Self!, args[0]);

    protected override MinamoFunction Clone(ExecutionContext ctx) => new MinamoBinaryFunction(FunctionName, fun, Parameters);

    public override object ToObject() => fun;

    protected override bool Equals(MinamoFunction func) =>
           func is MinamoBinaryFunction bin && ReferenceEquals(bin.fun, fun) && IsSameInstance(this, func);
}

internal sealed class MinamoTernaryFunction : MinamoForeignFunction
{
    private readonly Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject, MinamoObject> fun;

    public MinamoTernaryFunction(string name, Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject, MinamoObject> fun, Par par1, Par par2)
        : base(name, new Par[] { par1, par2 }, -1) => this.fun = fun;

    private MinamoTernaryFunction(string name, Func<ExecutionContext, MinamoObject, MinamoObject, MinamoObject, MinamoObject> fun, Par[] pars)
        : base(name, pars, -1) => this.fun = fun;

    internal MinamoObject CallTernary(ExecutionContext ctx, MinamoObject self, MinamoObject arg1, MinamoObject arg2) => fun(ctx, self, arg1, arg2);

    protected override bool CanCallWithTwoArgumentsDirectly => true;

    protected override MinamoObject CallWithTwoArguments(ExecutionContext ctx, MinamoObject arg1, MinamoObject arg2) => fun(ctx, Self!, arg1, arg2);

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args) => fun(ctx, Self!, args[0], args[1]);

    protected override MinamoFunction Clone(ExecutionContext ctx) => new MinamoTernaryFunction(FunctionName, fun, Parameters);

    public override object ToObject() => fun;

    protected override bool Equals(MinamoFunction func) =>
           func is MinamoTernaryFunction ter && ReferenceEquals(ter.fun, fun)
        && IsSameInstance(this, func);
}
