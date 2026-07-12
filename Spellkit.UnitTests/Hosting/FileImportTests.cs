using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class FileImportTests
{
    [Fact]
    public void RestrictsAndConfiguresFileImports() =>
        HostingScenarios.FileImportConfiguration();

    [Fact]
    public void ExecutesExplicitFilesAndSharesOperationResultContract() =>
        HostingScenarios.FileExecutionAndOperationResults();
}
