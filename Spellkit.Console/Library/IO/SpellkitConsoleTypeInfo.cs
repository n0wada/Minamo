using Spellkit.Codegen;
using Spellkit.Hosting;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.IO;

namespace Spellkit.Library.ConsoleLibrary;

[SpellkitType]
public sealed partial class SpellkitConsoleTypeInfo : SpellkitForeignTypeInfo
{
    private const string VAR_CONSOLEOUTPUT = "sys.ConsoleOutput";
    private const string VAR_CONSOLEINPUT = "sys.ConsoleInput";
    private const string VAR_DEFAULTBACKCOLOR = "sys.DefaultBackColor";

    public override string ReflectedTypeName => "Console";

    private static ConsoleColor GetColor(string color)
    {
        if (Enum.TryParse<ConsoleColor>(color, true, out var value))
        {
            return value;
        }

        return ConsoleColor.Black;
    }

    private static void WriteToConsole(ExecutionContext ctx, SpellkitObject value, string? color, string? backColor, bool newLine)
    {
        var str = value.ToString(ctx).Value;

        if (ctx.HasErrors)
        {
            return;
        }

        var oldColor = Console.ForegroundColor;
        var oldBackColor = Console.BackgroundColor;

        if (color is not null)
        {
            Console.ForegroundColor = GetColor(color);
        }

        if (backColor is not null)
        {
            Console.BackgroundColor = GetColor(backColor);
        }

        if (newLine)
        {
            Console.Write(str + Environment.NewLine);
        }
        else
        {
            Console.Write(str);
        }

        Console.ForegroundColor = oldColor;
        Console.BackgroundColor = oldBackColor;
    }

    [SpellkitStaticMethod]
    internal static void WriteLine(ExecutionContext ctx, [Default]SpellkitObject value, string? color = null, string? backColor = null) =>
        WriteToConsole(ctx, value, color, backColor, newLine: true);

    [SpellkitStaticMethod]
    internal static void Write(ExecutionContext ctx, SpellkitObject value, string? color = null, string? backColor = null) =>
       WriteToConsole(ctx, value, color, backColor, newLine: false);

    [SpellkitStaticMethod]
    internal static char Read() => (char)Console.Read();

    [SpellkitStaticMethod]
    internal static string ReadLine(ExecutionContext ctx)
    {
        var environment = ctx.GetContextVariable<SpellkitEnvironment>(SpellkitEnvironment.ContextKey);
        return environment is null
            ? Console.ReadLine() ?? string.Empty
            : environment.ReadLine(ctx.Control?.CancellationToken ?? default);
    }

    [SpellkitStaticMethod]
    internal static void Clear(ExecutionContext ctx, string? backColor = null)
    {
        if (!ctx.HasContextVariable(VAR_DEFAULTBACKCOLOR))
        {
            ctx.SetContextVariable(VAR_DEFAULTBACKCOLOR, Console.BackgroundColor);
        }

        Console.BackgroundColor = backColor is not null ? GetColor(backColor)
            : ctx.GetContextVariable<ConsoleColor>(VAR_DEFAULTBACKCOLOR);
        Console.Clear();
    }

    [SpellkitStaticMethod]
    internal static SpellkitObject GetCursorPosition()
    {
        var (left, top) = Console.GetCursorPosition();
        return SpellkitTuple.Create(new("left", left), new("top", top));
    }

    [SpellkitStaticMethod]
    internal static void SetCursorPosition(ExecutionContext ctx, int left, int top)
    {
        try
        {
            Console.SetCursorPosition(left, top);
        }
        catch (ArgumentOutOfRangeException)
        {
            ctx.InvalidValue();
        }
    }

    [SpellkitStaticMethod]
    internal static void SetTitle(string value) => Console.Title = value;

    [SpellkitStaticMethod]
    internal static void SetOutput(ExecutionContext ctx, SpellkitObject? write = null, SpellkitObject? writeLine = null)
    {
        if (write is null || writeLine is null)
        {
            var outputWriter = ctx.GetContextVariable<TextWriter>(VAR_CONSOLEOUTPUT);
            if (outputWriter is not null)
            {
                Console.SetOut(outputWriter);
            }
        }
        else
        {
            if (!ctx.HasContextVariable(VAR_CONSOLEOUTPUT))
            {
                ctx.SetContextVariable(VAR_CONSOLEOUTPUT, Console.Out);
            }

            Console.SetOut(new ConsoleTextWriter(ctx, write!, writeLine));
        }
    }

    [SpellkitStaticMethod]
    internal static void SetInput(ExecutionContext ctx, SpellkitObject? read = null, SpellkitObject? readLine = null)
    {
        if (read is null || readLine is null)
        {
            var consoleInput = ctx.GetContextVariable<TextReader>(VAR_CONSOLEINPUT);
            if (consoleInput is not null)
            {
                Console.SetIn(consoleInput);
            }
        }
        else
        {
            if (!ctx.HasContextVariable(VAR_CONSOLEINPUT))
            {
                ctx.SetContextVariable(VAR_CONSOLEINPUT, Console.In);
            }

            Console.SetIn(new ConsoleTextReader(ctx, read, readLine));
        }
    }

    [SpellkitStaticMethod]
    internal static SpellkitObject ReadKey(ExecutionContext ctx, bool intercept = false)
    {
        try
        {
            var ci = Console.ReadKey(intercept);
            return SpellkitTuple.Create(
                new("key", ci.Key.ToString()),
                new("keyChar", ci.KeyChar),
                new("alt", (ci.Modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt),
                new("shift", (ci.Modifiers & ConsoleModifiers.Shift) == ConsoleModifiers.Shift),
                new("ctrl", (ci.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control)
            );
        }
        catch (InvalidOperationException)
        {
            return ctx.InvalidOperation();
        }
    }
}
