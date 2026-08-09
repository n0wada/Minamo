using Spellkit.Hosting;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Collections;

[SpellkitModule("collections")]
[SpellkitForeignType(typeof(SpellkitPriorityQueueTypeInfo))]
[SpellkitForeignType(typeof(SpellkitDequeTypeInfo))]
[SpellkitForeignType(typeof(SpellkitSortedSetTypeInfo))]
[SpellkitForeignType(typeof(SpellkitMultiMapTypeInfo))]
[SpellkitForeignType(typeof(SpellkitRingBufferTypeInfo))]
[SpellkitForeignType(typeof(SpellkitSortedDictionaryTypeInfo))]
public static class CollectionsModule
{
    [SpellkitCommand("PriorityQueue")]
    internal static SpellkitObject PriorityQueue(SpellkitCommandContext host, SpellkitObject values = null!) =>
        SpellkitPriorityQueueTypeInfo.New(host.ExecutionContext, values);

    [SpellkitCommand("Deque")]
    internal static SpellkitObject Deque(SpellkitCommandContext host, SpellkitObject values = null!) =>
        SpellkitDequeTypeInfo.New(host.ExecutionContext, values);

    [SpellkitCommand("SortedSet")]
    internal static SpellkitObject SortedSet(SpellkitCommandContext host, SpellkitObject values = null!) =>
        SpellkitSortedSetTypeInfo.New(host.ExecutionContext, values);

    [SpellkitCommand("MultiMap")]
    internal static SpellkitObject MultiMap(SpellkitCommandContext host, SpellkitObject values = null!) =>
        SpellkitMultiMapTypeInfo.New(host.ExecutionContext, values);

    [SpellkitCommand("RingBuffer")]
    internal static SpellkitObject RingBuffer(
        SpellkitCommandContext host,
        int capacity,
        SpellkitObject values = null!) =>
        SpellkitRingBufferTypeInfo.New(host.ExecutionContext, capacity, values);

    [SpellkitCommand("SortedDictionary")]
    internal static SpellkitObject SortedDictionary(SpellkitCommandContext host, SpellkitObject values = null!) =>
        SpellkitSortedDictionaryTypeInfo.New(host.ExecutionContext, values);
}
