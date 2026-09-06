using Minamo.Compiler;
using Minamo.Linker;
using Minamo.Parser;
using Xunit;

namespace Minamo.UnitTesting.Compiler;

public sealed class MinamoCompilerTests
{
    [Fact]
    public void CompilesSourceAndCodeModelThroughFacade()
    {
        var sourceResult = MinamoCompiler.Compile("let answer = 42");
        var parsed = MinamoParser.Parse("let answer = 42").GetValueOrThrow();
        var modelResult = MinamoCompiler.Compile(parsed);

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
        var linker = new MinamoLinker(lookup);

        Assert.Same(options, linker.BuilderOptions);
        Assert.DoesNotContain(
            typeof(MinamoLinker).GetConstructors(),
            constructor => constructor.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(BuilderOptions)));
    }

    [Fact]
    public void RequiresExplicitLookupForImports()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "minamo-compiler-" + Guid.NewGuid().ToString("N"));
        var mainPath = Path.Combine(root, "main.nami");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "helper.nami"), "func value() => 42");
            File.WriteAllText(mainPath, "import helper\nhelper.value()");

            var restricted = MinamoCompiler.CompileFile(mainPath);
            var options = BuilderOptions.Default();
            var lookup = FileLookup.Restricted(options).AddPath(root).Build();
            var allowed = MinamoCompiler.CompileFile(mainPath, lookup);

            Assert.False(restricted.Success);
            Assert.True(allowed.Success);
            Assert.NotNull(allowed.Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvesExplicitRelativeImportPaths()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "minamo-compiler-" + Guid.NewGuid().ToString("N"));
        var modules = Path.Combine(root, "modules");
        var mainPath = Path.Combine(root, "main.nami");
        Directory.CreateDirectory(modules);

        try
        {
            File.WriteAllText(Path.Combine(modules, "helper.nami"), "func value() => 42");
            File.WriteAllText(mainPath, "import ./modules/helper\nhelper.value()");

            var options = BuilderOptions.Default();
            var lookup = FileLookup.Restricted(options).AddPath(root).Build();
            var result = MinamoCompiler.CompileFile(mainPath, lookup);

            Assert.True(result.Success);
            Assert.NotNull(result.Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RequiresMixinAndTraitMembersByEndOfSourceFile()
    {
        var incompleteDisposable = MinamoCompiler.Compile("""
            struct Resource {}
            impl Resource with Disposable {}
            """);
        var incompleteNumber = MinamoCompiler.Compile("""
            struct Amount {}
            impl Amount with Number {}
            """);
        var incompleteTrait = MinamoCompiler.Compile("""
            trait Billable {
                func Name()
                func Price()
            }
            impl Billable {
                func Price() => 0
            }
            struct Subscription {}
            impl Subscription with Billable {}
            """);
        var complete = MinamoCompiler.Compile("""
            trait Billable {
                func Name()
                func Describe()
            }
            impl Billable {
                func Describe() => this.Name()
            }
            struct Subscription { customer }
            impl Subscription with Billable {}
            func Subscription.Name() => this.customer
            """);

        Assert.False(incompleteDisposable.Success);
        Assert.Contains(incompleteDisposable.Errors, error =>
            error.Code == (int)CompilerError.TraitMemberNotImplemented);
        Assert.Equal(7, incompleteNumber.Errors.Count(error =>
            error.Code == (int)CompilerError.TraitMemberNotImplemented));
        Assert.False(incompleteTrait.Success);
        Assert.Contains(incompleteTrait.Errors, error =>
            error.Code == (int)CompilerError.TraitMemberNotImplemented);
        Assert.True(complete.Success);
    }
}
