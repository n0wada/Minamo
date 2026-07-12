using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class ConfigurationTests
{
    [Fact]
    public void ValidatesHostConfiguration() =>
        HostingScenarios.HostConfigurationValidation();

    [Fact]
    public void PreservesPublicApiBoundary() =>
        HostingScenarios.PublicApiBoundary();

    [Fact]
    public void PreservesPublicApiNames() =>
        HostingScenarios.PublicApiNames();

    [Fact]
    public void InvokesHostCommandCallbacks() =>
        HostingScenarios.HostCommandCallbacks();

    [Fact]
    public void RunsCompiledProgramsThroughInstances() =>
        HostingScenarios.ProgramBackedInstances();

    [Fact]
    public void ResolvesInstanceEnvironmentNames() =>
        HostingScenarios.InstanceEnvironmentNames();

    [Fact]
    public void CanHideHostObjectFromScripts() =>
        HostingScenarios.HiddenHostObject();

    [Fact]
    public void SnapshotsConfigurationAndEnforcesOwnership() =>
        HostingScenarios.ConfigurationAndOwnership();
}
