using Xunit;
using Spellkit.Hosting;
using Spellkit.Parser;
using Spellkit.Parser.Model;

namespace Spellkit.UnitTesting.Parser;

[Trait("Suite", "Pipeline")]
public sealed class ParserTests
{
    [Fact]
    public void ReportsInvalidSyntax() => PipelineScenarios.ParserDiagnostics();

    [Theory]
    [InlineData("1 <<< 2")]
    [InlineData("4 >>> 1")]
    [InlineData("1 &&& 1")]
    [InlineData("1 ||| 2")]
    [InlineData("1 ^^^ 2")]
    [InlineData("~~~1")]
    [InlineData("0^2..10")]
    public void RejectsRemovedBitwiseOperators(string source)
    {
        var result = SpkParser.Parse(source, "<removed-bitwise-operator>");

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("1 << 2")]
    [InlineData("4 >> 1")]
    public void ParsesOverloadableShiftOperators(string source)
    {
        var result = SpkParser.Parse(source, "<overloadable-shift-operator>");

        Assert.True(result.Success);
    }

    [Fact]
    public void ParsesSourceStringAndPreservesSourceName()
    {
        var successful = SpkParser.Parse("let value = 42");
        var failed = SpkParser.Parse("let =", "generated.kit");

        Assert.True(successful.Success);
        Assert.False(failed.Success);
        Assert.All(failed.Errors, error => Assert.Equal("generated.kit", error.File));
    }

    [Fact]
    public void ParsesFileAndPreservesPath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "spellkit-parser-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "invalid.kit");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(path, "let =");

            var result = SpkParser.ParseFile(path);

            Assert.False(result.Success);
            Assert.All(
                result.Errors,
                error => Assert.Equal(path.Replace('\\', '/'), error.File));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadsSourceBufferFromFileAsynchronously()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "spellkit-parser-async-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "source.kit");
        Directory.CreateDirectory(directory);

        try
        {
            await File.WriteAllTextAsync(path, "let value = 42");

            var buffer = await SourceBuffer.FromFileAsync(path);
            var result = SpkParser.Parse(buffer);

            Assert.True(result.Success);
            Assert.Equal(path.Replace('\\', '/'), buffer.FileName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelsAsynchronousSourceBufferRead()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SourceBuffer.FromFileAsync("unused.kit", cancellation.Token));
    }

    [Fact]
    public void PreservesParameterizedTypeHintsAndExpandsNullableSyntax()
    {
        var result = SpkParser.Parse(SourceBuffer.FromString(
            "let value: Result<List<String?>, Error>? = nil",
            "<type-hints>"));

        Assert.True(result.Success);
        var binding = Assert.IsType<BindingSyntax>(Assert.Single(result.Value!.Root.Nodes));
        var resultType = Assert.IsType<TypeAnnotation>(binding.TypeAnnotation);

        Assert.Equal("Result<List<String | Nil>, Error> | Nil", resultType.ToString());
        Assert.Equal("Result", resultType.Qualident.Local);
        Assert.Equal(2, resultType.TypeArguments.Count);
        Assert.Equal("List<String | Nil>", resultType.TypeArguments[0].ToString());
        Assert.Equal("String | Nil", resultType.TypeArguments[0].TypeArguments[0].ToString());
        Assert.Equal("Error", resultType.TypeArguments[1].ToString());
        Assert.Equal("Nil", resultType.Next!.Qualident.Local);
        Assert.Equal(
            new[] { "Result", "Nil" },
            resultType.Select(type => type.Local).ToArray());
    }

    [Fact]
    public void AcceptsNullableAndParameterizedHintsAcrossDeclarations()
    {
        const string source = """
            func load(input: String?): Result<String> => input
            struct Box { value: Result<String?> }
            let current: String? = nil
            """;

        var result = SpkParser.Parse(SourceBuffer.FromString(source, "<type-hint-forms>"));

        Assert.True(result.Success);
    }

    [Fact]
    public void IgnoresParameterizedHintArgumentsDuringExecution()
    {
        using var session = new SpellkitHost().CreateInstance();

        var result = session.Execute("""
            func echo(value: String?): Result<String> => value
            assert(42, echo(42))
            """);

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("let value: Result<> = nil")]
    [InlineData("let value: Result<String,> = nil")]
    public void RejectsEmptyTypeHintArguments(string source)
    {
        var result = SpkParser.Parse(SourceBuffer.FromString(source, "<invalid-type-hints>"));

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("struct Legacy { String value }")]
    [InlineData("struct Legacy { let value }")]
    [InlineData("enum Legacy { Value(String value) }")]
    public void RejectsLegacyFieldSyntax(string source)
    {
        var result = SpkParser.Parse(SourceBuffer.FromString(source, "<legacy-fields>"));

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("let value =")]
    [InlineData("func call(")]
    [InlineData("if true {")]
    [InlineData("struct Value {")]
    [InlineData("\"")]
    [InlineData("/*")]
    public void ReportsTruncatedInputWithoutThrowing(string source)
    {
        var result = SpkParser.Parse(SourceBuffer.FromString(source, "<truncated>"));

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, error => Assert.Equal("<truncated>", error.File));
    }

    [Fact]
    public void TracksLocationsAcrossMixedLineEndings()
    {
        var result = SpkParser.Parse(SourceBuffer.FromString(
            "let first = 1\r\nlet second = 2\nlet =",
            "mixed-lines.kit"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Line == 3);
    }
}
