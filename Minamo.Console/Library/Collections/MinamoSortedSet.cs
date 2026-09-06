using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Library.Collections;

public sealed class MinamoSortedSet : MinamoForeignObject
{
    internal readonly SortedSet<MinamoObject> Items;

    internal MinamoSortedSet(MinamoSortedSetTypeInfo typeInfo) : base(typeInfo) =>
        Items = new(new MinamoCollectionObjectComparer());

    private MinamoSortedSet(MinamoSortedSetTypeInfo typeInfo, IEnumerable<MinamoObject> values) : this(typeInfo) =>
        Items.UnionWith(values);

    internal int Count => Items.Count;

    public override MinamoObject Clone() => new MinamoSortedSet((MinamoSortedSetTypeInfo)TypeInfo, Items);

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => Items.GetHashCode();

    public override object ToObject() => Items.Select(value => value.ToObject()).ToArray();

    public override string ToString() => $"SortedSet({Count})";
}

[MinamoType]
public sealed partial class MinamoSortedSetTypeInfo : MinamoForeignTypeInfo
{
    public override string ReflectedTypeName => "SortedSet";

    public MinamoSortedSetTypeInfo() => AddMixins(MinamoTypeCodes.Lookup, MinamoTypeCodes.Sequence);

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        MinamoInteger.Get(((MinamoSortedSet)arg).Count);

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) =>
        new MinamoString(arg.ToString());

    protected override MinamoObject IterateOp(ExecutionContext ctx, MinamoObject self) =>
        MinamoIterator.Create(((MinamoSortedSet)self).Items);

    [MinamoProperty]
    internal static int Count(MinamoSortedSet self) => self.Count;

    [MinamoMethod(BuiltinMethodNames.Add)]
    internal static bool Add(MinamoSortedSet self, MinamoObject value) => self.Items.Add(value);

    [MinamoMethod(BuiltinMethodNames.Remove)]
    internal static bool Remove(MinamoSortedSet self, MinamoObject value) => self.Items.Remove(value);

    [MinamoMethod(BuiltinMethodNames.Clear)]
    internal static void Clear(MinamoSortedSet self) => self.Items.Clear();

    [MinamoMethod]
    internal static bool Contains(MinamoSortedSet self, MinamoObject value) => self.Items.Contains(value);

    [MinamoMethod(BuiltinMethodNames.First)]
    internal static MinamoObject First(MinamoSortedSet self) => self.Items.Min ?? Nil;

    [MinamoMethod(BuiltinMethodNames.Last)]
    internal static MinamoObject Last(MinamoSortedSet self) => self.Items.Max ?? Nil;

    [MinamoMethod]
    internal static MinamoObject Range(
        MinamoSortedSet self,
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

        return MinamoIterator.Create(Iterate());
    }

    [MinamoStaticMethod("SortedSet")]
    internal static MinamoObject New(ExecutionContext ctx, [Default] MinamoObject values)
    {
        var result = new MinamoSortedSet(ctx.Type<MinamoSortedSetTypeInfo>());
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

            result.Items.Add(value);
        }

        return result;
    }
}
