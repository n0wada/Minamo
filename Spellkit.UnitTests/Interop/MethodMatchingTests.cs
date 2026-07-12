using Xunit;

namespace Spellkit.UnitTesting.Interop;

[Trait("Suite", "Pipeline")]
public sealed class MethodMatchingTests
{
    [Fact]
    public void MatchesEveryRequestedParameterType() =>
        PipelineScenarios.InteropMethodMatching();
}
