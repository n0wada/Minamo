using Spellkit.Debug;
using Spellkit.Compiler;

namespace Spellkit.Runtime.Types;

internal sealed class CompositionContainer : SpellkitForeignFunction
{
    private readonly SpellkitFunction first;
    private readonly SpellkitFunction second;

    public CompositionContainer(SpellkitFunction first, SpellkitFunction second) : base(null, first.Parameters, first.VarArgIndex)
    {
        this.first = first;
        this.second = second;
    }

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] args)
    {
        var res = first.Call(ctx, args);

        if (ctx.HasErrors)
        {
            return Nil;
        }

        return second.Call(ctx, res);
    }

    protected override bool Equals(SpellkitFunction func) =>
           func is CompositionContainer cc
        && cc.first.Equals(first) && cc.second.Equals(second);
}

internal sealed class SpellkitMissingMethod : SpellkitForeignFunction
{
    private static readonly Par[] parameters = new Par[] { new Par("%args", ParKind.VarArg) };

    internal const string Name = "MissingMethod";
    private readonly SpellkitNativeFunction fun;
    private readonly string missingMethodName;

    public SpellkitMissingMethod(string name, SpellkitNativeFunction fun) : base(Name, parameters, 0) =>
        (this.fun, missingMethodName) = (fun, name);

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] args)
    {
        var fn = Self is not null ? fun.BindToInstance(ctx, Self) : fun;
        var pars = new SpellkitObject[2];
        pars[0] = new SpellkitString(missingMethodName);
        pars[1] = args.Length == 0 ? SpellkitTuple.Empty : (SpellkitTuple)args[0];
        return fn.Call(ctx, pars);
    }

    protected override SpellkitFunction Clone(ExecutionContext ctx) => new SpellkitMissingMethod(missingMethodName, fun);

    public override object ToObject() => fun.ToObject();

    protected override bool Equals(SpellkitFunction func) =>
           func is SpellkitMissingMethod mi && mi.fun.Equals(fun)
        && IsSameInstance(this, func);
}

internal class SpellkitUnaryFunction : SpellkitForeignFunction
{
    private readonly Func<ExecutionContext, SpellkitObject, SpellkitObject> fun;

    public SpellkitUnaryFunction(string name, Func<ExecutionContext, SpellkitObject, SpellkitObject> fun)
        : base(name, Array.Empty<Par>(), -1) => this.fun = fun;

    public SpellkitUnaryFunction(string name, Func<ExecutionContext, SpellkitObject, SpellkitObject> fun, bool isPropertyGetter)
        : this(name, fun)
    {
        if (isPropertyGetter)
        {
            Attr |= FunAttr.Auto;
        }
    }

    internal SpellkitObject CallUnary(ExecutionContext ctx, SpellkitObject self) => fun(ctx, self);

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] args) => fun(ctx, Self!);

    protected override SpellkitObject BindOrRun(ExecutionContext ctx, SpellkitObject arg)
    {
        if (!Auto)
        {
            return BindToInstance(ctx, arg);
        }

        return fun(ctx, arg);
    }

    protected override SpellkitFunction Clone(ExecutionContext ctx) => new SpellkitUnaryFunction(FunctionName, fun);

    public override object ToObject() => fun;

    protected override bool Equals(SpellkitFunction func) =>
           func is SpellkitUnaryFunction bin && ReferenceEquals(bin.fun, fun)
        && IsSameInstance(this, func);
}

internal sealed class SpellkitBinaryFunction : SpellkitForeignFunction
{
    private readonly Func<ExecutionContext, SpellkitObject, SpellkitObject, SpellkitObject> fun;

    public SpellkitBinaryFunction(string name, Func<ExecutionContext, SpellkitObject, SpellkitObject, SpellkitObject> fun, Par par)
        : base(name, new Par[] { par }, -1) => this.fun = fun;

    private SpellkitBinaryFunction(string name, Func<ExecutionContext, SpellkitObject, SpellkitObject, SpellkitObject> fun, Par[] pars)
        : base(name, pars, -1) => this.fun = fun;

    internal SpellkitObject CallBinary(ExecutionContext ctx, SpellkitObject self, SpellkitObject arg) => fun(ctx, self, arg);

    protected override bool CanCallWithSingleArgumentDirectly => true;

    protected override SpellkitObject CallWithSingleArgument(ExecutionContext ctx, SpellkitObject arg) => fun(ctx, Self!, arg);

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] args) => fun(ctx, Self!, args[0]);

    protected override SpellkitFunction Clone(ExecutionContext ctx) => new SpellkitBinaryFunction(FunctionName, fun, Parameters);

    public override object ToObject() => fun;

    protected override bool Equals(SpellkitFunction func) =>
           func is SpellkitBinaryFunction bin && ReferenceEquals(bin.fun, fun) && IsSameInstance(this, func);
}

internal sealed class SpellkitTernaryFunction : SpellkitForeignFunction
{
    private readonly Func<ExecutionContext, SpellkitObject, SpellkitObject, SpellkitObject, SpellkitObject> fun;

    public SpellkitTernaryFunction(string name, Func<ExecutionContext, SpellkitObject, SpellkitObject, SpellkitObject, SpellkitObject> fun, Par par1, Par par2)
        : base(name, new Par[] { par1, par2 }, -1) => this.fun = fun;

    private SpellkitTernaryFunction(string name, Func<ExecutionContext, SpellkitObject, SpellkitObject, SpellkitObject, SpellkitObject> fun, Par[] pars)
        : base(name, pars, -1) => this.fun = fun;

    internal SpellkitObject CallTernary(ExecutionContext ctx, SpellkitObject self, SpellkitObject arg1, SpellkitObject arg2) => fun(ctx, self, arg1, arg2);

    protected override bool CanCallWithTwoArgumentsDirectly => true;

    protected override SpellkitObject CallWithTwoArguments(ExecutionContext ctx, SpellkitObject arg1, SpellkitObject arg2) => fun(ctx, Self!, arg1, arg2);

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] args) => fun(ctx, Self!, args[0], args[1]);

    protected override SpellkitFunction Clone(ExecutionContext ctx) => new SpellkitTernaryFunction(FunctionName, fun, Parameters);

    public override object ToObject() => fun;

    protected override bool Equals(SpellkitFunction func) =>
           func is SpellkitTernaryFunction ter && ReferenceEquals(ter.fun, fun)
        && IsSameInstance(this, func);
}
