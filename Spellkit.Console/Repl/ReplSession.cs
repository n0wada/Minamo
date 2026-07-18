using Spellkit.Compiler;
using Spellkit.Hosting;
using Spellkit.Library;
using Spellkit.Linker;
using Spellkit.Parser;
using Spellkit.Runtime;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Spellkit;

internal sealed class ReplSession : IDisposable
{
    private readonly ReplCommands commands;
    private readonly SpellkitInstance session;

    public ReplSession(CommandLineOptions options)
    {
        Options = options;
        var host = CreateHost(options, out var buildOptions);
        BuildOptions = buildOptions;
        var nofn = options.FileNames is null || options.FileNames.Length == 0 || string.IsNullOrWhiteSpace(options.FileNames[0]);

        var lookup = FileLookup.Create(BuildOptions,
            nofn ? Environment.CurrentDirectory! : Path.GetDirectoryName(options.FileNames![0])!, options.Paths);
        host.UseFileLookup(lookup);
        session = host.CreateInstance(
            new SpellkitEnvironment()
                .UseInput(_ => Console.ReadLine())
                .UseOutput(Console.Write),
            options.UserArguments);
        CompilationLinker = new SpkIncrementalLinker(lookup, options.UserArguments);
        commands = new ReplCommands(this);
    }

    private static SpellkitHost CreateHost(
        CommandLineOptions options,
        out BuilderOptions buildOptions)
    {
        buildOptions = new BuilderOptions
        {
            Debug = options.Debug,
            LinkerLog = options.LinkerLog,
            NoOptimizations = options.NoOptimizations,
            NoLangModule = options.NoLang,
            NoWarnings = options.NoWarnings,
            NoWarningsLinker = options.NoWarningsLinker
        };

        if (options.IgnoreWarnings != null)
        {
            foreach (var i in options.IgnoreWarnings)
            {
                if (!buildOptions.IgnoreWarnings.Contains(i))
                {
                    buildOptions.IgnoreWarnings.Add(i);
                }
            }
        }

        var host = new SpellkitHost(new()
        {
            BuilderOptions = buildOptions
        })
            .AddStandardLibrary();

        host.ConfigureModules(buildOptions);
        return host;
    }

    public BuilderOptions BuildOptions { get; }

    public RuntimeContext? RuntimeContext => session.RuntimeContext;

    public SpkIncrementalLinker CompilationLinker { get; private set; }

    public CommandLineOptions Options { get; }

    public void Run()
    {
        var source = new StringBuilder();
        var expectsMore = false;

        while (true)
        {
            if (!expectsMore)
            {
                ConsoleOutput.LineFeed();
            }

            ConsoleOutput.Prefix(expectsMore ? "-->" : "kit>");
            var line = Console.ReadLine();

            if (line is null)
            {
                return;
            }

            line = line.Trim();
            if (TryRunCommand(line))
            {
                continue;
            }

            source.AppendLine(line);
            if (line.Length > 0 && !SpkParser.Parse(SourceBuffer.FromString(source.ToString())).Success)
            {
                expectsMore = true;
                continue;
            }

            expectsMore = false;
            Eval(source.ToString());
            source.Clear();
        }
    }

    public void Reset()
    {
        session.Reset();
        CompilationLinker = new SpkIncrementalLinker(
            CompilationLinker.Lookup,
            Options.UserArguments);
    }

    public bool Eval(string source)
    {
        var result = session.Execute(source, "<stdio>");
        return PrintResult(result, measureTime: false);
    }

    public bool Compile(string fileName, out Unit unit)
    {
        unit = null!;
        Result<Unit> made;

        try
        {
            var buffer = SourceBuffer.FromFile(fileName);
            made = CompilationLinker.Compile(buffer);
        }
        catch (Exception ex)
        {
            ConsoleOutput.Error($"Unable to read file \"{fileName}\": {ex.Message}");
            return false;
        }

        if (made.Messages.Any())
        {
            ConsoleOutput.PrintErrors(made.Messages);
        }

        if (!made.Success)
        {
            return false;
        }

        unit = made.Value!;
        return true;
    }

    public bool EvalFile(string fileName, bool measureTime)
    {
        var result = session.ExecuteFile(fileName);
        return PrintResult(result, measureTime);
    }

    private bool PrintResult(SpellkitExecutionResult result, bool measureTime)
    {
        if (result.Diagnostics.Count != 0)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                ConsoleOutput.Output(
                    $"{diagnostic.File}:{diagnostic.Line}:{diagnostic.Column} "
                    + $"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
            }
        }

        if (!result.Success)
        {
            ConsoleOutput.Error(result.Failure?.Message ?? "Execution failed.");
            return false;
        }

        var value = result.GetValue<Spellkit.Runtime.Types.SpkObject>();
        if (value is not null and not Spellkit.Runtime.Types.SpkNil
            && RuntimeContext is not null)
        {
            var context = SpkMachine.CreateExecutionContext(RuntimeContext);
            ConsoleOutput.Output(ConsoleOutput.Format(value, context));
        }

        if (measureTime)
        {
            ConsoleOutput.SupplementaryOutput(
                $"Time taken: {result.Metrics.TotalDuration:mm\\:ss\\.fffffff}");
        }

        return true;
    }

    public void Dispose() => session.Dispose();

    private bool TryRunCommand(string line)
    {
        if (line.Length < 2 || line[0] != ReplCommands.Prefix[0])
        {
            return false;
        }

        var commandLine = line[1..].Trim();
        var separator = commandLine.IndexOf(' ');
        var command = separator < 0 ? commandLine : commandLine[..separator];
        var argument = separator < 0 ? null : commandLine[(separator + 1)..];
        commands.Dispatch(command, argument);
        return true;
    }
}
