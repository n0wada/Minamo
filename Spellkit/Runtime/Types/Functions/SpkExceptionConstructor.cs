using Spellkit.Compiler;
using Spellkit.Debug;

namespace Spellkit.Runtime.Types.Functions;

internal sealed class SpkExceptionConstructor : SpkForeignFunction
{
    private readonly Func<ExecutionContext, SpkTuple, SpkObject> fun;

    public SpkExceptionConstructor(string name, Func<ExecutionContext, SpkTuple, SpkObject> fun, Par par)
        : base(name, new[] { par }) => (this.fun, Attr) = (fun, FunAttr.Variadic);

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] args) => fun(ctx, (SpkTuple)args[0]);

    protected override SpkFunction Clone(ExecutionContext ctx) => this;

    public override object ToObject() => fun;

    protected override bool Equals(SpkFunction func) => func is SpkExceptionConstructor c && c.fun.Equals(fun);
}
