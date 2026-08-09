using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Collections;

public sealed class SpellkitMultiMap : SpellkitForeignObject
{
    internal readonly OrderedDictionary<SpellkitObject, List<SpellkitObject>> Items = new();

    internal SpellkitMultiMap(SpellkitMultiMapTypeInfo typeInfo) : base(typeInfo) { }

    private SpellkitMultiMap(SpellkitMultiMapTypeInfo typeInfo, OrderedDictionary<SpellkitObject, List<SpellkitObject>> source)
        : this(typeInfo)
    {
        foreach (var (key, values) in source)
        {
            Items.Add(key, [.. values]);
        }
    }

    internal int Count => Items.Values.Sum(values => values.Count);

    public override SpellkitObject Clone() => new SpellkitMultiMap((SpellkitMultiMapTypeInfo)TypeInfo, Items);

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => Items.GetHashCode();

    public override object ToObject() => Items;

    public override string ToString() => $"MultiMap({Count})";

    internal void Add(SpellkitObject key, SpellkitObject value)
    {
        if (!Items.TryGetValue(key, out var values))
        {
            values = [];
            Items.Add(key, values);
        }

        values.Add(value);
    }

    internal SpellkitArray Get(SpellkitObject key) =>
        Items.TryGetValue(key, out var values)
            ? new SpellkitArray(values.ToArray())
            : new SpellkitArray(Array.Empty<SpellkitObject>());

    internal bool Remove(SpellkitObject key, SpellkitObject value)
    {
        if (!Items.TryGetValue(key, out var values) || !values.Remove(value))
        {
            return false;
        }

        if (values.Count == 0)
        {
            Items.Remove(key);
        }

        return true;
    }

    internal bool RemoveKey(SpellkitObject key) => Items.Remove(key);

    internal bool Contains(SpellkitObject key, SpellkitObject value) =>
        Items.TryGetValue(key, out var values) && values.Contains(value);

    internal void Clear() => Items.Clear();

    internal IEnumerable<SpellkitObject> Pairs()
    {
        foreach (var (key, values) in Items)
        {
            foreach (var value in values)
            {
                yield return SpellkitTuple.Create(new("key", key), new("value", value));
            }
        }
    }
}

[SpellkitType]
public sealed partial class SpellkitMultiMapTypeInfo : SpellkitForeignTypeInfo
{
    public override string ReflectedTypeName => "MultiMap";

    public SpellkitMultiMapTypeInfo() => AddMixins(SpellkitTypeCodes.Lookup, SpellkitTypeCodes.Sequence);

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitMultiMap)arg).Count);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        new SpellkitString(arg.ToString());

    protected override SpellkitObject IterateOp(ExecutionContext ctx, SpellkitObject self) =>
        SpellkitIterator.Create(((SpellkitMultiMap)self).Pairs());

    [SpellkitProperty]
    internal static int Count(SpellkitMultiMap self) => self.Count;

    [SpellkitProperty]
    internal static int KeyCount(SpellkitMultiMap self) => self.Items.Count;

    [SpellkitProperty]
    internal static SpellkitObject Keys(SpellkitMultiMap self) =>
        new SpellkitArray(self.Items.Keys.ToArray());

    [SpellkitMethod(BuiltinMethodNames.Add)]
    internal static void Add(SpellkitMultiMap self, SpellkitObject key, SpellkitObject value) => self.Add(key, value);

    [SpellkitMethod]
    internal static SpellkitObject Get(SpellkitMultiMap self, SpellkitObject key) => self.Get(key);

    [SpellkitMethod(BuiltinMethodNames.Remove)]
    internal static bool Remove(SpellkitMultiMap self, SpellkitObject key, SpellkitObject value) =>
        self.Remove(key, value);

    [SpellkitMethod]
    internal static bool RemoveKey(SpellkitMultiMap self, SpellkitObject key) => self.RemoveKey(key);

    [SpellkitMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(SpellkitMultiMap self) => self.Clear();

    [SpellkitMethod]
    internal static bool ContainsKey(SpellkitMultiMap self, SpellkitObject key) => self.Items.ContainsKey(key);

    [SpellkitMethod]
    internal static bool Contains(SpellkitMultiMap self, SpellkitObject key, SpellkitObject value) =>
        self.Contains(key, value);

    [SpellkitStaticMethod("MultiMap")]
    internal static SpellkitObject New(ExecutionContext ctx, [Default] SpellkitObject values)
    {
        var result = new SpellkitMultiMap(ctx.Type<SpellkitMultiMapTypeInfo>());
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

            result.Add(pair[0], pair[1]);
        }

        return result;
    }
}
