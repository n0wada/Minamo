using Spellkit.Hosting;
using Spellkit.Library.Collections;
using Spellkit.Library.ReadLineLibrary;
using Spellkit.Library.IO;
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
