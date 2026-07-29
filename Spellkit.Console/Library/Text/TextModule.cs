using Spellkit.Hosting;
using Spellkit.Linker;

namespace Spellkit.Library.Text;

[SpellkitModule("text")]
public sealed class TextModule : ForeignUnit
{
    public TextModule()
    {
        AddType<SpellkitStringBuilderTypeInfo>();
        AddType<SpellkitRegexTypeInfo>();
    }
}
