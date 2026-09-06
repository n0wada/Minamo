using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Library.Random;

[MinamoType]
public sealed partial class MinamoRandomTypeInfo : MinamoForeignTypeInfo
{
    private const string Random = "Random";

    public override string ReflectedTypeName => Random;

    [MinamoMethod]
    internal static MinamoObject Next(
        ExecutionContext ctx,
        MinamoRandom self,
        long min = 0,
        long? max = null)
    {
        var upper = max ?? long.MaxValue;
        if (min >= upper)
        {
            return ctx.InvalidValue(min, upper);
        }
        return MinamoInteger.Get(self.Generator.NextInt64(min, upper));
    }

    [MinamoMethod]
    internal static double NextFloat(MinamoRandom self) => self.Generator.NextDouble();

    [MinamoMethod]
    internal static bool NextBool(MinamoRandom self) => self.Generator.Next(2) == 1;

    [MinamoMethod]
    internal static MinamoObject Choose(ExecutionContext ctx, MinamoRandom self, MinamoObject values)
    {
        var items = MinamoIterator.ToEnumerable(ctx, values).ToArray();
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

    [MinamoMethod]
    internal static MinamoObject Shuffle(ExecutionContext ctx, MinamoRandom self, MinamoObject values)
    {
        var items = MinamoIterator.ToEnumerable(ctx, values).ToArray();
        if (ctx.HasErrors)
        {
            return Nil;
        }

        for (var i = items.Length - 1; i > 0; i--)
        {
            var j = self.Generator.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return new MinamoArray(items);
    }

    [MinamoStaticMethod(Random)]
    internal static MinamoObject Create(ExecutionContext ctx, int? seed = null) =>
        new MinamoRandom(ctx.Type<MinamoRandomTypeInfo>(), seed);
}
