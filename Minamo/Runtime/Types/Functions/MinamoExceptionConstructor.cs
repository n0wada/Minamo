using Minamo.Compiler;
using Minamo.Debug;

namespace Minamo.Runtime.Types.Functions;

internal sealed class MinamoExceptionConstructor : MinamoForeignFunction
{
    private readonly Func<ExecutionContext, MinamoTuple, MinamoObject> fun;

    public MinamoExceptionConstructor(string name, Func<ExecutionContext, MinamoTuple, MinamoObject> fun, Par par)
        : base(name, new[] { par }) => (this.fun, Attr) = (fun, FunAttr.Variadic);

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args) => fun(ctx, (MinamoTuple)args[0]);

    protected override MinamoFunction Clone(ExecutionContext ctx) => this;

    public override object ToObject() => fun;

    protected override bool Equals(MinamoFunction func) => func is MinamoExceptionConstructor c && c.fun.Equals(fun);
}
