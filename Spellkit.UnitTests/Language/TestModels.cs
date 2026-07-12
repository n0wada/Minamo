using Spellkit.Parser.Model;

namespace Spellkit.UnitTesting;

[Flags]
internal enum TestFormatFlags
{
    None = 0,
    OnlyFailed = 1,
    Markdown = 2
}

internal sealed class TestBlockInfo
{
    public TestBlockInfo(string fileName) => FileName = fileName;

    public string FileName { get; }

    public RegionSyntax? Block { get; init; }

    public string? Error { get; init; }
}

internal sealed class TestReport
{
    public required string[] TestFiles { get; init; }

    public List<BuildMessage>? BuildWarnings { get; set; }

    public List<TestResult> Results { get; } = new();
}

internal sealed class TestResult
{
    public string? Name { get; init; }

    public required string FileName { get; init; }

    public string? Error { get; init; }

    public string? StackTrace { get; init; }

    public string? Expected { get; init; }

    public string? Actual { get; init; }

    public string? ReproductionCommand { get; init; }

    public TimeSpan Duration { get; init; }

    public bool TimedOut { get; init; }
}
