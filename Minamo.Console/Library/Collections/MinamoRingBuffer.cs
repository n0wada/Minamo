using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Library.Collections;

public sealed class MinamoRingBuffer : MinamoForeignObject
{
    private readonly MinamoObject[] buffer;
    private int start;
    private int count;

    internal MinamoRingBuffer(MinamoRingBufferTypeInfo typeInfo, int capacity) : base(typeInfo) =>
        buffer = new MinamoObject[capacity];

    private MinamoRingBuffer(MinamoRingBufferTypeInfo typeInfo, int capacity, IEnumerable<MinamoObject> values)
        : this(typeInfo, capacity)
    {
        foreach (var value in values)
        {
            Add(value);
        }
    }

    internal int Capacity => buffer.Length;

    internal int Count => count;

    public override MinamoObject Clone() =>
        new MinamoRingBuffer((MinamoRingBufferTypeInfo)TypeInfo, Capacity, Values());

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => buffer.GetHashCode();

    public override object ToObject() => Values().Select(value => value.ToObject()).ToArray();

    public override string ToString() => $"RingBuffer({Count}/{Capacity})";

    internal void Add(MinamoObject value)
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

    internal MinamoObject First() => count == 0 ? Nil : buffer[start];

    internal MinamoObject Last() => count == 0 ? Nil : buffer[(start + count - 1) % Capacity];

    internal void Clear()
    {
        Array.Clear(buffer);
        start = 0;
        count = 0;
    }

    internal IEnumerable<MinamoObject> Values()
    {
        for (var i = 0; i < count; i++)
        {
            yield return buffer[(start + i) % Capacity];
        }
    }
}

[MinamoType]
public sealed partial class MinamoRingBufferTypeInfo : MinamoForeignTypeInfo
{
    public override string ReflectedTypeName => "RingBuffer";

    public MinamoRingBufferTypeInfo() => AddMixins(MinamoTypeCodes.Lookup, MinamoTypeCodes.Sequence);

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        MinamoInteger.Get(((MinamoRingBuffer)arg).Count);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) =>
        new MinamoString(arg.ToString());

    protected override MinamoObject IterateOp(ExecutionContext ctx, MinamoObject self) =>
        MinamoIterator.Create(((MinamoRingBuffer)self).Values());

    [MinamoProperty]
    internal static int Count(MinamoRingBuffer self) => self.Count;

    [MinamoProperty]
    internal static int Capacity(MinamoRingBuffer self) => self.Capacity;

    [MinamoMethod(BuiltinMethodNames.Add)]
    internal static void Add(MinamoRingBuffer self, MinamoObject value) => self.Add(value);

    [MinamoMethod(BuiltinMethodNames.First)]
    internal static MinamoObject First(MinamoRingBuffer self) => self.First();

    [MinamoMethod(BuiltinMethodNames.Last)]
    internal static MinamoObject Last(MinamoRingBuffer self) => self.Last();

    [MinamoMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(MinamoRingBuffer self) => self.Clear();

    [MinamoStaticMethod("RingBuffer")]
    internal static MinamoObject New(ExecutionContext ctx, int capacity, [Default] MinamoObject values)
    {
        if (capacity <= 0)
        {
            return ctx.InvalidValue(MinamoInteger.Get(capacity));
        }

        var result = new MinamoRingBuffer(ctx.Type<MinamoRingBufferTypeInfo>(), capacity);
        if (values is null || values.TypeId == MinamoTypeCodes.Nil)
        {
            return result;
        }

        foreach (var value in MinamoIterator.ToEnumerable(ctx, values))
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
