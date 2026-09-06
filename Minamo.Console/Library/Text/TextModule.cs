using Minamo.Hosting;
using Minamo.Linker;

namespace Minamo.Library.Text;

[MinamoModule("text")]
public sealed class TextModule : ForeignUnit
{
    public TextModule()
    {
        AddType<MinamoStringBuilderTypeInfo>();
        AddType<MinamoRegexTypeInfo>();
    }
}
