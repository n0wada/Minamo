using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class ResourceLifetimeTests
{
    [Fact]
    public void EnforcesResourceLifetime() =>
        HostingScenarios.ResourceLifetime();

    [Fact]
    public void ReusesRegisteredDefinitionsAndCatalogsCommands() =>
        HostingScenarios.RegisteredResourceTypeAndCatalog();

    [Fact]
    public void RunsReleaseCallbacksExactlyOnce() =>
        HostingScenarios.ResourceReleaseCallbacks();

    [Fact]
    public void ReusesSharedHandlesAndPreventsScriptRelease() =>
        HostingScenarios.SharedResourceHandles();
}
