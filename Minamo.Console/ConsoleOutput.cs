using Minamo.Runtime;
using Minamo.Runtime.Types;
using System;
using System.Collections.Generic;
using System.IO;
using static System.Console;

namespace Minamo;

internal static class ConsoleOutput
{
    public static bool NoLogo { get; set; }

    public static void LineFeed() => WriteLine();

    public static void Prefix(string data) => Write(data);

    public static void Error(string data) => WriteLine(data);

    public static void Warning(string data) => WriteLine(data);

    public static void Information(string data) => WriteLine(data);

    public static void Output(string data) => WriteLine(data);

    public static void Output(ExecutionResult res)
    {
        if (res.Reason != TerminationReason.Complete)
        {
            Error($"Terminated, reason: {res.Reason}");
            return;
        }

        if (res.Value is MinamoNil || res.Value is null)
        {
            return;
        }

        var fmt = Format(res.Value, res.Context);
        Output(fmt);
    }

    public static string Format(MinamoObject obj, ExecutionContext ctx, bool notype = false, int maxLen = 0)
    {
        string fmt;

        try
        {
            fmt = obj.ToLiteral(ctx).ToString();
        }
        catch (Exception ex)
        {
            return $"Formatting failed:\n{(ex is MinamoCodeException c ? c.ToString() : ex.Message)}";
        }

        if (maxLen > 0 && fmt.Length > maxLen)
        {
            fmt = fmt[..maxLen] + "...";
        }

        return notype ? fmt : fmt + " :: " + obj.TypeName;
    }

    public static void SupplementaryOutput(string data) => WriteLine(data);

    public static void Help()
    {
        Output("Usage: minamo [options] [file ...]");
        LineFeed();
        Output("Options:");
        Output(CommandLine.GenerateHelp<CommandLineOptions>("-").TrimEnd('\r', '\n'));
    }

    public static void Version() => Output($"minamo {GetVersion()}");

    public static void Header()
    {
        if (!NoLogo)
        {
            var assemblyPath = typeof(ConsoleOutput).Assembly.Location;
            var ts = File.GetLastWriteTime(assemblyPath);
            Title = $"Minamo - {assemblyPath}";
            Header($"Minamo (Minamo Console). Build {ts.ToString().Trim()}");
            Subheader($"Version {GetVersion()}");
            Subheader($"Running {Environment.OSVersion}");
        }
    }

    internal static string GetVersion() =>
        typeof(ConsoleOutput).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private static void Header(params string[] lines)
    {
        foreach (var l in lines)
        {
            WriteLine(l);
        }
    }

    private static void Subheader(string data) => WriteLine(data);

    public static void PrintErrors(IEnumerable<BuildMessage> messages)
    {
        foreach (var m in messages)
        {
            if (m.Type == BuildMessageType.Error)
            {
                Error(m.ToString());
            }
            else if (m.Type == BuildMessageType.Warning)
            {
                Warning(m.ToString());
            }
            else
            {
                Information(m.ToString());
            }
        }
    }
}
