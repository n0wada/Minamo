using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class StateAndSignalTests
{
    [Fact]
    public void SharesState() =>
        HostingScenarios.SharedState();

    [Fact]
    public void DeliversAndCleansUpSignals() =>
        HostingScenarios.Signals();
}
