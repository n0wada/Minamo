using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Collections;

[SpellkitType]
public sealed partial class SpellkitSortedDictionaryTypeInfo : SpellkitForeignTypeInfo
{
    public override string ReflectedTypeName => "SortedDictionary";

    public SpellkitSortedDictionaryTypeInfo() => AddMixins(SpellkitTypeCodes.Lookup, SpellkitTypeCodes.Sequence);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        var self = (SpellkitSortedDictionary)arg;
        return SpellkitString.Get("[" + ToLiteral(ctx, self.Items) + "]");
    }

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitSortedDictionary)arg).Items.Count);

    protected override SpellkitObject IterateOp(ExecutionContext ctx, SpellkitObject self) =>
        SpellkitIterator.Create(((SpellkitSortedDictionary)self).Items.Select(Pair));

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index) =>
        ((SpellkitSortedDictionary)self).Items.TryGetValue(index, out var value)
            ? value
            : ctx.KeyNotFound(index);

    protected override SpellkitObject SetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index, SpellkitObject value)
    {
        ((SpellkitSortedDictionary)self).Items[index] = value;
        return Nil;
    }

    [SpellkitProperty]
    internal static int Count(SpellkitSortedDictionary self) => self.Items.Count;

    [SpellkitProperty]
    internal static SpellkitObject Keys(SpellkitSortedDictionary self) =>
        new SpellkitArray(self.Items.Keys.ToArray());

    [SpellkitProperty]
    internal static SpellkitObject Values(SpellkitSortedDictionary self) =>
        new SpellkitArray(self.Items.Values.ToArray());

    [SpellkitMethod(BuiltinMethodNames.Add)]
    internal static void Add(ExecutionContext ctx, SpellkitSortedDictionary self, SpellkitObject key, SpellkitObject value)
    {
        if (!self.Items.TryAdd(key, value))
        {
            ctx.KeyAlreadyPresent(key);
        }
    }

    [SpellkitMethod(BuiltinMethodNames.TryAdd)]
    internal static bool TryAdd(SpellkitSortedDictionary self, SpellkitObject key, SpellkitObject value) =>
        self.Items.TryAdd(key, value);

    [SpellkitMethod]
    internal static SpellkitObject Get(SpellkitSortedDictionary self, SpellkitObject key, [ParameterName("default")] SpellkitObject fallback = null!) =>
        self.Items.TryGetValue(key, out var value)
            ? value
            : fallback ?? Nil;

    [SpellkitMethod(BuiltinMethodNames.TryGet)]
    internal static SpellkitObject? TryGet(SpellkitSortedDictionary self, SpellkitObject key) =>
        self.Items.TryGetValue(key, out var value) ? value : null;

    [SpellkitMethod(BuiltinMethodNames.Remove)]
    internal static bool Remove(SpellkitSortedDictionary self, SpellkitObject key) =>
        self.Items.Remove(key);

    [SpellkitMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(SpellkitSortedDictionary self) => self.Items.Clear();

    [SpellkitMethod]
    internal static bool ContainsKey(SpellkitSortedDictionary self, SpellkitObject key) =>
        self.Items.ContainsKey(key);

    [SpellkitMethod]
    internal static bool ContainsValue(SpellkitSortedDictionary self, SpellkitObject value) =>
        self.Items.ContainsValue(value);

    [SpellkitMethod(BuiltinMethodNames.First)]
    internal static SpellkitObject First(SpellkitSortedDictionary self) =>
        self.Items.Count == 0 ? Nil : Pair(self.Items.First());

    [SpellkitMethod(BuiltinMethodNames.Last)]
    internal static SpellkitObject Last(SpellkitSortedDictionary self) =>
        self.Items.Count == 0 ? Nil : Pair(self.Items.Last());

    [SpellkitMethod]
    internal static SpellkitObject Range(
        SpellkitSortedDictionary self,
        SpellkitObject from = null!,
        SpellkitObject to = null!,
        bool includeFrom = true,
        bool includeTo = true)
    {
        var comparer = self.Items.Comparer;
        var hasFrom = from is not null && from.TypeId != SpellkitTypeCodes.Nil;
        var hasTo = to is not null && to.TypeId != SpellkitTypeCodes.Nil;

        IEnumerable<SpellkitObject> Iterate()
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

        return SpellkitIterator.Create(Iterate());
    }

    [SpellkitMethod(BuiltinMethodNames.ToDictionary)]
    internal static SpellkitObject ToDictionary(SpellkitSortedDictionary self) =>
        TypeConverter.ConvertFrom(self.Items.ToDictionary(kv => kv.Key, kv => kv.Value));

    [SpellkitStaticMethod("SortedDictionary")]
    internal static SpellkitObject New(ExecutionContext ctx, [Default] SpellkitObject values)
    {
        var result = new SpellkitSortedDictionary(ctx.Type<SpellkitSortedDictionaryTypeInfo>());
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

            if (item is SpellkitTuple pair && pair.Count >= 2)
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

    private static SpellkitObject Pair(KeyValuePair<SpellkitObject, SpellkitObject> item) =>
        SpellkitTuple.Create(new("key", item.Key), new("value", item.Value));

    private static string ToLiteral(ExecutionContext ctx, IEnumerable<KeyValuePair<SpellkitObject, SpellkitObject>> values) =>
        string.Join(", ", values.Select(kv => kv.Key.ToLiteral(ctx) + ": " + kv.Value.ToLiteral(ctx)));
}
