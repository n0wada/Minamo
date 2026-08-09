using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Collections;

public sealed class SpellkitDeque : SpellkitForeignObject
{
    private readonly LinkedList<SpellkitObject> items = new();

    internal SpellkitDeque(SpellkitDequeTypeInfo typeInfo) : base(typeInfo) { }

    private SpellkitDeque(SpellkitDequeTypeInfo typeInfo, IEnumerable<SpellkitObject> values) : this(typeInfo)
    {
        foreach (var value in values)
        {
            items.AddLast(value);
        }
    }

    internal int Count => items.Count;

    public override SpellkitObject Clone() => new SpellkitDeque((SpellkitDequeTypeInfo)TypeInfo, items);

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => items.GetHashCode();

    public override object ToObject() => items.Select(value => value.ToObject()).ToArray();

    public override string ToString() => $"Deque({Count})";

    internal void PushFront(SpellkitObject value) => items.AddFirst(value);

    internal void PushBack(SpellkitObject value) => items.AddLast(value);

    internal SpellkitObject PopFront()
    {
        if (items.First is not { } first)
        {
            return Nil;
        }

        items.RemoveFirst();
        return first.Value;
    }

    internal SpellkitObject PopBack()
    {
        if (items.Last is not { } last)
        {
            return Nil;
        }

        items.RemoveLast();
        return last.Value;
    }

    internal SpellkitObject First() => items.First?.Value ?? Nil;

    internal SpellkitObject Last() => items.Last?.Value ?? Nil;

    internal void Clear() => items.Clear();

    internal IEnumerable<SpellkitObject> Values() => items;
}

[SpellkitType]
public sealed partial class SpellkitDequeTypeInfo : SpellkitForeignTypeInfo
{
    public override string ReflectedTypeName => "Deque";

    public SpellkitDequeTypeInfo() => AddMixins(SpellkitTypeCodes.Lookup, SpellkitTypeCodes.Sequence);

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitDeque)arg).Count);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        new SpellkitString(arg.ToString());

    protected override SpellkitObject IterateOp(ExecutionContext ctx, SpellkitObject self) =>
        SpellkitIterator.Create(((SpellkitDeque)self).Values());

    [SpellkitProperty]
    internal static int Count(SpellkitDeque self) => self.Count;

    [SpellkitProperty]
    internal static bool IsEmpty(SpellkitDeque self) => self.Count == 0;

    [SpellkitMethod]
    internal static void PushFront(SpellkitDeque self, SpellkitObject value) => self.PushFront(value);

    [SpellkitMethod]
    internal static void PushBack(SpellkitDeque self, SpellkitObject value) => self.PushBack(value);

    [SpellkitMethod]
    internal static SpellkitObject PopFront(SpellkitDeque self) => self.PopFront();

    [SpellkitMethod]
    internal static SpellkitObject PopBack(SpellkitDeque self) => self.PopBack();

    [SpellkitMethod(BuiltinMethodNames.First)]
    internal static SpellkitObject First(SpellkitDeque self) => self.First();

    [SpellkitMethod(BuiltinMethodNames.Last)]
    internal static SpellkitObject Last(SpellkitDeque self) => self.Last();

    [SpellkitMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(SpellkitDeque self) => self.Clear();

    [SpellkitStaticMethod("Deque")]
    internal static SpellkitObject New(ExecutionContext ctx, [Default] SpellkitObject values)
    {
        if (values is null || values.TypeId == SpellkitTypeCodes.Nil)
        {
            return new SpellkitDeque(ctx.Type<SpellkitDequeTypeInfo>());
        }

        var result = new SpellkitDeque(ctx.Type<SpellkitDequeTypeInfo>());
        foreach (var value in SpellkitIterator.ToEnumerable(ctx, values))
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
