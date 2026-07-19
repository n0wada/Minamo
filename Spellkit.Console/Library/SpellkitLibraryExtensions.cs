using Spellkit.Hosting;
using Spellkit.Library.Binary;
using Spellkit.Library.Collections;
using Spellkit.Library.ConsoleLibrary;
using Spellkit.Library.Desktop;
using Spellkit.Library.Http;
using Spellkit.Library.IO;
using Spellkit.Library.Mathematics;
using Spellkit.Library.Text;
using Spellkit.Library.Time;
using Spellkit.Library.Uuid;

namespace Spellkit.Library;

internal static class SpellkitLibraryExtensions
{
    internal static SpellkitHost AddStandardLibrary(this SpellkitHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host
            .AddBinaryModule()
            .AddCollectionsModule()
            .AddConsoleModule()
            .AddDesktopModule()
            .AddHttpModule()
            .AddMathModule()
            .AddTextModule()
            .AddTimeModule()
            .AddUuidModule()
            .AddIoModule();
    }
}
