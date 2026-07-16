using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Collections;

[SpkType]
public sealed partial class SpkSortedDictionaryTypeInfo : SpkForeignTypeInfo
{
    public override string ReflectedTypeName => "SortedDictionary";

    public SpkSortedDictionaryTypeInfo() => AddMixins(Spk.Lookup, Spk.Sequence);

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format)
    {
        var self = (SpkSortedDictionary)arg;
        return SpkString.Get("[" + ToLiteral(ctx, self.Items) + "]");
    }

    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject arg) =>
        SpkInteger.Get(((SpkSortedDictionary)arg).Items.Count);

    protected override SpkObject IterateOp(ExecutionContext ctx, SpkObject self) =>
        SpkIterator.Create(((SpkSortedDictionary)self).Items.Select(Pair));

    protected override SpkObject GetOp(ExecutionContext ctx, SpkObject self, SpkObject index) =>
        ((SpkSortedDictionary)self).Items.TryGetValue(index, out var value)
            ? value
            : ctx.KeyNotFound(index);

    protected override SpkObject SetOp(ExecutionContext ctx, SpkObject self, SpkObject index, SpkObject value)
    {
        ((SpkSortedDictionary)self).Items[index] = value;
        return Nil;
    }

    [SpkProperty]
    internal static int Count(SpkSortedDictionary self) => self.Items.Count;

    [SpkProperty]
    internal static SpkObject Keys(SpkSortedDictionary self) =>
        new SpkArray(self.Items.Keys.ToArray());

    [SpkProperty]
    internal static SpkObject Values(SpkSortedDictionary self) =>
        new SpkArray(self.Items.Values.ToArray());

    [SpkMethod(BuiltinMethodNames.Add)]
    internal static void Add(ExecutionContext ctx, SpkSortedDictionary self, SpkObject key, SpkObject value)
    {
        if (!self.Items.TryAdd(key, value))
        {
            ctx.KeyAlreadyPresent(key);
        }
    }

    [SpkMethod(BuiltinMethodNames.TryAdd)]
    internal static bool TryAdd(SpkSortedDictionary self, SpkObject key, SpkObject value) =>
        self.Items.TryAdd(key, value);

    [SpkMethod]
    internal static SpkObject Get(SpkSortedDictionary self, SpkObject key, [ParameterName("default")] SpkObject fallback = null!) =>
        self.Items.TryGetValue(key, out var value)
            ? value
            : fallback ?? Nil;

    [SpkMethod(BuiltinMethodNames.TryGet)]
    internal static SpkObject? TryGet(SpkSortedDictionary self, SpkObject key) =>
        self.Items.TryGetValue(key, out var value) ? value : null;

    [SpkMethod(BuiltinMethodNames.Remove)]
    internal static bool Remove(SpkSortedDictionary self, SpkObject key) =>
        self.Items.Remove(key);

    [SpkMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(SpkSortedDictionary self) => self.Items.Clear();

    [SpkMethod]
    internal static bool ContainsKey(SpkSortedDictionary self, SpkObject key) =>
        self.Items.ContainsKey(key);

    [SpkMethod]
    internal static bool ContainsValue(SpkSortedDictionary self, SpkObject value) =>
        self.Items.ContainsValue(value);

    [SpkMethod(BuiltinMethodNames.First)]
    internal static SpkObject First(SpkSortedDictionary self) =>
        self.Items.Count == 0 ? Nil : Pair(self.Items.First());

    [SpkMethod(BuiltinMethodNames.Last)]
    internal static SpkObject Last(SpkSortedDictionary self) =>
        self.Items.Count == 0 ? Nil : Pair(self.Items.Last());

    [SpkMethod]
    internal static SpkObject Range(
        SpkSortedDictionary self,
        SpkObject from = null!,
        SpkObject to = null!,
        bool includeFrom = true,
        bool includeTo = true)
    {
        var comparer = self.Items.Comparer;
        var hasFrom = from is not null && from.TypeId != Spk.Nil;
        var hasTo = to is not null && to.TypeId != Spk.Nil;

        IEnumerable<SpkObject> Iterate()
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

        return SpkIterator.Create(Iterate());
    }

    [SpkMethod(BuiltinMethodNames.ToDictionary)]
    internal static SpkObject ToDictionary(SpkSortedDictionary self) =>
        TypeConverter.ConvertFrom(self.Items.ToDictionary(kv => kv.Key, kv => kv.Value));

    [SpkStaticMethod("SortedDictionary")]
    internal static SpkObject New(ExecutionContext ctx, [Default] SpkObject values)
    {
        var result = new SpkSortedDictionary(ctx.Type<SpkSortedDictionaryTypeInfo>());
        if (values is null || values.TypeId == Spk.Nil)
        {
            return result;
        }

        foreach (var item in SpkIterator.ToEnumerable(ctx, values))
        {
            if (ctx.HasErrors)
            {
                return Nil;
            }

            if (item is SpkTuple pair && pair.Count >= 2)
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

    private static SpkObject Pair(KeyValuePair<SpkObject, SpkObject> item) =>
        SpkTuple.Create(new("key", item.Key), new("value", item.Value));

    private static string ToLiteral(ExecutionContext ctx, IEnumerable<KeyValuePair<SpkObject, SpkObject>> values) =>
        string.Join(", ", values.Select(kv => kv.Key.ToLiteral(ctx) + ": " + kv.Value.ToLiteral(ctx)));
}
