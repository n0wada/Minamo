using Minamo.Hosting;
using Minamo.Library.Collections;
using Minamo.Library.ReadLineLibrary;
using Minamo.Library.IO;
using Minamo.Library.Mathematics;
using Minamo.Library.Random;
using Minamo.Library.Text;
using Minamo.Library.Time;
using Minamo.Library.Uuid;

namespace Minamo.Library;

internal static class MinamoLibraryExtensions
{
    internal static MinamoHost AddStandardLibrary(this MinamoHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host
            .AddCollectionsModule()
            .AddReadLineModule()
            .AddIoModule()
            .AddMathModule()
            .AddRandomModule()
            .AddTextModule()
            .AddTimeModule()
            .AddUuidModule();
    }
}
