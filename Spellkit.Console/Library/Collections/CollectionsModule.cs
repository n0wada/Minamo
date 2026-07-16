using Spellkit.Hosting;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Collections;

[SpellkitModule("collections")]
[SpellkitForeignType(typeof(SpkSortedDictionaryTypeInfo))]
public static class CollectionsModule
{
    [SpellkitCommand("SortedDictionary")]
    internal static SpkObject SortedDictionary(SpellkitCommandContext host, SpkObject values = null!) =>
        SpkSortedDictionaryTypeInfo.New(host.ExecutionContext, values);
}
