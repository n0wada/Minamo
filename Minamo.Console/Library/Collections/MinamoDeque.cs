using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Library.Collections;

public sealed class MinamoDeque : MinamoForeignObject
{
    private readonly LinkedList<MinamoObject> items = new();

    internal MinamoDeque(MinamoDequeTypeInfo typeInfo) : base(typeInfo) { }

    private MinamoDeque(MinamoDequeTypeInfo typeInfo, IEnumerable<MinamoObject> values) : this(typeInfo)
    {
        foreach (var value in values)
        {
            items.AddLast(value);
        }
    }

    internal int Count => items.Count;

    public override MinamoObject Clone() => new MinamoDeque((MinamoDequeTypeInfo)TypeInfo, items);

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => items.GetHashCode();

    public override object ToObject() => items.Select(value => value.ToObject()).ToArray();

    public override string ToString() => $"Deque({Count})";

    internal void PushFront(MinamoObject value) => items.AddFirst(value);

    internal void PushBack(MinamoObject value) => items.AddLast(value);

    internal MinamoObject PopFront()
    {
        if (items.First is not { } first)
        {
            return Nil;
        }

        items.RemoveFirst();
        return first.Value;
    }

    internal MinamoObject PopBack()
    {
        if (items.Last is not { } last)
        {
            return Nil;
        }

        items.RemoveLast();
        return last.Value;
    }

    internal MinamoObject First() => items.First?.Value ?? Nil;

    internal MinamoObject Last() => items.Last?.Value ?? Nil;

    internal void Clear() => items.Clear();

    internal IEnumerable<MinamoObject> Values() => items;
}

[MinamoType]
public sealed partial class MinamoDequeTypeInfo : MinamoForeignTypeInfo
{
    public override string ReflectedTypeName => "Deque";

    public MinamoDequeTypeInfo() => AddMixins(MinamoTypeCodes.Lookup, MinamoTypeCodes.Sequence);

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        MinamoInteger.Get(((MinamoDeque)arg).Count);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) =>
        new MinamoString(arg.ToString());

    protected override MinamoObject IterateOp(ExecutionContext ctx, MinamoObject self) =>
        MinamoIterator.Create(((MinamoDeque)self).Values());

    [MinamoProperty]
    internal static int Count(MinamoDeque self) => self.Count;

    [MinamoProperty]
    internal static bool IsEmpty(MinamoDeque self) => self.Count == 0;

    [MinamoMethod]
    internal static void PushFront(MinamoDeque self, MinamoObject value) => self.PushFront(value);

    [MinamoMethod]
    internal static void PushBack(MinamoDeque self, MinamoObject value) => self.PushBack(value);

    [MinamoMethod]
    internal static MinamoObject PopFront(MinamoDeque self) => self.PopFront();

    [MinamoMethod]
    internal static MinamoObject PopBack(MinamoDeque self) => self.PopBack();

    [MinamoMethod(BuiltinMethodNames.First)]
    internal static MinamoObject First(MinamoDeque self) => self.First();

    [MinamoMethod(BuiltinMethodNames.Last)]
    internal static MinamoObject Last(MinamoDeque self) => self.Last();

    [MinamoMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(MinamoDeque self) => self.Clear();

    [MinamoStaticMethod("Deque")]
    internal static MinamoObject New(ExecutionContext ctx, [Default] MinamoObject values)
    {
        if (values is null || values.TypeId == MinamoTypeCodes.Nil)
        {
            return new MinamoDeque(ctx.Type<MinamoDequeTypeInfo>());
        }

        var result = new MinamoDeque(ctx.Type<MinamoDequeTypeInfo>());
        foreach (var value in MinamoIterator.ToEnumerable(ctx, values))
        {
            if (ctx.HasErrors)
            {
                return Nil;
            }

            result.PushBack(value);
        }

        return result;
    }
}
