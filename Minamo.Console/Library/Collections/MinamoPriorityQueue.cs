using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Library.Collections;

public sealed class MinamoPriorityQueue : MinamoForeignObject
{
    private sealed record Entry(MinamoObject Value, MinamoObject Priority, long Sequence);

    private readonly List<Entry> entries = [];
    private readonly MinamoCollectionObjectComparer comparer = new();
    private long nextSequence;

    internal MinamoPriorityQueue(MinamoPriorityQueueTypeInfo typeInfo) : base(typeInfo) { }

    private MinamoPriorityQueue(MinamoPriorityQueueTypeInfo typeInfo, IEnumerable<Entry> source, long nextSequence)
        : base(typeInfo)
    {
        entries.AddRange(source);
        this.nextSequence = nextSequence;
    }

    internal int Count => entries.Count;

    public override MinamoObject Clone() =>
        new MinamoPriorityQueue((MinamoPriorityQueueTypeInfo)TypeInfo, entries, nextSequence);

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => entries.GetHashCode();

    public override object ToObject() => Values().Select(value => value.ToObject()).ToArray();

    public override string ToString() => $"PriorityQueue({Count})";

    internal void Enqueue(MinamoObject value, MinamoObject priority)
    {
        entries.Add(new(value, priority, nextSequence++));
        SiftUp(entries.Count - 1);
    }

    internal bool TryPeek(out MinamoObject value, out MinamoObject priority)
    {
        if (TryGetNext(out var entry))
        {
            value = entry.Value;
            priority = entry.Priority;
            return true;
        }

        value = Nil;
        priority = Nil;
        return false;
    }

    internal bool TryDequeue(out MinamoObject value, out MinamoObject priority)
    {
        if (entries.Count == 0)
        {
            value = Nil;
            priority = Nil;
            return false;
        }

        var entry = entries[0];
        var lastIndex = entries.Count - 1;
        var last = entries[lastIndex];
        entries.RemoveAt(lastIndex);
        if (entries.Count > 0)
        {
            entries[0] = last;
            SiftDown(0);
        }

        value = entry.Value;
        priority = entry.Priority;
        return true;
    }

    internal void Clear() => entries.Clear();

    internal IEnumerable<MinamoObject> Values()
    {
        var snapshot = new MinamoPriorityQueue(
            (MinamoPriorityQueueTypeInfo)TypeInfo,
            entries,
            nextSequence);

        while (snapshot.TryDequeue(out var value, out _))
        {
            yield return value;
        }
    }

    private bool TryGetNext(out Entry entry)
    {
        if (entries.Count > 0)
        {
            entry = entries[0];
            return true;
        }

        entry = null!;
        return false;
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            var parent = (index - 1) / 2;
            if (Compare(entries[index], entries[parent]) >= 0)
            {
                break;
            }

            (entries[index], entries[parent]) = (entries[parent], entries[index]);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        while (true)
        {
            var left = index * 2 + 1;
            if (left >= entries.Count)
            {
                return;
            }

            var right = left + 1;
            var next = right < entries.Count && Compare(entries[right], entries[left]) < 0
                ? right
                : left;
            if (Compare(entries[index], entries[next]) <= 0)
            {
                return;
            }

            (entries[index], entries[next]) = (entries[next], entries[index]);
            index = next;
        }
    }

    private int Compare(Entry left, Entry right)
    {
        var result = comparer.Compare(left.Priority, right.Priority);
        return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
    }
}

[MinamoType]
public sealed partial class MinamoPriorityQueueTypeInfo : MinamoForeignTypeInfo
{
    public override string ReflectedTypeName => "PriorityQueue";

    public MinamoPriorityQueueTypeInfo() => AddMixins(MinamoTypeCodes.Lookup, MinamoTypeCodes.Sequence);

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        MinamoInteger.Get(((MinamoPriorityQueue)arg).Count);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) =>
        new MinamoString(arg.ToString());

    protected override MinamoObject IterateOp(ExecutionContext ctx, MinamoObject self) =>
        MinamoIterator.Create(((MinamoPriorityQueue)self).Values());

    [MinamoProperty]
    internal static int Count(MinamoPriorityQueue self) => self.Count;

    [MinamoMethod]
    internal static void Enqueue(MinamoPriorityQueue self, MinamoObject value, MinamoObject priority) =>
        self.Enqueue(value, priority);

    [MinamoMethod]
    internal static MinamoObject Peek(MinamoPriorityQueue self) =>
        self.TryPeek(out var value, out var priority) ? Pair(value, priority) : Nil;

    [MinamoMethod]
    internal static MinamoObject Dequeue(MinamoPriorityQueue self) =>
        self.TryDequeue(out var value, out var priority) ? Pair(value, priority) : Nil;

    [MinamoMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(MinamoPriorityQueue self) => self.Clear();

    [MinamoStaticMethod("PriorityQueue")]
    internal static MinamoObject New(ExecutionContext ctx, [Default] MinamoObject values)
    {
        var result = new MinamoPriorityQueue(ctx.Type<MinamoPriorityQueueTypeInfo>());
        if (values is null || values.TypeId == MinamoTypeCodes.Nil)
        {
            return result;
        }

        foreach (var item in MinamoIterator.ToEnumerable(ctx, values))
        {
            if (ctx.HasErrors)
            {
                return Nil;
            }

            if (item is not MinamoTuple pair || pair.Count < 2)
            {
                return ctx.InvalidValue(item);
            }

            result.Enqueue(pair[0], pair[1]);
        }

        return result;
    }

    private static MinamoObject Pair(MinamoObject value, MinamoObject priority) =>
        MinamoTuple.Create(new("value", value), new("priority", priority));
}
