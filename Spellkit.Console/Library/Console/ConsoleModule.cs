using Spellkit.Hosting;
using Spellkit.Runtime;

namespace Spellkit.Library.ConsoleLibrary;

[SpellkitModule("console")]
[SpellkitForeignType(typeof(SpkConsoleTypeInfo))]
public static class ConsoleModule
{
    [SpellkitCommand("readLine")]
    internal static string ReadLine(SpellkitCommandContext host)
    {
        var context = host.ExecutionContext;
        var environment = context.GetContextVariable<SpellkitEnvironment>(SpellkitEnvironment.ContextKey);
        return environment is null
            ? System.Console.ReadLine() ?? string.Empty
            : environment.ReadLine(context.Control?.CancellationToken ?? default);
    }
}
