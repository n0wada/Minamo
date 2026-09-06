using Minamo.Hosting;
using Minamo.Library.ConsoleLibrary;
using Minamo.Runtime;
using System.Threading.Tasks;

namespace Minamo.Library.ReadLineLibrary;

[MinamoModule("readline")]
[MinamoForeignType(typeof(MinamoConsoleTypeInfo))]
public static class ReadLineModule
{
    [MinamoCommand("readLine")]
    internal static async ValueTask<string> ReadLine(MinamoCommandContext host)
    {
        var context = host.ExecutionContext;
        var environment = context.GetContextVariable<MinamoEnvironment>(MinamoEnvironment.ContextKey);
        return environment is null
            ? await System.Console.In.ReadLineAsync(
                context.Control?.CancellationToken ?? default).ConfigureAwait(false) ?? string.Empty
            : await environment.ReadLineAsync(
                context.Control?.CancellationToken ?? default).ConfigureAwait(false);
    }
}
