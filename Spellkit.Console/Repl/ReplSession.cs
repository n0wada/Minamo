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
        var spellkitEnvironment = new SpellkitEnvironment()
                .UseInput(_ => Console.ReadLine())
                .UseOutput(Console.Write)
                .UseSelect(RunSelectSession);
        session = host.CreateInstance(spellkitEnvironment, options.UserArguments);
        CompilationLinker = new SpellkitIncrementalLinker(lookup, options.UserArguments);
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
            .AddBundledLibraries();

        host.ConfigureModules(buildOptions);
        return host;
    }

    public BuilderOptions BuildOptions { get; }

    public RuntimeContext? RuntimeContext => session.RuntimeContext;

    public SpellkitIncrementalLinker CompilationLinker { get; private set; }

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
            if (line.Length > 0 && !SpellkitParser.Parse(SourceBuffer.FromString(source.ToString())).Success)
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
        CompilationLinker = new SpellkitIncrementalLinker(
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

    public bool RunSelect(string name)
    {
        try
        {
            using var select = session.OpenSelect(name);
            RunSelectSession(select);
            return true;
        }
        catch (Exception ex)
        {
            ConsoleOutput.Error(ex.Message);
            return false;
        }
    }

    private void RunSelectSession(SpellkitSelectSession select)
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
                ConsoleOutput.Output($"{i + 1}. {renderedChoice.Label} [{renderedChoice.Id}]");
                if (!string.IsNullOrEmpty(renderedChoice.Description))
                {
                    ConsoleOutput.Output($"   {renderedChoice.Description}");
                }
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

                    select.Select(choice.Id);
                }
                else if (choice.ParameterCount == 1 && argument is not null)
                {
                    select.Select(choice.Id, argument);
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

        var value = result.GetValue<Spellkit.Runtime.Types.SpellkitObject>();
        if (value is not null and not Spellkit.Runtime.Types.SpellkitNil
            && RuntimeContext is not null)
        {
            var context = SpellkitMachine.CreateExecutionContext(RuntimeContext);
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
