using Minamo.Compiler;
using Minamo.Hosting;
using Minamo.Linker;
using Xunit;

namespace Minamo.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class FileImportTests
{
    [Fact]
    public void RestrictsAndConfiguresFileImports() =>
        HostingScenarios.FileImportConfiguration();

    [Fact]
    public void ExecutesExplicitFilesAndSharesOperationResultContract() =>
        HostingScenarios.FileExecutionAndOperationResults();

    [Theory]
    [InlineData("import refs.math\nmath.value()")]
    [InlineData("import refs.math as mathematics\nmathematics.value()")]
    [InlineData("import value from refs.math\nvalue()")]
    [InlineData("import * from refs.math\nvalue()")]
    [InlineData("import refs.nested.math\nmath.value()")]
    [InlineData("import refs.lang\nlang.value()")]
    public void ResolvesDottedFileModulesWhenFullHostNameIsNotRegistered(string source)
    {
        using var files = new ModuleFiles();
        using var instance = files.CreateHost()
            .Module("math", module => module.Command("value", _ => 7))
            .CreateInstance();

        var result = instance.Execute(source);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Theory]
    [InlineData("import refs.math\nmath.value()")]
    [InlineData("import refs.math as mathematics\nmathematics.value()")]
    [InlineData("import value from refs.math\nvalue()")]
    [InlineData("import * from refs.math\nvalue()")]
    public void PrefersFullHostModuleNameOverMatchingFile(string source)
    {
        using var files = new ModuleFiles();
        using var instance = files.CreateHost()
            .Module("refs.math", module => module.Command("value", _ => 84))
            .Module("math", module => module.Command("value", _ => 7))
            .CreateInstance();

        var result = instance.Execute(source);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(84L, result.GetValue<long>());
    }

    [Fact]
    public void ImportsDottedHostModulesWithFileImportsDisabled()
    {
        using var instance = new MinamoHost()
            .Module("refs.math", module => module.Command("value", _ => 84))
            .DisableFileImports()
            .CreateInstance();

        var result = instance.Execute("import refs.math\nmath.value()");

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(84L, result.GetValue<long>());
    }

    private sealed class ModuleFiles : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(), "minamo-dotted-modules-" + Guid.NewGuid().ToString("N"));

        internal ModuleFiles()
        {
            Directory.CreateDirectory(Path.Combine(root, "refs", "nested"));
            File.WriteAllText(Path.Combine(root, "refs", "math.nami"), "func value() => 42");
            File.WriteAllText(Path.Combine(root, "refs", "nested", "math.nami"), "func value() => 42");
            File.WriteAllText(Path.Combine(root, "refs", "lang.nami"), "func value() => 42");
            File.WriteAllText(Path.Combine(root, "refs.math.nami"), "func value() => 7");
        }

        internal MinamoHost CreateHost()
        {
            var options = BuilderOptions.Default();
            return new MinamoHost(new() { BuilderOptions = options })
                .UseFileLookup(FileLookup.Restricted(options).AddPath(root).Build());
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
