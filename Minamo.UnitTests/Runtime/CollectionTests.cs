using Xunit;

namespace Minamo.UnitTesting.Runtime;

[Trait("Suite", "Pipeline")]
public sealed class CollectionTests
{
    [Fact]
    public void PreservesMutationAndConversionContracts() =>
        PipelineScenarios.CollectionMutationContracts();
}
