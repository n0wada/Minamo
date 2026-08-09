using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Collections;

public sealed class SpellkitSortedSet : SpellkitForeignObject
{
    internal readonly SortedSet<SpellkitObject> Items;

    internal SpellkitSortedSet(SpellkitSortedSetTypeInfo typeInfo) : base(typeInfo) =>
        Items = new(new SpellkitCollectionObjectComparer());

    private SpellkitSortedSet(SpellkitSortedSetTypeInfo typeInfo, IEnumerable<SpellkitObject> values) : this(typeInfo) =>
        Items.UnionWith(values);

    internal int Count => Items.Count;

    public override SpellkitObject Clone() => new SpellkitSortedSet((SpellkitSortedSetTypeInfo)TypeInfo, Items);

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => Items.GetHashCode();

    public override object ToObject() => Items.Select(value => value.ToObject()).ToArray();

    public override string ToString() => $"SortedSet({Count})";
}

[SpellkitType]
public sealed partial class SpellkitSortedSetTypeInfo : SpellkitForeignTypeInfo
{
    public override string ReflectedTypeName => "SortedSet";

    public SpellkitSortedSetTypeInfo() => AddMixins(SpellkitTypeCodes.Lookup, SpellkitTypeCodes.Sequence);

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitSortedSet)arg).Count);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        new SpellkitString(arg.ToString());

    protected override SpellkitObject IterateOp(ExecutionContext ctx, SpellkitObject self) =>
        SpellkitIterator.Create(((SpellkitSortedSet)self).Items);

    [SpellkitProperty]
    internal static int Count(SpellkitSortedSet self) => self.Count;

    [SpellkitMethod(BuiltinMethodNames.Add)]
    internal static bool Add(SpellkitSortedSet self, SpellkitObject value) => self.Items.Add(value);

    [SpellkitMethod(BuiltinMethodNames.Remove)]
    internal static bool Remove(SpellkitSortedSet self, SpellkitObject value) => self.Items.Remove(value);

    [SpellkitMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(SpellkitSortedSet self) => self.Items.Clear();

    [SpellkitMethod]
    internal static bool Contains(SpellkitSortedSet self, SpellkitObject value) => self.Items.Contains(value);

    [SpellkitMethod(BuiltinMethodNames.First)]
    internal static SpellkitObject First(SpellkitSortedSet self) => self.Items.Min ?? Nil;

    [SpellkitMethod(BuiltinMethodNames.Last)]
    internal static SpellkitObject Last(SpellkitSortedSet self) => self.Items.Max ?? Nil;

    [SpellkitMethod]
    internal static SpellkitObject Range(
        SpellkitSortedSet self,
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
            foreach (var value in self.Items)
            {
                if (hasFrom)
                {
                    var compared = comparer.Compare(value, from);
                    if (compared < 0 || compared == 0 && !includeFrom)
                    {
                        continue;
                    }
                }

                if (hasTo)
                {
                    var compared = comparer.Compare(value, to);
                    if (compared > 0 || compared == 0 && !includeTo)
                    {
                        continue;
                    }
                }

                yield return value;
            }
        }

        return SpellkitIterator.Create(Iterate());
    }

    [SpellkitStaticMethod("SortedSet")]
    internal static SpellkitObject New(ExecutionContext ctx, [Default] SpellkitObject values)
    {
        var result = new SpellkitSortedSet(ctx.Type<SpellkitSortedSetTypeInfo>());
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

            result.Items.Add(value);
        }

        return result;
    }
}
