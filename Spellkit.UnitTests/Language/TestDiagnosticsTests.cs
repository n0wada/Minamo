using Spellkit.Compiler;
using Xunit;

namespace Spellkit.UnitTesting.Language;

[Trait("Suite", "Language")]
public sealed class TestDiagnosticsTests
{
    [Fact]
    public void ParsesRegionAndPerTestTimeout()
    {
        var options = TestOptions.Parse(
            ["tests", "--region", "one case", "--timeout-seconds", "1.25"]);

        Assert.Equal("one case", options.Region);
        Assert.Equal(TimeSpan.FromSeconds(1.25), options.TestTimeout);
    }

    [Fact]
    public void FormatsStructuredFailureDetails()
    {
        var report = new TestReport { TestFiles = ["sample.kit"] };
        report.Results.Add(new TestResult
        {
            Name = "failure",
            FileName = "sample.kit",
            Error = "Assertion failed.",
            StackTrace = "Spellkit.Runtime.SpkRuntimeException: Assertion failed.",
            Expected = "1",
            Actual = "2",
            ReproductionCommand = @".\scripts\test-local.ps1 -Region ""failure""",
            Duration = TimeSpan.FromMilliseconds(12),
            TimedOut = true
        });

        var text = TestFormatter.Format(report, TestFormatFlags.None);

        Assert.Contains("File: sample.kit", text);
        Assert.Contains("Region: failure", text);
        Assert.Contains("Duration: 12.0 ms", text);
        Assert.Contains("Expected: 1", text);
        Assert.Contains("Actual: 2", text);
        Assert.Contains("Reproduce:", text);
        Assert.Contains("Stack trace:", text);
        Assert.Contains("Timed out: yes", text);
    }

    [Fact]
    public void CapturesAssertionDetailsAndReproductionCommand()
    {
        using var file = TemporaryLanguageTest.Create(
            """
            #region "failing assertion"
                assert(1, 2)
            #endregion
            """);
        var runner = CreateRunner(file.Path, "failing assertion", TimeSpan.FromSeconds(1));

        var result = Assert.Single(runner.RunWithReport([file.Path]).Results);

        Assert.NotNull(result.Error);
        Assert.NotNull(result.StackTrace);
        Assert.Equal("\"1\" :: Integer", result.Expected);
        Assert.Equal("\"2\" :: Integer", result.Actual);
        Assert.True(result.Duration > TimeSpan.Zero);
        Assert.Contains("-Region \"failing assertion\"", result.ReproductionCommand);
    }

    [Fact]
    public void StopsAnIndividualRegionAtItsTimeout()
    {
        using var file = TemporaryLanguageTest.Create(
            """
            #region "never finishes"
                while true {}
            #endregion
            """);
        var runner = CreateRunner(file.Path, "never finishes", TimeSpan.FromMilliseconds(10));

        var result = Assert.Single(runner.RunWithReport([file.Path]).Results);

        Assert.True(result.TimedOut);
        Assert.Contains("time limit", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Duration < TimeSpan.FromSeconds(5));
    }

    private static TestRunner CreateRunner(string path, string region, TimeSpan timeout) =>
        new(new TestOptions
        {
            TestPath = path,
            Region = region,
            TestTimeout = timeout,
            UseMarkdown = false
        });

    private sealed class TemporaryLanguageTest : IDisposable
    {
        private TemporaryLanguageTest(string path) => Path = path;

        public string Path { get; }

        public static TemporaryLanguageTest Create(string source)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"spellkit-language-{Guid.NewGuid():N}.kit");
            File.WriteAllText(path, source);
            return new TemporaryLanguageTest(path);
        }

        public void Dispose() => File.Delete(Path);
    }
}
