using Spellkit.Hosting;
using Spellkit.Library.Binary;
using Spellkit.Library.IO;
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
            .AddTextModule()
            .AddTimeModule()
            .AddUuidModule()
            .AddIoModule();
    }
}
