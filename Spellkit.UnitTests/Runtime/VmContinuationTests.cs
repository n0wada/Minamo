using Spellkit.Compiler;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using Xunit;

namespace Spellkit.UnitTesting.Runtime;

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
        var context = SpkMachine.CreateExecutionContext(new UnitComposition(units));

        var suspended = SpkMachine.Execute(context);

        Assert.Equal(TerminationReason.Suspended, suspended.Reason);
        var continuation = Assert.IsType<SpkMachine.VmContinuation>(suspended.Continuation);

        var suspendedAgain = SpkMachine.Resume(continuation);

        Assert.Equal(TerminationReason.Suspended, suspendedAgain.Reason);
        Assert.Same(continuation, suspendedAgain.Continuation);

        var completed = SpkMachine.Resume(continuation);

        Assert.Equal(TerminationReason.Complete, completed.Reason);
        Assert.Equal(2L, ((SpkInteger)completed.Value!).Value);
        Assert.Throws<InvalidOperationException>(() => SpkMachine.Resume(continuation));
    }
}
