using Minamo.Compiler;
using Minamo.Hosting;
using Minamo.Library;
using Minamo.Linker;
using Minamo.Parser;
using Minamo.Runtime;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Minamo;

internal sealed class ReplSession : IDisposable
{
    private readonly ReplCommands commands;
    private readonly MinamoInstance session;

    public ReplSession(CommandLineOptions options)
    {
        Options = options;
        var host = CreateHost(options, out var buildOptions);
        BuildOptions = buildOptions;
        var nofn = options.FileNames is null || options.FileNames.Length == 0 || string.IsNullOrWhiteSpace(options.FileNames[0]);

        var lookup = FileLookup.Create(BuildOptions,
            nofn ? Environment.CurrentDirectory! : Path.GetDirectoryName(options.FileNames![0])!, options.Paths);
        host.UseFileLookup(lookup);
        var minamoEnvironment = new MinamoEnvironment()
                .UseInputAsync(cancellationToken => Console.In.ReadLineAsync(cancellationToken))
                .UseOutput(Console.Write);
        session = host.CreateInstance(minamoEnvironment, options.UserArguments);
        CompilationLinker = new MinamoIncrementalLinker(lookup, options.UserArguments);
        commands = new ReplCommands(this);
    }

    private static MinamoHost CreateHost(
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

        var host = new MinamoHost(new()
        {
            BuilderOptions = buildOptions
        })
            .AddStandardLibrary()
            .AddConfiguredExtensionLibraries();

        host.ConfigureModules(buildOptions);
        return host;
    }

    public BuilderOptions BuildOptions { get; }

    public RuntimeContext? RuntimeContext => session.RuntimeContext;

    public MinamoIncrementalLinker CompilationLinker { get; private set; }

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

            ConsoleOutput.Prefix(expectsMore ? "-->" : "nami>");
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
            if (line.Length > 0 && !MinamoParser.Parse(SourceBuffer.FromString(source.ToString())).Success)
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
        CompilationLinker = new MinamoIncrementalLinker(
            CompilationLinker.Lookup,
            Options.UserArguments);
    }

    public bool Eval(string source)
    {
        var result = session.ExecuteAsync(source).GetAwaiter().GetResult();
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
        var result = session.ExecuteFileAsync(fileName).GetAwaiter().GetResult();
        return PrintResult(result, measureTime);
    }

    public bool RunSelect(string name)
    {
        try
        {
            return RunSelectAsync(name).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ConsoleOutput.Error(ex.Message);
            return false;
        }
    }

    private async Task<bool> RunSelectAsync(string name)
    {
        using var select = await session.OpenSelectAsync(name).ConfigureAwait(false);
        await RunSelectSessionAsync(select).ConfigureAwait(false);
        return true;
    }

    private async ValueTask RunSelectSessionAsync(MinamoSelect select)
    {
        while (!select.IsCompleted)
        {
            var choices = select.Choices;
            if (choices.Count == 0)
            {
                ConsoleOutput.Output("No choices are currently available.");
                return;
            }

            ConsoleOutput.LineFeed();
            for (var i = 0; i < choices.Count; i++)
            {
                var renderedChoice = choices[i];
                ConsoleOutput.Output($"{i + 1}. {renderedChoice.Label}");
            }

            ConsoleOutput.Prefix("select> ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                continue;
            }

            if (input is "cancel" or "quit")
            {
                select.Cancel();
                return;
            }

            var separator = input.IndexOf(' ');
            var choiceName = separator < 0 ? input : input[..separator];
            var argument = separator < 0 ? null : input[(separator + 1)..].Trim();
            if (int.TryParse(choiceName, out var number)
                && number > 0 && number <= choices.Count)
            {
                choiceName = choices[number - 1].Id;
            }

            var choice = choices.FirstOrDefault(candidate => candidate.Id == choiceName);
            if (choice is null)
            {
                ConsoleOutput.Error($"Unknown choice '{choiceName}'.");
                continue;
            }

            try
            {
                if (choice.ParameterCount == 0)
                {
                    if (argument is not null)
                    {
                        ConsoleOutput.Error($"Choice '{choice.Id}' does not accept an argument.");
                        continue;
                    }

                    await select.SelectAsync(choice).ConfigureAwait(false);
                }
                else if (choice.ParameterCount == 1 && argument is not null)
                {
                    await select.SelectAsync(choice, argument).ConfigureAwait(false);
                }
                else
                {
                    ConsoleOutput.Error(
                        $"Choice '{choice.Id}' requires {choice.ParameterCount} argument(s). "
                        + "The console supports one string argument: <choice> <value>.");
                }
            }
            catch (Exception ex)
            {
                ConsoleOutput.Error(ex.Message);
            }
        }
    }

    private bool PrintResult(MinamoExecutionResult result, bool measureTime)
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

        var value = result.GetValue<Minamo.Runtime.Types.MinamoObject>();
        if (value is not null and not Minamo.Runtime.Types.MinamoNil
            && RuntimeContext is not null)
        {
            var context = MinamoMachine.CreateExecutionContext(RuntimeContext);
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
        if (TryRunSelect(line))
        {
            return true;
        }

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

    // Console command only; select invocation is not part of the language.
    private bool TryRunSelect(string line)
    {
        if (!line.StartsWith("do ", StringComparison.Ordinal))
        {
            return false;
        }

        var name = line[3..].Trim();
        if (name.Length == 0 || name.Any(character =>
            !char.IsLetterOrDigit(character) && character is not '_' and not '.'))
        {
            return false;
        }

        RunSelect(name);
        return true;
    }
}
