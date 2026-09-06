using Minamo.Compiler;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using Xunit;

namespace Minamo.UnitTesting.Runtime;

[Trait("Suite", "Runtime")]
public sealed class VmContinuationTests
{
    [Fact]
    public void SuspendsAndResumesWithItsEvaluationStack()
    {
        var unit = new Unit();
        unit.Layouts.Add(new MemoryLayout(0, 2, 0));
        unit.Ops.Add(Op.LoadInt1);
        unit.Ops.Add(Op.Suspend);
        unit.Ops.Add(Op.LoadInt1);
        unit.Ops.Add(Op.Suspend);
        unit.Ops.Add(Op.Add);
        unit.Ops.Add(Op.FinishModule);

        var units = new FastList<Unit>();
        units.Add(unit);
        var context = MinamoMachine.CreateExecutionContext(new UnitComposition(units));

        var suspended = MinamoMachine.Execute(context);

        Assert.Equal(TerminationReason.Suspended, suspended.Reason);
        var continuation = Assert.IsType<MinamoMachine.VmContinuation>(suspended.Continuation);

        var suspendedAgain = MinamoMachine.Resume(continuation);

        Assert.Equal(TerminationReason.Suspended, suspendedAgain.Reason);
        Assert.Same(continuation, suspendedAgain.Continuation);

        var completed = MinamoMachine.Resume(continuation);

        Assert.Equal(TerminationReason.Complete, completed.Reason);
        Assert.Equal(2L, ((MinamoInteger)completed.Value!).Value);
        Assert.Throws<InvalidOperationException>(() => MinamoMachine.Resume(continuation));
    }
}
