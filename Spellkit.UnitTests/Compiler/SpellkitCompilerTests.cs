using Spellkit.Compiler;
using Spellkit.Linker;
using Spellkit.Parser;
using Xunit;

namespace Spellkit.UnitTesting.Compiler;

public sealed class SpellkitCompilerTests
{
    [Fact]
    public void CompilesSourceAndCodeModelThroughFacade()
    {
        var sourceResult = SpellkitCompiler.Compile("let answer = 42");
        var parsed = SpellkitParser.Parse("let answer = 42").GetValueOrThrow();
        var modelResult = SpellkitCompiler.Compile(parsed);

        Assert.True(sourceResult.Success);
        Assert.NotNull(sourceResult.Value);
        Assert.True(modelResult.Success);
        Assert.NotNull(modelResult.Value);
    }

    [Fact]
    public void UsesLookupAsTheSingleOptionsOwner()
    {
        var options = new BuilderOptions { Debug = true };
        var lookup = FileLookup.Restricted(options).Build();
        var linker = new SpellkitLinker(lookup);

        Assert.Same(options, linker.BuilderOptions);
        Assert.DoesNotContain(
            typeof(SpellkitLinker).GetConstructors(),
            constructor => constructor.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(BuilderOptions)));
    }

    [Fact]
    public void RequiresExplicitLookupForImports()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "spellkit-compiler-" + Guid.NewGuid().ToString("N"));
        var mainPath = Path.Combine(root, "main.kit");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "helper.kit"), "func value() => 42");
            File.WriteAllText(mainPath, "import helper\nhelper.value()");

            var restricted = SpellkitCompiler.CompileFile(mainPath);
            var options = BuilderOptions.Default();
            var lookup = FileLookup.Restricted(options).AddPath(root).Build();
            var allowed = SpellkitCompiler.CompileFile(mainPath, lookup);

            Assert.False(restricted.Success);
            Assert.True(allowed.Success);
            Assert.NotNull(allowed.Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
