using Spellkit.Hosting;
using Spellkit.Library.ConsoleLibrary;
using Spellkit.Runtime;

namespace Spellkit.Library.ReadLineLibrary;

[SpellkitModule("readline")]
[SpellkitForeignType(typeof(SpellkitConsoleTypeInfo))]
public static class ReadLineModule
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
