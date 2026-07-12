namespace Spellkit.UnitTesting;

internal sealed class TestOptions
{
    public required string TestPath { get; init; }

    public string? OutputPath { get; init; }

    public bool ShowOnlyFailures { get; init; } = true;

    public bool UseMarkdown { get; init; } = true;

    public string? Region { get; init; }

    public TimeSpan TestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public static TestOptions Parse(string[] args)
    {
        string? testPath = null;
        string? outputPath = null;
        string? region = null;
        var showOnlyFailures = true;
        var useMarkdown = true;
        var timeout = TimeSpan.FromSeconds(10);
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output" when i + 1 < args.Length:
                    outputPath = args[++i];
                    break;
                case "--all":
                    showOnlyFailures = false;
                    break;
                case "--text":
                    useMarkdown = false;
                    break;
                case "--region" when i + 1 < args.Length:
                    region = args[++i];
                    break;
                case "--timeout-seconds" when i + 1 < args.Length:
                    if (!double.TryParse(
                        args[++i],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var seconds)
                        || !double.IsFinite(seconds)
                        || seconds <= 0)
                    {
                        throw new ArgumentException("Test timeout must be a positive number.");
                    }
                    timeout = TimeSpan.FromSeconds(seconds);
                    break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown test option: {args[i]}");
                    }

                    if (testPath is not null)
                    {
                        throw new ArgumentException("Only one test path can be specified.");
                    }

                    testPath = args[i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(testPath))
        {
            throw new ArgumentException("Test path not specified.");
        }

        return new TestOptions
        {
            TestPath = testPath,
            OutputPath = outputPath,
            ShowOnlyFailures = showOnlyFailures,
            UseMarkdown = useMarkdown,
            Region = region,
            TestTimeout = timeout
        };
    }
}
