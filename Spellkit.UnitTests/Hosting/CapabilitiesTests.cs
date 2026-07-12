using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class CapabilitiesTests
{
    [Fact]
    public void FiltersCommandsAndCatalogEntries() =>
        HostingScenarios.CapabilityAndCatalog();

    [Fact]
    public void ProtectsState() =>
        HostingScenarios.StateCapabilities();
}
