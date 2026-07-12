using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class ResultContractTests
{
    [Fact]
    public void ClassifiesFailuresAndSanitizesHostErrors() =>
        HostingScenarios.ResultContracts();
}
