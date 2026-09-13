using Minamo.Hosting;
using Minamo.Library.ConsoleLibrary;
using System.Threading.Tasks;

namespace Minamo.Library.ReadLineLibrary;

[MinamoModule("readline")]
[MinamoForeignType(typeof(MinamoConsoleTypeInfo))]
public static class ReadLineModule
{
    [MinamoCommand("readLine")]
    internal static async ValueTask<string> ReadLine(MinamoCommandContext host) =>
        await System.Console.In.ReadLineAsync(host.CancellationToken).ConfigureAwait(false)
            ?? string.Empty;
}
