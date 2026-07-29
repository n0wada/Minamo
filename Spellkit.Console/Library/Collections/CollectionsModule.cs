using Spellkit.Hosting;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Collections;

[SpellkitModule("collections")]
[SpellkitForeignType(typeof(SpellkitSortedDictionaryTypeInfo))]
public static class CollectionsModule
{
    [SpellkitCommand("SortedDictionary")]
    internal static SpellkitObject SortedDictionary(SpellkitCommandContext host, SpellkitObject values = null!) =>
        SpellkitSortedDictionaryTypeInfo.New(host.ExecutionContext, values);
}
