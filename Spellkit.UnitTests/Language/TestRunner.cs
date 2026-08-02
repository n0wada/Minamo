using Spellkit.Compiler;
using Spellkit.Hosting;
using Spellkit.Library;
using Spellkit.Linker;
using Spellkit.Parser;
using Spellkit.Parser.Model;
using Spellkit.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spellkit.UnitTesting;

internal sealed class TestRunner
{
    private static readonly Regex AssertionFailure = new(
        @"^Assertion failed\. Expected (?<expected>.+), got (?<actual>.+)\.$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly object WorkingDirectorySync = new();
    private readonly TestOptions options;
    private readonly TextWriter stdOut;

    public TestRunner(TestOptions options) =>
        (this.options, stdOut) = (options, Console.Out);

    public bool Run(IEnumerable<string> fileNames)
    {
        lock (WorkingDirectorySync)
        {
            var previous = Environment.CurrentDirectory;
            Directory.CreateDirectory(TestRepository.BuildOutputRoot);
            try
            {
                Environment.CurrentDirectory = TestRepository.BuildOutputRoot;
                return RunCore(fileNames);
            }
            finally
            {
                Environment.CurrentDirectory = previous;
            }
        }
    }

    private bool RunCore(IEnumerable<string> fileNames)
    {
        TestReport? report = null;

        try
        {
            report = RunWithReport(fileNames);

            var output = TestFormatter.Format(report,
                options.ShowOnlyFailures ? TestFormatFlags.OnlyFailed : TestFormatFlags.None);
            Console.WriteLine(output);
            return report.Results.All(result => result.Error is null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failure! {ex}");
            return false;
        }
        finally
        {
            if (report is not null && !string.IsNullOrWhiteSpace(options.OutputPath))
            {
                try
                {
                    var path = Path.GetFullPath(options.OutputPath);
                    var flags = options.UseMarkdown ? TestFormatFlags.Markdown : TestFormatFlags.None;
                    var reportStr = TestFormatter.Format(report, flags);
                    File.WriteAllText(path, reportStr);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Unable to save test results: {ex}");
                }
            }
        }
    }

    internal TestReport RunWithReport(IEnumerable<string> fileNames)
    {
        var files = fileNames.ToArray();
        var report = new TestReport { TestFiles = files };
        var warns = new List<BuildMessage>();
        var blocks = GatherTests(files, warns);

        RunTests(report, blocks, warns);

        if (warns.Count > 0)
        {
            report.BuildWarnings = warns;
        }

        return report;
    }

    private static TestBlockInfo[] GatherTests(IEnumerable<string> files, List<BuildMessage> warns)
    {
        var blocks = new List<TestBlockInfo>();

        foreach (var file in files)
        {
            var res = SpellkitParser.Parse(SourceBuffer.FromFile(file));

            if (!res.Success)
            {
                blocks.Add(new TestBlockInfo(file)
                {
                    Error = "Unable to process test file: " + string.Join(" ", res.Messages)
                });
                continue;
            }

            if (res.Messages.Any())
            {
                warns.AddRange(res.Messages);
            }

            foreach (var node in res.Value!.Root.Nodes)
            {
                if (node is RegionSyntax b)
                {
                    blocks.Add(new TestBlockInfo(file)
                    {
                        Block = b
                    });
                }
            }
        }

        return blocks.ToArray();
    }

    private void RunTests(
        TestReport report,
        TestBlockInfo[] testBlocks,
        List<BuildMessage> warns)
    {
        const string INIT = "Initialize";

        if (testBlocks.Length == 0)
        {
            return;
        }

        Dictionary<string, SpellkitCodeModel> inits;

        try
        {
            inits = testBlocks.Where(b => b.Block is not null && b.Block.Name == INIT)
                .ToDictionary(b => b.FileName, b => b.Block!.Body);
        }
        catch (ArgumentException)
        {
            throw new Exception("Multiple initialization blocks in a test file.");
        }

        var selectedBlocks = testBlocks
            .Where(b => b.Block?.Name != INIT)
            .Where(b => options.Region is null
                || string.Equals(b.Block?.Name, options.Region, StringComparison.Ordinal))
            .ToArray();

        if (options.Region is not null && selectedBlocks.Length == 0)
        {
            report.Results.Add(new TestResult
            {
                Name = options.Region,
                Error = $"Test region '{options.Region}' was not found.",
                FileName = options.TestPath,
                ReproductionCommand = CreateReproductionCommand(options.TestPath, options.Region)
            });
            return;
        }

        foreach (var bi in selectedBlocks)
        {
            var fileName = bi.FileName ?? "unknown";

            if (bi.Block is null)
            {
                report.Results.Add(new TestResult
                {
                    Error = bi.Error ?? "Unknown error",
                    FileName = fileName
                });
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var ast = bi.Block.Body;

            if (inits.TryGetValue(fileName, out var init))
            {
                var imports = ast.Imports;

                if (init.Imports is not null)
                {
                    imports = new ImportSyntax[ast.Imports.Length + init.Imports.Length];
                    Array.Copy(init.Imports, 0, imports, 0, init.Imports.Length);
                    Array.Copy(ast.Imports, 0, imports, init.Imports.Length, ast.Imports.Length);
                }

                var root = new BlockSyntax(init.Root.Location);
                root.Nodes.AddRange(init.Root.Nodes);
                root.Nodes.AddRange(ast.Root.Nodes);
                ast = new SpellkitCodeModel(root, imports, ast.FileName);
            }

            var buildOptions = new BuilderOptions();
            var lookup = FileLookup.Create(
                buildOptions,
                Path.GetDirectoryName(fileName)!);
            var host = new SpellkitHost(new()
            {
                BuilderOptions = buildOptions,
                Limits = new()
                {
                    MaxExecutionTime = options.TestTimeout
                }
            })
                .UseFileLookup(lookup)
                .AddStandardLibrary();
            using var session = host.CreateInstance();
            var execution = session.Execute(ast);
            warns.AddRange(execution.Diagnostics
                .Where(diagnostic => diagnostic.Severity == SpellkitDiagnosticSeverity.Warning)
                .Select(diagnostic => new BuildMessage(
                    diagnostic.Message,
                    BuildMessageType.Warning,
                    diagnostic.Code,
                    diagnostic.Line,
                    diagnostic.Column,
                    diagnostic.File)));

            if (!execution.Success)
            {
                stopwatch.Stop();
                var message = execution.Failure?.Message
                    ?? string.Join(' ', execution.Diagnostics.Select(diagnostic => diagnostic.Message));
                var exception = execution.Failure?.Exception;
                var (expected, actual) = GetAssertionValues(message);
                report.Results.Add(new TestResult
                {
                    Name = bi.Block.Name,
                    Error = message,
                    StackTrace = exception?.ToString(),
                    Expected = expected,
                    Actual = actual,
                    FileName = bi.FileName ?? "<unknown>",
                    Duration = stopwatch.Elapsed,
                    TimedOut = execution.Failure is
                    {
                        Kind: SpellkitFailureKind.Limit,
                        Limit: SpellkitExecutionLimitKind.Time
                    },
                    ReproductionCommand = CreateReproductionCommand(fileName, bi.Block.Name)
                });
                continue;
            }

            stopwatch.Stop();
            report.Results.Add(new TestResult
            {
                Name = bi.Block.Name,
                FileName = bi.FileName ?? "<unknown>",
                Duration = stopwatch.Elapsed
            });
        }
    }

    private static (string? Expected, string? Actual) GetAssertionValues(string message)
    {
        var match = AssertionFailure.Match(message);
        return match.Success
            ? (match.Groups["expected"].Value, match.Groups["actual"].Value)
            : (null, null);
    }

    private static string CreateReproductionCommand(string fileName, string? region)
    {
        var repoRoot = TestRepository.Root;
        var path = Path.GetRelativePath(repoRoot, Path.GetFullPath(fileName));
        var command = $@".\scripts\test-local.ps1 -Suite Language -TestPath ""{EscapePowerShell(path)}""";

        if (!string.IsNullOrWhiteSpace(region))
        {
            command += $@" -Region ""{EscapePowerShell(region)}""";
        }

        return command;
    }

    private static string EscapePowerShell(string value) =>
        value.Replace("`", "``", StringComparison.Ordinal)
            .Replace("\"", "`\"", StringComparison.Ordinal);
}
