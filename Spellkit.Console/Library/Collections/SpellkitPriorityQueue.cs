using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Collections;

public sealed class SpellkitPriorityQueue : SpellkitForeignObject
{
    private sealed record Entry(SpellkitObject Value, SpellkitObject Priority, long Sequence);

    private readonly List<Entry> entries = [];
    private readonly SpellkitCollectionObjectComparer comparer = new();
    private long nextSequence;

    internal SpellkitPriorityQueue(SpellkitPriorityQueueTypeInfo typeInfo) : base(typeInfo) { }

    private SpellkitPriorityQueue(SpellkitPriorityQueueTypeInfo typeInfo, IEnumerable<Entry> source, long nextSequence)
        : base(typeInfo)
    {
        entries.AddRange(source);
        this.nextSequence = nextSequence;
    }

    internal int Count => entries.Count;

    public override SpellkitObject Clone() =>
        new SpellkitPriorityQueue((SpellkitPriorityQueueTypeInfo)TypeInfo, entries, nextSequence);

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => entries.GetHashCode();

    public override object ToObject() => Values().Select(value => value.ToObject()).ToArray();

    public override string ToString() => $"PriorityQueue({Count})";

    internal void Enqueue(SpellkitObject value, SpellkitObject priority)
    {
        entries.Add(new(value, priority, nextSequence++));
        SiftUp(entries.Count - 1);
    }

    internal bool TryPeek(out SpellkitObject value, out SpellkitObject priority)
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

    internal bool TryDequeue(out SpellkitObject value, out SpellkitObject priority)
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

    internal IEnumerable<SpellkitObject> Values()
    {
        var snapshot = new SpellkitPriorityQueue(
            (SpellkitPriorityQueueTypeInfo)TypeInfo,
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

[SpellkitType]
public sealed partial class SpellkitPriorityQueueTypeInfo : SpellkitForeignTypeInfo
{
    public override string ReflectedTypeName => "PriorityQueue";

    public SpellkitPriorityQueueTypeInfo() => AddMixins(SpellkitTypeCodes.Lookup, SpellkitTypeCodes.Sequence);

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitPriorityQueue)arg).Count);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        new SpellkitString(arg.ToString());

    protected override SpellkitObject IterateOp(ExecutionContext ctx, SpellkitObject self) =>
        SpellkitIterator.Create(((SpellkitPriorityQueue)self).Values());

    [SpellkitProperty]
    internal static int Count(SpellkitPriorityQueue self) => self.Count;

    [SpellkitMethod]
    internal static void Enqueue(SpellkitPriorityQueue self, SpellkitObject value, SpellkitObject priority) =>
        self.Enqueue(value, priority);

    [SpellkitMethod]
    internal static SpellkitObject Peek(SpellkitPriorityQueue self) =>
        self.TryPeek(out var value, out var priority) ? Pair(value, priority) : Nil;

    [SpellkitMethod]
    internal static SpellkitObject Dequeue(SpellkitPriorityQueue self) =>
        self.TryDequeue(out var value, out var priority) ? Pair(value, priority) : Nil;

    [SpellkitMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(SpellkitPriorityQueue self) => self.Clear();

    [SpellkitStaticMethod("PriorityQueue")]
    internal static SpellkitObject New(ExecutionContext ctx, [Default] SpellkitObject values)
    {
        var result = new SpellkitPriorityQueue(ctx.Type<SpellkitPriorityQueueTypeInfo>());
        if (values is null || values.TypeId == SpellkitTypeCodes.Nil)
        {
            return result;
        }

        foreach (var item in SpellkitIterator.ToEnumerable(ctx, values))
        {
            if (ctx.HasErrors)
            {
                return Nil;
            }

            if (item is not SpellkitTuple pair || pair.Count < 2)
            {
                return ctx.InvalidValue(item);
            }

            result.Enqueue(pair[0], pair[1]);
        }

        return result;
    }

    private static SpellkitObject Pair(SpellkitObject value, SpellkitObject priority) =>
        SpellkitTuple.Create(new("value", value), new("priority", priority));
}
