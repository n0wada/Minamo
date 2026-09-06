using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Library.Collections;

[MinamoType]
public sealed partial class MinamoSortedDictionaryTypeInfo : MinamoForeignTypeInfo
{
    public override string ReflectedTypeName => "SortedDictionary";

    public MinamoSortedDictionaryTypeInfo() => AddMixins(MinamoTypeCodes.Lookup, MinamoTypeCodes.Sequence);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format)
    {
        var self = (MinamoSortedDictionary)arg;
        return MinamoString.Get("[" + ToLiteral(ctx, self.Items) + "]");
    }

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        MinamoInteger.Get(((MinamoSortedDictionary)arg).Items.Count);

    protected override MinamoObject IterateOp(ExecutionContext ctx, MinamoObject self) =>
        MinamoIterator.Create(((MinamoSortedDictionary)self).Items.Select(Pair));

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index) =>
        ((MinamoSortedDictionary)self).Items.TryGetValue(index, out var value)
            ? value
            : ctx.KeyNotFound(index);

    protected override MinamoObject SetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index, MinamoObject value)
    {
        ((MinamoSortedDictionary)self).Items[index] = value;
        return Nil;
    }

    [MinamoProperty]
    internal static int Count(MinamoSortedDictionary self) => self.Items.Count;

    [MinamoProperty]
    internal static MinamoObject Keys(MinamoSortedDictionary self) =>
        new MinamoArray(self.Items.Keys.ToArray());

    [MinamoProperty]
    internal static MinamoObject Values(MinamoSortedDictionary self) =>
        new MinamoArray(self.Items.Values.ToArray());

    [MinamoMethod(BuiltinMethodNames.Add)]
    internal static void Add(ExecutionContext ctx, MinamoSortedDictionary self, MinamoObject key, MinamoObject value)
    {
        if (!self.Items.TryAdd(key, value))
        {
            ctx.KeyAlreadyPresent(key);
        }
    }

    [MinamoMethod(BuiltinMethodNames.TryAdd)]
    internal static bool TryAdd(MinamoSortedDictionary self, MinamoObject key, MinamoObject value) =>
        self.Items.TryAdd(key, value);

    [MinamoMethod]
    internal static MinamoObject Get(MinamoSortedDictionary self, MinamoObject key, [ParameterName("default")] MinamoObject fallback = null!) =>
        self.Items.TryGetValue(key, out var value)
            ? value
            : fallback ?? Nil;

    [MinamoMethod(BuiltinMethodNames.TryGet)]
    internal static MinamoObject? TryGet(MinamoSortedDictionary self, MinamoObject key) =>
        self.Items.TryGetValue(key, out var value) ? value : null;

    [MinamoMethod(BuiltinMethodNames.Remove)]
    internal static bool Remove(MinamoSortedDictionary self, MinamoObject key) =>
        self.Items.Remove(key);

    [MinamoMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(MinamoSortedDictionary self) => self.Items.Clear();

    [MinamoMethod]
    internal static bool ContainsKey(MinamoSortedDictionary self, MinamoObject key) =>
        self.Items.ContainsKey(key);

    [MinamoMethod]
    internal static bool ContainsValue(MinamoSortedDictionary self, MinamoObject value) =>
        self.Items.ContainsValue(value);

    [MinamoMethod(BuiltinMethodNames.First)]
    internal static MinamoObject First(MinamoSortedDictionary self) =>
        self.Items.Count == 0 ? Nil : Pair(self.Items.First());

    [MinamoMethod(BuiltinMethodNames.Last)]
    internal static MinamoObject Last(MinamoSortedDictionary self) =>
        self.Items.Count == 0 ? Nil : Pair(self.Items.Last());

    [MinamoMethod]
    internal static MinamoObject Range(
        MinamoSortedDictionary self,
        MinamoObject from = null!,
        MinamoObject to = null!,
        bool includeFrom = true,
        bool includeTo = true)
    {
        var comparer = self.Items.Comparer;
        var hasFrom = from is not null && from.TypeId != MinamoTypeCodes.Nil;
        var hasTo = to is not null && to.TypeId != MinamoTypeCodes.Nil;

        IEnumerable<MinamoObject> Iterate()
        {
            foreach (var item in self.Items)
            {
                if (hasFrom)
                {
                    var compared = comparer.Compare(item.Key, from);
                    if (compared < 0 || compared == 0 && !includeFrom)
                    {
                        continue;
                    }
                }

                if (hasTo)
                {
                    var compared = comparer.Compare(item.Key, to);
                    if (compared > 0 || compared == 0 && !includeTo)
                    {
                        continue;
                    }
                }

                yield return Pair(item);
            }
        }

        return MinamoIterator.Create(Iterate());
    }

    [MinamoMethod(BuiltinMethodNames.ToDictionary)]
    internal static MinamoObject ToDictionary(MinamoSortedDictionary self) =>
        TypeConverter.ConvertFrom(self.Items.ToDictionary(kv => kv.Key, kv => kv.Value));

    [MinamoStaticMethod("SortedDictionary")]
    internal static MinamoObject New(ExecutionContext ctx, [Default] MinamoObject values)
    {
        var result = new MinamoSortedDictionary(ctx.Type<MinamoSortedDictionaryTypeInfo>());
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

            if (item is MinamoTuple pair && pair.Count >= 2)
            {
                result.Items[pair[0]] = pair[1];
            }
            else
            {
                return ctx.InvalidValue(item);
            }
        }

        return result;
    }

    private static MinamoObject Pair(KeyValuePair<MinamoObject, MinamoObject> item) =>
        MinamoTuple.Create(new("key", item.Key), new("value", item.Value));

    private static string ToLiteral(ExecutionContext ctx, IEnumerable<KeyValuePair<MinamoObject, MinamoObject>> values) =>
        string.Join(", ", values.Select(kv => kv.Key.ToLiteral(ctx) + ": " + kv.Value.ToLiteral(ctx)));
}
