using Spellkit.Debug;
using Spellkit.Compiler;

namespace Spellkit.Runtime.Types;

internal sealed class CompositionContainer : SpkForeignFunction
{
    private readonly SpkFunction first;
    private readonly SpkFunction second;

    public CompositionContainer(SpkFunction first, SpkFunction second) : base(null, first.Parameters, first.VarArgIndex)
    {
        this.first = first;
        this.second = second;
    }

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] args)
    {
        var res = first.Call(ctx, args);

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return second.Call(ctx, res);
    }

    protected override bool Equals(SpkFunction func) => 
           func is CompositionContainer cc
        && cc.first.Equals(first) && cc.second.Equals(second);
}

internal sealed class SpkMissingMethod : SpkForeignFunction
{
    private static readonly Par[] parameters = new Par[] { new Par("%args", ParKind.VarArg) };

    internal const string Name = "MissingMethod";
    private readonly SpkNativeFunction fun;
    private readonly string missingMethodName;

    public SpkMissingMethod(string name, SpkNativeFunction fun) : base(Name, parameters, 0) => 
        (this.fun, missingMethodName) = (fun, name);

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] args)
    {
        var fn = Self is not null ? fun.BindToInstance(ctx, Self) : fun;
        var pars = new SpkObject[2];
        pars[0] = new SpkString(missingMethodName);
        pars[1] = args.Length == 0 ? SpkTuple.Empty : (SpkTuple)args[0];
        return fn.Call(ctx, pars);
    }

    protected override SpkFunction Clone(ExecutionContext ctx) => new SpkMissingMethod(missingMethodName, fun);

    public override object ToObject() => fun.ToObject();

    protected override bool Equals(SpkFunction func) =>
           func is SpkMissingMethod mi && mi.fun.Equals(fun)
        && IsSameInstance(this, func);
}

internal class SpkUnaryFunction : SpkForeignFunction
{
    private readonly Func<ExecutionContext, SpkObject, SpkObject> fun;

    public SpkUnaryFunction(string name, Func<ExecutionContext, SpkObject, SpkObject> fun)
        : base(name, Array.Empty<Par>(), -1) => this.fun = fun;

    public SpkUnaryFunction(string name, Func<ExecutionContext, SpkObject, SpkObject> fun, bool isPropertyGetter)
        : this(name, fun)
    {
        if (isPropertyGetter)
        {
            Attr |= FunAttr.Auto;
        }
    }

    internal SpkObject CallUnary(ExecutionContext ctx, SpkObject self) => fun(ctx, self);

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] args) => fun(ctx, Self!);

    protected override SpkObject BindOrRun(ExecutionContext ctx, SpkObject arg)
    {
        if (!Auto)
        {
            return BindToInstance(ctx, arg);
        }

        return fun(ctx, arg);
    }

    protected override SpkFunction Clone(ExecutionContext ctx) => new SpkUnaryFunction(FunctionName, fun);

    public override object ToObject() => fun;

    protected override bool Equals(SpkFunction func) => 
           func is SpkUnaryFunction bin && ReferenceEquals(bin.fun, fun)
        && IsSameInstance(this, func);
}

internal sealed class SpkBinaryFunction : SpkForeignFunction
{
    private readonly Func<ExecutionContext, SpkObject, SpkObject, SpkObject> fun;

    public SpkBinaryFunction(string name, Func<ExecutionContext, SpkObject, SpkObject, SpkObject> fun, Par par)
        : base(name, new Par[] { par }, -1) => this.fun = fun;

    private SpkBinaryFunction(string name, Func<ExecutionContext, SpkObject, SpkObject, SpkObject> fun, Par[] pars)
        : base(name, pars, -1) => this.fun = fun;

    internal SpkObject CallBinary(ExecutionContext ctx, SpkObject self, SpkObject arg) => fun(ctx, self, arg);

    protected override bool CanCallWithSingleArgumentDirectly => true;

    protected override SpkObject CallWithSingleArgument(ExecutionContext ctx, SpkObject arg) => fun(ctx, Self!, arg);

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] args) => fun(ctx, Self!, args[0]);

    protected override SpkFunction Clone(ExecutionContext ctx) => new SpkBinaryFunction(FunctionName, fun, Parameters);

    public override object ToObject() => fun;

    protected override bool Equals(SpkFunction func) => 
           func is SpkBinaryFunction bin && ReferenceEquals(bin.fun, fun) && IsSameInstance(this, func);
}

internal sealed class SpkTernaryFunction : SpkForeignFunction
{
    private readonly Func<ExecutionContext, SpkObject, SpkObject, SpkObject, SpkObject> fun;

    public SpkTernaryFunction(string name, Func<ExecutionContext, SpkObject, SpkObject, SpkObject, SpkObject> fun, Par par1, Par par2)
        : base(name, new Par[] { par1, par2 }, -1) => this.fun = fun;

    private SpkTernaryFunction(string name, Func<ExecutionContext, SpkObject, SpkObject, SpkObject, SpkObject> fun, Par[] pars)
        : base(name, pars, -1) => this.fun = fun;

    internal SpkObject CallTernary(ExecutionContext ctx, SpkObject self, SpkObject arg1, SpkObject arg2) => fun(ctx, self, arg1, arg2);

    protected override bool CanCallWithTwoArgumentsDirectly => true;

    protected override SpkObject CallWithTwoArguments(ExecutionContext ctx, SpkObject arg1, SpkObject arg2) => fun(ctx, Self!, arg1, arg2);

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] args) => fun(ctx, Self!, args[0], args[1]);

    protected override SpkFunction Clone(ExecutionContext ctx) => new SpkTernaryFunction(FunctionName, fun, Parameters);

    public override object ToObject() => fun;

    protected override bool Equals(SpkFunction func) =>
           func is SpkTernaryFunction ter && ReferenceEquals(ter.fun, fun)
        && IsSameInstance(this, func);
}
