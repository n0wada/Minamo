using Spellkit.Compiler;
using Spellkit.Debug;

namespace Spellkit.Runtime.Types.Functions;

internal sealed class SpellkitExceptionConstructor : SpellkitForeignFunction
{
    private readonly Func<ExecutionContext, SpellkitTuple, SpellkitObject> fun;

    public SpellkitExceptionConstructor(string name, Func<ExecutionContext, SpellkitTuple, SpellkitObject> fun, Par par)
        : base(name, new[] { par }) => (this.fun, Attr) = (fun, FunAttr.Variadic);

    protected override SpellkitObject CallWithMemoryLayout(ExecutionContext ctx, SpellkitObject[] args) => fun(ctx, (SpellkitTuple)args[0]);

    protected override SpellkitFunction Clone(ExecutionContext ctx) => this;

    public override object ToObject() => fun;

    protected override bool Equals(SpellkitFunction func) => func is SpellkitExceptionConstructor c && c.fun.Equals(fun);
}
