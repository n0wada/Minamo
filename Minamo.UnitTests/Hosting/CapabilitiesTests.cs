using Xunit;

namespace Minamo.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class CapabilitiesTests
{
    [Fact]
    public void FiltersCommandsAndCatalogEntries() =>
        HostingScenarios.CapabilityAndCatalog();

    [Fact]
    public void ProtectsRegistry() =>
        HostingScenarios.RegistryCapabilities();
}
