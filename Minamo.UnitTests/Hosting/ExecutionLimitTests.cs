using Xunit;

namespace Minamo.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class ExecutionLimitTests
{
    [Fact]
    public void EnforcesConfiguredExecutionLimits() =>
        HostingScenarios.ExecutionLimits();
}
