using Minamo.Hosting;
using Xunit;

namespace Minamo.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
[Trait("Category", "Security")]
public sealed class CapabilityModeMatrixTests
{
    [Theory]
    [InlineData(MinamoCapabilityMode.Automatic, null, true)]
    [InlineData(MinamoCapabilityMode.Automatic, "other", false)]
    [InlineData(MinamoCapabilityMode.Automatic, "feature.use", true)]
    [InlineData(MinamoCapabilityMode.Automatic, "feature.*", true)]
    [InlineData(MinamoCapabilityMode.Automatic, "*", true)]
    [InlineData(MinamoCapabilityMode.Restricted, null, false)]
    [InlineData(MinamoCapabilityMode.Restricted, "other", false)]
    [InlineData(MinamoCapabilityMode.Restricted, "feature.use", true)]
    [InlineData(MinamoCapabilityMode.Restricted, "feature.*", true)]
    [InlineData(MinamoCapabilityMode.Restricted, "*", true)]
    [InlineData(MinamoCapabilityMode.Unrestricted, null, true)]
    [InlineData(MinamoCapabilityMode.Unrestricted, "other", true)]
    public void AppliesCapabilityModeToExecutionAndDiscovery(
        MinamoCapabilityMode mode,
        string? allowed,
        bool expected)
    {
        var host = new MinamoHost(new() { CapabilityMode = mode })
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
        using var instance = new MinamoHost()
            .AddCapabilities("feature.use")
            .CreateInstance();

        var result = instance.Execute("host.Capabilities");

        Assert.False(result.Success);
        Assert.Equal(MinamoFailureKind.Runtime, result.Failure?.Kind);
    }
}
