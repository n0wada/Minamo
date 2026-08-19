using Spellkit.Hosting;
using Spellkit.Library.ConsoleLibrary;
using Spellkit.Runtime;
using System.Threading.Tasks;

namespace Spellkit.Library.ReadLineLibrary;

[SpellkitModule("readline")]
[SpellkitForeignType(typeof(SpellkitConsoleTypeInfo))]
public static class ReadLineModule
{
    [SpellkitCommand("readLine")]
    internal static async ValueTask<string> ReadLine(SpellkitCommandContext host)
    {
        var context = host.ExecutionContext;
        var environment = context.GetContextVariable<SpellkitEnvironment>(SpellkitEnvironment.ContextKey);
        return environment is null
            ? await System.Console.In.ReadLineAsync(
                context.Control?.CancellationToken ?? default).ConfigureAwait(false) ?? string.Empty
            : await environment.ReadLineAsync(
                context.Control?.CancellationToken ?? default).ConfigureAwait(false);
    }
}
