using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class ExecutionLimitTests
{
    [Fact]
    public void EnforcesConfiguredExecutionLimits() =>
        HostingScenarios.ExecutionLimits();
}
