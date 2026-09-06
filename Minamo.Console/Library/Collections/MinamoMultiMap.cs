using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Library.Collections;

public sealed class MinamoMultiMap : MinamoForeignObject
{
    internal readonly OrderedDictionary<MinamoObject, List<MinamoObject>> Items = new();

    internal MinamoMultiMap(MinamoMultiMapTypeInfo typeInfo) : base(typeInfo) { }

    private MinamoMultiMap(MinamoMultiMapTypeInfo typeInfo, OrderedDictionary<MinamoObject, List<MinamoObject>> source)
        : this(typeInfo)
    {
        foreach (var (key, values) in source)
        {
            Items.Add(key, [.. values]);
        }
    }

    internal int Count => Items.Values.Sum(values => values.Count);

    public override MinamoObject Clone() => new MinamoMultiMap((MinamoMultiMapTypeInfo)TypeInfo, Items);

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => Items.GetHashCode();

    public override object ToObject() => Items;

    public override string ToString() => $"MultiMap({Count})";

    internal void Add(MinamoObject key, MinamoObject value)
    {
        if (!Items.TryGetValue(key, out var values))
        {
            values = [];
            Items.Add(key, values);
        }

        values.Add(value);
    }

    internal MinamoArray Get(MinamoObject key) =>
        Items.TryGetValue(key, out var values)
            ? new MinamoArray(values.ToArray())
            : new MinamoArray(Array.Empty<MinamoObject>());

    internal bool Remove(MinamoObject key, MinamoObject value)
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

    internal bool RemoveKey(MinamoObject key) => Items.Remove(key);

    internal bool Contains(MinamoObject key, MinamoObject value) =>
        Items.TryGetValue(key, out var values) && values.Contains(value);

    internal void Clear() => Items.Clear();

    internal IEnumerable<MinamoObject> Pairs()
    {
        foreach (var (key, values) in Items)
        {
            foreach (var value in values)
            {
                yield return MinamoTuple.Create(new("key", key), new("value", value));
            }
        }
    }
}

[MinamoType]
public sealed partial class MinamoMultiMapTypeInfo : MinamoForeignTypeInfo
{
    public override string ReflectedTypeName => "MultiMap";

    public MinamoMultiMapTypeInfo() => AddMixins(MinamoTypeCodes.Lookup, MinamoTypeCodes.Sequence);

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        MinamoInteger.Get(((MinamoMultiMap)arg).Count);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) =>
        new MinamoString(arg.ToString());

    protected override MinamoObject IterateOp(ExecutionContext ctx, MinamoObject self) =>
        MinamoIterator.Create(((MinamoMultiMap)self).Pairs());

    [MinamoProperty]
    internal static int Count(MinamoMultiMap self) => self.Count;

    [MinamoProperty]
    internal static int KeyCount(MinamoMultiMap self) => self.Items.Count;

    [MinamoProperty]
    internal static MinamoObject Keys(MinamoMultiMap self) =>
        new MinamoArray(self.Items.Keys.ToArray());

    [MinamoMethod(BuiltinMethodNames.Add)]
    internal static void Add(MinamoMultiMap self, MinamoObject key, MinamoObject value) => self.Add(key, value);

    [MinamoMethod]
    internal static MinamoObject Get(MinamoMultiMap self, MinamoObject key) => self.Get(key);

    [MinamoMethod(BuiltinMethodNames.Remove)]
    internal static bool Remove(MinamoMultiMap self, MinamoObject key, MinamoObject value) =>
        self.Remove(key, value);

    [MinamoMethod]
    internal static bool RemoveKey(MinamoMultiMap self, MinamoObject key) => self.RemoveKey(key);

    [MinamoMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(MinamoMultiMap self) => self.Clear();

    [MinamoMethod]
    internal static bool ContainsKey(MinamoMultiMap self, MinamoObject key) => self.Items.ContainsKey(key);

    [MinamoMethod]
    internal static bool Contains(MinamoMultiMap self, MinamoObject key, MinamoObject value) =>
        self.Contains(key, value);

    [MinamoStaticMethod("MultiMap")]
    internal static MinamoObject New(ExecutionContext ctx, [Default] MinamoObject values)
    {
        var result = new MinamoMultiMap(ctx.Type<MinamoMultiMapTypeInfo>());
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

            result.Add(pair[0], pair[1]);
        }

        return result;
    }
}
