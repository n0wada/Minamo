using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Collections;

public sealed class SpellkitRingBuffer : SpellkitForeignObject
{
    private readonly SpellkitObject[] buffer;
    private int start;
    private int count;

    internal SpellkitRingBuffer(SpellkitRingBufferTypeInfo typeInfo, int capacity) : base(typeInfo) =>
        buffer = new SpellkitObject[capacity];

    private SpellkitRingBuffer(SpellkitRingBufferTypeInfo typeInfo, int capacity, IEnumerable<SpellkitObject> values)
        : this(typeInfo, capacity)
    {
        foreach (var value in values)
        {
            Add(value);
        }
    }

    internal int Capacity => buffer.Length;

    internal int Count => count;

    public override SpellkitObject Clone() =>
        new SpellkitRingBuffer((SpellkitRingBufferTypeInfo)TypeInfo, Capacity, Values());

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => buffer.GetHashCode();

    public override object ToObject() => Values().Select(value => value.ToObject()).ToArray();

    public override string ToString() => $"RingBuffer({Count}/{Capacity})";

    internal void Add(SpellkitObject value)
    {
        if (count < Capacity)
        {
            buffer[(start + count) % Capacity] = value;
            count++;
            return;
        }

        buffer[start] = value;
        start = (start + 1) % Capacity;
    }

    internal SpellkitObject First() => count == 0 ? Nil : buffer[start];

    internal SpellkitObject Last() => count == 0 ? Nil : buffer[(start + count - 1) % Capacity];

    internal void Clear()
    {
        Array.Clear(buffer);
        start = 0;
        count = 0;
    }

    internal IEnumerable<SpellkitObject> Values()
    {
        for (var i = 0; i < count; i++)
        {
            yield return buffer[(start + i) % Capacity];
        }
    }
}

[SpellkitType]
public sealed partial class SpellkitRingBufferTypeInfo : SpellkitForeignTypeInfo
{
    public override string ReflectedTypeName => "RingBuffer";

    public SpellkitRingBufferTypeInfo() => AddMixins(SpellkitTypeCodes.Lookup, SpellkitTypeCodes.Sequence);

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitRingBuffer)arg).Count);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        new SpellkitString(arg.ToString());

    protected override SpellkitObject IterateOp(ExecutionContext ctx, SpellkitObject self) =>
        SpellkitIterator.Create(((SpellkitRingBuffer)self).Values());

    [SpellkitProperty]
    internal static int Count(SpellkitRingBuffer self) => self.Count;

    [SpellkitProperty]
    internal static int Capacity(SpellkitRingBuffer self) => self.Capacity;

    [SpellkitMethod(BuiltinMethodNames.Add)]
    internal static void Add(SpellkitRingBuffer self, SpellkitObject value) => self.Add(value);

    [SpellkitMethod(BuiltinMethodNames.First)]
    internal static SpellkitObject First(SpellkitRingBuffer self) => self.First();

    [SpellkitMethod(BuiltinMethodNames.Last)]
    internal static SpellkitObject Last(SpellkitRingBuffer self) => self.Last();

    [SpellkitMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(SpellkitRingBuffer self) => self.Clear();

    [SpellkitStaticMethod("RingBuffer")]
    internal static SpellkitObject New(ExecutionContext ctx, int capacity, [Default] SpellkitObject values)
    {
        if (capacity <= 0)
        {
            return ctx.InvalidValue(SpellkitInteger.Get(capacity));
        }

        var result = new SpellkitRingBuffer(ctx.Type<SpellkitRingBufferTypeInfo>(), capacity);
        if (values is null || values.TypeId == SpellkitTypeCodes.Nil)
        {
            return result;
        }

        foreach (var value in SpellkitIterator.ToEnumerable(ctx, values))
        {
            if (ctx.HasErrors)
            {
                return Nil;
            }

            result.Add(value);
        }

        return result;
    }
}
