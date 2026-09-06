using Minamo.Hosting;
using Minamo.Runtime.Types;

namespace Minamo.Library.Collections;

[MinamoModule("collections")]
[MinamoForeignType(typeof(MinamoPriorityQueueTypeInfo))]
[MinamoForeignType(typeof(MinamoDequeTypeInfo))]
[MinamoForeignType(typeof(MinamoSortedSetTypeInfo))]
[MinamoForeignType(typeof(MinamoMultiMapTypeInfo))]
[MinamoForeignType(typeof(MinamoRingBufferTypeInfo))]
[MinamoForeignType(typeof(MinamoSortedDictionaryTypeInfo))]
public static class CollectionsModule
{
    [MinamoCommand("PriorityQueue")]
    internal static MinamoObject PriorityQueue(MinamoCommandContext host, MinamoObject values = null!) =>
        MinamoPriorityQueueTypeInfo.New(host.ExecutionContext, values);

    [MinamoCommand("Deque")]
    internal static MinamoObject Deque(MinamoCommandContext host, MinamoObject values = null!) =>
        MinamoDequeTypeInfo.New(host.ExecutionContext, values);

    [MinamoCommand("SortedSet")]
    internal static MinamoObject SortedSet(MinamoCommandContext host, MinamoObject values = null!) =>
        MinamoSortedSetTypeInfo.New(host.ExecutionContext, values);

    [MinamoCommand("MultiMap")]
    internal static MinamoObject MultiMap(MinamoCommandContext host, MinamoObject values = null!) =>
        MinamoMultiMapTypeInfo.New(host.ExecutionContext, values);

    [MinamoCommand("RingBuffer")]
    internal static MinamoObject RingBuffer(
        MinamoCommandContext host,
        int capacity,
        MinamoObject values = null!) =>
        MinamoRingBufferTypeInfo.New(host.ExecutionContext, capacity, values);

    [MinamoCommand("SortedDictionary")]
    internal static MinamoObject SortedDictionary(MinamoCommandContext host, MinamoObject values = null!) =>
        MinamoSortedDictionaryTypeInfo.New(host.ExecutionContext, values);
}
