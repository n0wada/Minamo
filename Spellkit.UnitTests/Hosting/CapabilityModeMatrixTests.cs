using Spellkit.Hosting;
using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
[Trait("Category", "Security")]
public sealed class CapabilityModeMatrixTests
{
    [Theory]
    [InlineData(SpellkitCapabilityMode.Automatic, null, true)]
    [InlineData(SpellkitCapabilityMode.Automatic, "other", false)]
    [InlineData(SpellkitCapabilityMode.Automatic, "feature.use", true)]
    [InlineData(SpellkitCapabilityMode.Automatic, "feature.*", true)]
    [InlineData(SpellkitCapabilityMode.Automatic, "*", true)]
    [InlineData(SpellkitCapabilityMode.Restricted, null, false)]
    [InlineData(SpellkitCapabilityMode.Restricted, "other", false)]
    [InlineData(SpellkitCapabilityMode.Restricted, "feature.use", true)]
    [InlineData(SpellkitCapabilityMode.Restricted, "feature.*", true)]
    [InlineData(SpellkitCapabilityMode.Restricted, "*", true)]
    [InlineData(SpellkitCapabilityMode.Unrestricted, null, true)]
    [InlineData(SpellkitCapabilityMode.Unrestricted, "other", true)]
    public void AppliesCapabilityModeToExecutionAndDiscovery(
        SpellkitCapabilityMode mode,
        string? allowed,
        bool expected)
    {
        var host = new SpellkitHost(new() { CapabilityMode = mode })
            .Module("feature", module => module.Command(
                "Use",
                null,
                "feature.use",
                _ => 42));
        if (allowed is not null)
        {
            host.AddCapabilities(allowed);
        }

        using var instance = host.CreateInstance();
        var result = instance.Execute("import feature\nfeature.Use()");
        var catalogEntry = instance.Environment.Commands.Describe("feature.Use");

        Assert.Equal(expected, result.Success);
        Assert.Equal(expected, catalogEntry is not null);
        Assert.Equal(expected, instance.Environment.Capabilities.Allows("feature.use"));
    }

    [Fact]
    public void DoesNotExposeCapabilityPolicyToScripts()
    {
        using var instance = new SpellkitHost()
            .AddCapabilities("feature.use")
            .CreateInstance();

        var result = instance.Execute("host.Capabilities");

        Assert.False(result.Success);
        Assert.Equal(SpellkitFailureKind.Runtime, result.Failure?.Kind);
    }
}
