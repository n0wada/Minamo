using Minamo.Hosting;
using Xunit;

namespace Minamo.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class RegistryTests
{
    [Fact]
    public void SharesRegistry() =>
        HostingScenarios.Registry();
}
