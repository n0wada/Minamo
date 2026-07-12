#pragma warning disable CA1822
using Spellkit.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spellkit;

internal sealed class ReplCommands
{
    public const string Prefix = "#";

    private Dictionary<string, CommandCallBack> commands = null!;

    private readonly ReplSession session;

    internal delegate void CommandCallBack(object? arg);

    public ReplCommands(ReplSession session)
    {
        this.session = session;
    }

    public void Dispatch(string command, object? argument)
    {
        if (commands is null)
        {
            commands = new Dictionary<string, CommandCallBack>(StringComparer.OrdinalIgnoreCase);
            var mis = typeof(ReplCommands).GetMethods();

            foreach (var m in mis)
            {
                if (Attribute.GetCustomAttribute(m, typeof(BindingAttribute)) is not BindingAttribute attr)
                {
                    continue;
                }

                var act = (CommandCallBack)m.CreateDelegate(typeof(CommandCallBack), this);

                foreach (var n in attr.Names)
                {
                    commands.Add(n, act);
                }
            }
        }

        if (!commands.TryGetValue(command, out var cmd))
        {
            ConsoleOutput.Error($"Unknown command #{command}.");
            return;
        }

        cmd(argument);
    }

    [Binding("bye", "exit", Help = "Exits console.")]
    public void Exit(object _)
    {
        ConsoleOutput.Output("Bye!");
        Environment.Exit(0);
    }

    [Binding("cls", "clear", Help = "Clears the console window.")]
    public void Clear(object _)
    {
        Console.Clear();
    }

    [Binding("reset", Help = "Resets the interactive session.")]
    public void Reset(object _)
    {
        session.Reset();
        ConsoleOutput.Output("Virtual machine was reset.");
    }

    [Binding("help", Help = "Displays this help screen.")]
    public void Help(object _)
    {
        var switches = CommandLine.GenerateHelp<CommandLineOptions>("-").TrimEnd('\r', '\n');
        var commands = CommandLine.GenerateHelp(typeof(ReplCommands), Prefix).TrimEnd('\r', '\n');

        ConsoleOutput.LineFeed();
        ConsoleOutput.Output("Command line switches:");
        ConsoleOutput.Output(switches);
        ConsoleOutput.LineFeed();
        ConsoleOutput.Output("Commands:");
        ConsoleOutput.Output(commands);
    }

    [Binding("dir", Help = "Shows (if invoked without argument) or sets current working directory.")]
    public void Directory(object val)
    {
        if (val is null)
        {
            ConsoleOutput.Output(Environment.CurrentDirectory);
        }
        else
        {
            try
            {
                System.IO.Directory.SetCurrentDirectory(val.ToString()!);
                ConsoleOutput.Output($"Current directory set to: {Environment.CurrentDirectory}");
            }
            catch (Exception)
            {
                ConsoleOutput.Error($"Unable to set current directory to: {val}");
            }
        }
    }

    [Binding("options", Help = "Displays current console options.")]
    public void ShowOptions(object _)
    {
        ConsoleOutput.LineFeed();
        ConsoleOutput.Output("Current options:");
        ConsoleOutput.Output(session.Options.ToString());
    }

    [Binding("il", Help = "Generates IL (intermediate assembly) for all code in the current session.")]
    public void GenerateIL(object _)
    {
        if (session.RuntimeContext is not null && session.RuntimeContext.Composition is not null)
        {
            var str = BytecodeFormatter.Format(session.RuntimeContext.Composition.Units);
            Console.Write(str);
        }
    }

    [Binding("dump", Help = "Dumps global variables and prints their values.")]
    public void Dump(object _)
    {
        ConsoleOutput.LineFeed();
        ConsoleOutput.Output("Dump of globals:");

        if (session.RuntimeContext is null)
        {
            ConsoleOutput.Output("<none>");
            return;
        }

        var xs = SpkMachine.DumpVariables(session.RuntimeContext).ToList();
        var vals = new string[xs.Count];
        var types = new string[xs.Count];
        var (keyLen, valLen) = (0, 0);
        var etx = SpkMachine.CreateExecutionContext(session.RuntimeContext);

        for (var i = 0; i < xs.Count; i++)
        {
            var rv = xs[i];
            vals[i] = ConsoleOutput.Format(rv.Value, etx, notype: true, maxLen: 32);
            types[i] = rv.Value.TypeName;

            if (keyLen < rv.Name.Length)
            {
                keyLen = rv.Name.Length;
            }

            if (valLen < vals[i].Length)
            {
                valLen = vals[i].Length;
            }
        }

        for (var i = 0; i < xs.Count; i++)
        {
            var rv = xs[i];
            ConsoleOutput.Output($"{rv.Name}{new string(' ', keyLen - rv.Name.Length)} | {vals[i]}{new string(' ', valLen - vals[i].Length)} | {types[i]}");
        }
    }

    [Binding("eval", Help = "Evaluates a given file in a current interactive session.")]
    public void Eval(object arg)
    {
        var str = arg?.ToString()?.Trim('\"', '\'');

        if (str is not null && session.EvalFile(str, measureTime: false))
        {
            ConsoleOutput.Output($"File \"{Path.GetFileName(str)}\" successfully evaluated.");
        }
    }
}
