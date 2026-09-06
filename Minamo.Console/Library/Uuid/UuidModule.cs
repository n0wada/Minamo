using Minamo.Hosting;
using Minamo.Linker;

namespace Minamo.Library.Uuid;

[MinamoModule("uuid")]
public sealed class UuidModule : ForeignUnit
{
    public UuidModule() => AddType<MinamoGuidTypeInfo>();
}
