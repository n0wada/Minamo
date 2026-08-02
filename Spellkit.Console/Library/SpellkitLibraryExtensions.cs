using Spellkit.Hosting;
using Spellkit.Library.Binary;
using Spellkit.Library.Collections;
using Spellkit.Library.ConsoleLibrary;
using Spellkit.Library.Http;
using Spellkit.Library.IO;
using Spellkit.Library.Json;
using Spellkit.Library.Mathematics;
using Spellkit.Library.Random;
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
            .AddJsonModule()
            .AddMathModule()
            .AddRandomModule()
            .AddTextModule()
            .AddTimeModule()
            .AddUuidModule();
    }

    internal static SpellkitHost AddHostLibrary(this SpellkitHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host
            .AddConsoleModule()
            .AddIoModule();
    }

    internal static SpellkitHost AddExtendedLibrary(this SpellkitHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.AddHttpModule();
    }

    internal static SpellkitHost AddBundledLibraries(this SpellkitHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host
            .AddStandardLibrary()
            .AddHostLibrary()
            .AddExtendedLibrary();
    }
}
