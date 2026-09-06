using Minamo.Hosting;
using Minamo.Linker;

namespace Minamo.Library.Random;

[MinamoModule("random")]
public sealed class RandomModule : ForeignUnit
{
    public RandomModule() => AddType<MinamoRandomTypeInfo>();
}
