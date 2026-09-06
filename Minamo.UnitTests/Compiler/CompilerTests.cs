using Xunit;

namespace Minamo.UnitTesting.Compiler;

[Trait("Suite", "Pipeline")]
public sealed class CompilerTests
{
    [Fact]
    public void CompilesAndExecutesPipelineSource() => PipelineScenarios.CompilerAndRuntime();
}
