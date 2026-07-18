using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Spellkit.Generators;
using Spellkit.Hosting;
using System.Reflection;
using Xunit;

namespace Spellkit.UnitTesting.Generators;

[Trait("Suite", "Generator")]
public sealed class GeneratorContractTests
{
    private const string SnapshotSource = """
        using Spellkit.Hosting;
        using System.Linq;

        namespace GeneratorFixture;

        [SpellkitModule("snapshot")]
        public static class SnapshotCommands
        {
            [SpellkitCommand("echo", Description = "Echoes text.", Capability = "snapshot.read")]
            public static string Echo(string value, int count = 2) =>
                string.Concat(Enumerable.Repeat(value, count));

            [SpellkitCommand]
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
            using Spellkit.Hosting;
            using System.Linq;

            namespace GeneratorFixture;

            [SpellkitModule("sample")]
            public sealed class SampleCommands
            {
                [SpellkitProperty(Description = "Current count.", Capability = "sample.write")]
                public int Count { get; set; } = 1;

                [SpellkitProperty]
                public string Name => "sample";

                [SpellkitCommand("echo")]
                public string Echo(SpellkitCommandContext context, string value, int count = 2) =>
                    string.Concat(Enumerable.Repeat(value, count));
            }
            """;

        var generated = Generate(source, "Spellkit.GeneratorExecutionFixture");
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
        var host = (SpellkitHost)addCommands.Invoke(
            null,
            new[] { new SpellkitHost(), instance })!;

        using var session = host.CreateInstance();
        var result = session.Execute("""
            import sample
            assert("oneone", sample.echo("one"))
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

        var restrictedHost = (SpellkitHost)addCommands.Invoke(
            null,
            new object[]
            {
                new SpellkitHost().AddCapabilities("other"),
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
            using Spellkit.Hosting;

            namespace GeneratorFixture;

            [SpellkitModule("")]
            public static class EmptyName { }

            [SpellkitModule("duplicate")]
            public static class DuplicateCommands
            {
                [SpellkitCommand("same")]
                public static void First() { }

                [SpellkitCommand("same")]
                public static void Second() { }
            }

            [SpellkitModule("invalid")]
            public static class InvalidParameter
            {
                [SpellkitCommand]
                public static void Ref(ref int value) { }
            }

            [SpellkitModule("invalid-property")]
            public sealed class InvalidProperty
            {
                [SpellkitProperty]
                public int this[int index] => index;
            }
            """;

        var diagnostics = Errors(Generate(source));

        AssertDiagnostic(
            diagnostics,
            "SPKH001",
            "EmptyName",
            "SpellkitModule on 'EmptyName' requires a non-empty module name.");
        AssertDiagnostic(
            diagnostics,
            "SPKH003",
            "Second",
            "Module 'duplicate' contains more than one command named 'same'.");
        AssertDiagnostic(
            diagnostics,
            "SPKH004",
            "value",
            "Parameter 'value' on command 'Ref' is not supported: "
                + "ref, in, and out parameters are not supported");
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "SPKH007"
            && diagnostic.GetMessage().Contains(
                "indexers are not supported",
                StringComparison.Ordinal));
    }

    [Fact]
    public void NullableValueAndReferenceTypesGenerateValidBindings()
    {
        const string source = """
            #nullable enable
            using Spellkit.Hosting;

            namespace GeneratorFixture;

            [SpellkitModule("nullable")]
            public static class NullableCommands
            {
                [SpellkitCommand]
                public static int? Number(int? value) => value;

                [SpellkitCommand]
                public static string? Text(string? value) => value;
            }
            """;

        var result = Generate(source);

        AssertNoErrors(result.Diagnostics);
        AssertNoErrors(result.Compilation.GetDiagnostics());
        Assert.Contains("result.HasValue", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains(
            "SpellkitCommandParameter.Required<int?>(\"value\")",
            result.GeneratedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "SpellkitCommandParameter.Required<string>(\"value\")",
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
            using Spellkit.Hosting;

            namespace GeneratorFixture;

            [SpellkitModule("generic-module")]
            public static class GenericModule<T>
            {
                [SpellkitCommand]
                public static int Echo(int value) => value;
            }

            [SpellkitModule("generic-method")]
            public static class GenericMethodModule
            {
                [SpellkitCommand]
                public static T Echo<T>(T value) => value;
            }
            """;

        var diagnostics = Errors(Generate(source));

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "SPKH002"
            && diagnostic.GetMessage().Contains(
                "generic module classes are not supported",
                StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "SPKH002"
            && diagnostic.GetMessage().Contains(
                "generic methods are not supported",
                StringComparison.Ordinal));
    }

    [Fact]
    public void OverloadsRequireDistinctExposedCommandNames()
    {
        const string validSource = """
            using Spellkit.Hosting;

            namespace GeneratorFixture;

            [SpellkitModule("overloads")]
            public static class OverloadCommands
            {
                [SpellkitCommand("integer")]
                public static int Echo(int value) => value;

                [SpellkitCommand("text")]
                public static string Echo(string value) => value;
            }
            """;
        const string invalidSource = """
            using Spellkit.Hosting;

            namespace GeneratorFixture;

            [SpellkitModule("overloads")]
            public static class OverloadCommands
            {
                [SpellkitCommand("echo")]
                public static int First(int value) => value;

                [SpellkitCommand("echo")]
                public static string Second(string value) => value;
            }
            """;

        var valid = Generate(validSource);
        var invalid = Errors(Generate(invalidSource));

        AssertNoErrors(valid.Diagnostics);
        AssertNoErrors(valid.Compilation.GetDiagnostics());
        Assert.Contains("RawCommand(\"integer\"", valid.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("RawCommand(\"text\"", valid.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains(invalid, diagnostic => diagnostic.Id == "SPKH003");
    }

    [Fact]
    public void TaskAndValueTaskCommandsAreGenerated()
    {
        const string source = """
            using Spellkit.Hosting;
            using System.Threading.Tasks;

            namespace GeneratorFixture;

            [SpellkitModule("async")]
            public static class AsyncCommands
            {
                [SpellkitCommand]
                public static Task<int> TaskCommand() => Task.FromResult(1);
            }

            [SpellkitModule("value-task")]
            public static class ValueTaskCommands
            {
                [SpellkitCommand]
                public static ValueTask<int> ValueTaskCommand() => ValueTask.FromResult(1);
            }
            """;

        var result = Generate(source);

        AssertNoErrors(result.Diagnostics);
        Assert.Contains("FromAwaitable", result.GeneratedSource, StringComparison.Ordinal);
    }

    private static GeneratorResult Generate(
        string source,
        string assemblyName = "Spellkit.GeneratorFixture")
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
            new[] { new SpellkitCommandGenerator().AsSourceGenerator() },
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

    private static SpellkitInstance CreateInstance(GeneratorResult generated, object? hostContext = null)
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
        var host = (SpellkitHost)addCommands.Invoke(null, new object[] { new SpellkitHost() })!;
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
            .Append(typeof(SpellkitHost).Assembly.Location)
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
            "Spellkit.UnitTests",
            "Generators",
            "Snapshots",
            name));
        actual = Normalize(actual);

        if (string.Equals(
            Environment.GetEnvironmentVariable("SPELLKIT_UPDATE_SNAPSHOTS"),
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
