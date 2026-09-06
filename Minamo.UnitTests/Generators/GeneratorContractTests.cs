using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Minamo.Generators;
using Minamo.Hosting;
using System.Reflection;
using Xunit;

namespace Minamo.UnitTesting.Generators;

[Trait("Suite", "Generator")]
public sealed class GeneratorContractTests
{
    private const string SnapshotSource = """
        using Minamo.Hosting;
        using System.Linq;

        namespace GeneratorFixture;

        [MinamoModule("snapshot")]
        public static class SnapshotCommands
        {
            [MinamoCommand("echo", Description = "Echoes text.", Capability = "snapshot.read")]
            public static string Echo(string value, int count = 2) =>
                string.Concat(Enumerable.Repeat(value, count));

            [MinamoCommand]
            public static int? Maybe(int? value) => value;
        }
        """;

    [Fact]
    public void GeneratedSourceMatchesApprovedSnapshot()
    {
        var first = Generate(SnapshotSource);
        var second = Generate(SnapshotSource);

        AssertNoErrors(first.Diagnostics);
        AssertNoErrors(first.Compilation.GetDiagnostics());
        Assert.Equal(first.GeneratedSource, second.GeneratedSource);
        AssertSnapshot("SnapshotCommands.generated.cs", first.GeneratedSource);
    }

    [Fact]
    public void GeneratedAssemblyRegistersAndExecutesCommands()
    {
        const string source = """
            using Minamo.Hosting;
            using System.Linq;

            namespace GeneratorFixture;

            [MinamoModule("sample")]
            public sealed class SampleCommands
            {
                [MinamoProperty(Description = "Current count.", Capability = "sample.write")]
                public int Count { get; set; } = 1;

                [MinamoProperty]
                public string Name => "sample";

                [MinamoCommand("echo")]
                public string Echo(MinamoCommandContext context, string value, int count = 2) =>
                    string.Concat(Enumerable.Repeat(value, count));
            }
            """;

        var generated = Generate(source, "Minamo.GeneratorExecutionFixture");
        AssertNoErrors(generated.Diagnostics);
        AssertNoErrors(generated.Compilation.GetDiagnostics());

        using var stream = new MemoryStream();
        var emit = generated.Compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        var assembly = Assembly.Load(stream.ToArray());
        var commandsType = assembly.GetType("GeneratorFixture.SampleCommands")!;
        var extensionsType = assembly.GetType(
            "GeneratorFixture.SampleCommandsHostingExtensions")!;
        Assert.Null(extensionsType.GetMethod(
            "AddSampleCommands",
            BindingFlags.Public | BindingFlags.Static));
        var addCommands = extensionsType.GetMethod(
            "AddModule",
            BindingFlags.Public | BindingFlags.Static)!;
        var instance = Activator.CreateInstance(commandsType)!;
        var host = (MinamoHost)addCommands.Invoke(
            null,
            new[] { new MinamoHost(), instance })!;

        using var session = host.CreateInstance();
        var result = session.Execute("""
            import sample
            assert("oneone", sample.echo("one"))
            assert(true, isCallable(sample.echo))
            assert(1, sample.Count)
            sample.Count = 3
            assert(3, sample.Count)
            assert("sample", sample.Name)
            assert("sample.Count", host.Commands.Describe("sample.Count").Name)
            assert(nil, host.Commands.Describe("sample.set_Count"))
            """);

        Assert.True(result.Success, result.Failure?.Message);
        var readOnly = session.Execute("""
            import sample
            sample.Name = "changed"
            """);
        Assert.False(readOnly.Success);

        var restrictedHost = (MinamoHost)addCommands.Invoke(
            null,
            new object[]
            {
                new MinamoHost().AddCapabilities("other"),
                Activator.CreateInstance(commandsType)!
            })!;
        using var restricted = restrictedHost.CreateInstance();
        Assert.False(restricted.Execute("import sample\nsample.Count").Success);
        Assert.False(restricted.Execute("import sample\nsample.Count = 2").Success);
    }

    [Fact]
    public void DiagnosticsIncludeIdLocationAndMessageArguments()
    {
        const string source = """
            using Minamo.Hosting;

            namespace GeneratorFixture;

            [MinamoModule("")]
            public static class EmptyName { }

            [MinamoModule("duplicate")]
            public static class DuplicateCommands
            {
                [MinamoCommand("same")]
                public static void First() { }

                [MinamoCommand("same")]
                public static void Second() { }
            }

            [MinamoModule("invalid")]
            public static class InvalidParameter
            {
                [MinamoCommand]
                public static void Ref(ref int value) { }
            }

            [MinamoModule("invalid-property")]
            public sealed class InvalidProperty
            {
                [MinamoProperty]
                public int this[int index] => index;
            }
            """;

        var diagnostics = Errors(Generate(source));

        AssertDiagnostic(
            diagnostics,
            "MinamoH001",
            "EmptyName",
            "MinamoModule on 'EmptyName' requires a non-empty module name.");
        AssertDiagnostic(
            diagnostics,
            "MinamoH003",
            "Second",
            "Module 'duplicate' contains more than one command named 'same'.");
        AssertDiagnostic(
            diagnostics,
            "MinamoH004",
            "value",
            "Parameter 'value' on command 'Ref' is not supported: "
                + "ref, in, and out parameters are not supported");
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "MinamoH007"
            && diagnostic.GetMessage().Contains(
                "indexers are not supported",
                StringComparison.Ordinal));
    }

    [Fact]
    public void NullableValueAndReferenceTypesGenerateValidBindings()
    {
        const string source = """
            #nullable enable
            using Minamo.Hosting;

            namespace GeneratorFixture;

            [MinamoModule("nullable")]
            public static class NullableCommands
            {
                [MinamoCommand]
                public static int? Number(int? value) => value;

                [MinamoCommand]
                public static string? Text(string? value) => value;
            }
            """;

        var result = Generate(source);

        AssertNoErrors(result.Diagnostics);
        AssertNoErrors(result.Compilation.GetDiagnostics());
        Assert.Contains("result.HasValue", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains(
            "MinamoCommandParameter.Required<int?>(\"value\")",
            result.GeneratedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "MinamoCommandParameter.Required<string>(\"value\")",
            result.GeneratedSource,
            StringComparison.Ordinal);

        using var session = CreateInstance(result);
        var execution = session.Execute("""
            import nullable
            assert(nil, nullable.Number(nil))
            assert(nil, nullable.Text(nil))
            assert(42, nullable.Number(42))
            assert("text", nullable.Text("text"))
            """);
        Assert.True(execution.Success, execution.Failure?.Message);
    }

    [Fact]
    public void GenericModulesAndMethodsAreRejected()
    {
        const string source = """
            using Minamo.Hosting;

            namespace GeneratorFixture;

            [MinamoModule("generic-module")]
            public static class GenericModule<T>
            {
                [MinamoCommand]
                public static int Echo(int value) => value;
            }

            [MinamoModule("generic-method")]
            public static class GenericMethodModule
            {
                [MinamoCommand]
                public static T Echo<T>(T value) => value;
            }
            """;

        var diagnostics = Errors(Generate(source));

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "MinamoH002"
            && diagnostic.GetMessage().Contains(
                "generic module classes are not supported",
                StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "MinamoH002"
            && diagnostic.GetMessage().Contains(
                "generic methods are not supported",
                StringComparison.Ordinal));
    }

    [Fact]
    public void OverloadsRequireDistinctExposedCommandNames()
    {
        const string validSource = """
            using Minamo.Hosting;

            namespace GeneratorFixture;

            [MinamoModule("overloads")]
            public static class OverloadCommands
            {
                [MinamoCommand("integer")]
                public static int Echo(int value) => value;

                [MinamoCommand("text")]
                public static string Echo(string value) => value;
            }
            """;
        const string invalidSource = """
            using Minamo.Hosting;

            namespace GeneratorFixture;

            [MinamoModule("overloads")]
            public static class OverloadCommands
            {
                [MinamoCommand("echo")]
                public static int First(int value) => value;

                [MinamoCommand("echo")]
                public static string Second(string value) => value;
            }
            """;

        var valid = Generate(validSource);
        var invalid = Errors(Generate(invalidSource));

        AssertNoErrors(valid.Diagnostics);
        AssertNoErrors(valid.Compilation.GetDiagnostics());
        Assert.Contains("RawCommand(\"integer\"", valid.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("RawCommand(\"text\"", valid.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains(invalid, diagnostic => diagnostic.Id == "MinamoH003");
    }

    [Fact]
    public void TaskAndValueTaskCommandsAreGenerated()
    {
        const string source = """
            using Minamo.Hosting;
            using System.Threading.Tasks;

            namespace GeneratorFixture;

            [MinamoModule("async")]
            public static class AsyncCommands
            {
                [MinamoCommand]
                public static Task<int> TaskCommand() => Task.FromResult(1);
            }

            [MinamoModule("value-task")]
            public static class ValueTaskCommands
            {
                [MinamoCommand]
                public static ValueTask<int> ValueTaskCommand() => ValueTask.FromResult(1);
            }
            """;

        var result = Generate(source);

        AssertNoErrors(result.Diagnostics);
        Assert.Contains("FromAwaitable", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueTaskTypeMethodsAreGenerated()
    {
        const string source = """
            using Minamo.Codegen;
            using Minamo.Runtime.Types;
            using System.Threading.Tasks;

            namespace GeneratorFixture;

            [MinamoType]
            public sealed partial class AsyncTypeInfo : MinamoForeignTypeInfo
            {
                public override string ReflectedTypeName => "Async";

                [MinamoStaticMethod]
                public static ValueTask<int> Value() => ValueTask.FromResult(1);
            }
            """;

        var result = Generate(source);

        AssertNoErrors(result.Diagnostics);
        AssertNoErrors(result.Compilation.GetDiagnostics());
        Assert.Contains("FromAwaitable", result.GeneratedSource, StringComparison.Ordinal);
    }

    private static GeneratorResult Generate(
        string source,
        string assemblyName = "Minamo.GeneratorFixture")
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            parseOptions,
            path: "GeneratorFixture.cs");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            References(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[]
            {
                new MinamoCommandGenerator().AsSourceGenerator(),
                new MinamoTypeGenerator().AsSourceGenerator()
            },
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics);
        var generatedSources = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .OrderBy(result => result.HintName, StringComparer.Ordinal)
            .Select(result => result.SourceText.ToString())
            .ToArray();

        return new(
            outputCompilation,
            diagnostics,
            string.Join("\n", generatedSources));
    }

    private static MinamoInstance CreateInstance(GeneratorResult generated, object? hostContext = null)
    {
        AssertNoErrors(generated.Diagnostics);
        AssertNoErrors(generated.Compilation.GetDiagnostics());

        using var stream = new MemoryStream();
        var emit = generated.Compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        var assembly = Assembly.Load(stream.ToArray());
        var extensionType = assembly.GetTypes().Single(type =>
            type.IsAbstract
            && type.IsSealed
            && type.Name.EndsWith("HostingExtensions", StringComparison.Ordinal));
        var addCommands = extensionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name.StartsWith("Add", StringComparison.Ordinal));
        var host = (MinamoHost)addCommands.Invoke(null, new object[] { new MinamoHost() })!;
        return host.CreateInstance(hostContext);
    }

    private static IReadOnlyList<Diagnostic> Errors(GeneratorResult result) =>
        result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .OrderBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<MetadataReference> References()
    {
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var paths = trustedAssemblies.Split(Path.PathSeparator)
            .Append(typeof(MinamoHost).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return paths.Select(path => MetadataReference.CreateFromFile(path));
    }

    private static void AssertDiagnostic(
        IEnumerable<Diagnostic> diagnostics,
        string id,
        string sourceText,
        string message)
    {
        var diagnostic = Assert.Single(diagnostics, item => item.Id == id);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Equal("GeneratorFixture.cs", diagnostic.Location.SourceTree?.FilePath);
        Assert.Equal(
            sourceText,
            diagnostic.Location.SourceTree?.GetText()
                .GetSubText(diagnostic.Location.SourceSpan)
                .ToString());
        Assert.Equal(message, diagnostic.GetMessage());
        Assert.True(diagnostic.Location.GetLineSpan().StartLinePosition.Line >= 0);
    }

    private static void AssertNoErrors(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
    }

    private static void AssertSnapshot(string name, string actual)
    {
        var path = Path.GetFullPath(Path.Combine(
            TestRepository.Root,
            "Minamo.UnitTests",
            "Generators",
            "Snapshots",
            name));
        actual = Normalize(actual);

        if (string.Equals(
            Environment.GetEnvironmentVariable("MINAMO_UPDATE_SNAPSHOTS"),
            "1",
            StringComparison.Ordinal))
        {
            File.WriteAllText(path, actual);
        }

        Assert.Equal(Normalize(File.ReadAllText(path)), actual);
    }

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";

    private sealed record GeneratorResult(
        Compilation Compilation,
        IEnumerable<Diagnostic> Diagnostics,
        string GeneratedSource);
}
