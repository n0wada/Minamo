using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Library.Random;

[SpellkitType]
public sealed partial class SpellkitRandomTypeInfo : SpellkitForeignTypeInfo
{
    private const string Random = "Random";

    public override string ReflectedTypeName => Random;

    [SpellkitMethod]
    internal static SpellkitObject Next(
        ExecutionContext ctx,
        SpellkitRandom self,
        long min = 0,
        long? max = null)
    {
        var upper = max ?? long.MaxValue;
        if (min >= upper)
        {
            return ctx.InvalidValue(min, upper);
        }
        return SpellkitInteger.Get(self.Generator.NextInt64(min, upper));
    }

    [SpellkitMethod]
    internal static double NextFloat(SpellkitRandom self) => self.Generator.NextDouble();

    [SpellkitMethod]
    internal static bool NextBool(SpellkitRandom self) => self.Generator.Next(2) == 1;

    [SpellkitMethod]
    internal static SpellkitObject Choose(ExecutionContext ctx, SpellkitRandom self, SpellkitObject values)
    {
        var items = SpellkitIterator.ToEnumerable(ctx, values).ToArray();
        if (ctx.HasErrors)
        {
            return Nil;
        }
        if (items.Length == 0)
        {
            return ctx.InvalidValue(values);
        }
        return items[self.Generator.Next(items.Length)];
    }

    [SpellkitMethod]
    internal static SpellkitObject Shuffle(ExecutionContext ctx, SpellkitRandom self, SpellkitObject values)
    {
        var items = SpellkitIterator.ToEnumerable(ctx, values).ToArray();
        if (ctx.HasErrors)
        {
            return Nil;
        }

        for (var i = items.Length - 1; i > 0; i--)
        {
            var j = self.Generator.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return new SpellkitArray(items);
    }

    [SpellkitStaticMethod(Random)]
    internal static SpellkitObject Create(ExecutionContext ctx, int? seed = null) =>
        new SpellkitRandom(ctx.Type<SpellkitRandomTypeInfo>(), seed);
}
