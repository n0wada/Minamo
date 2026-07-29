using Spellkit.Hosting;
using Spellkit.Linker;

namespace Spellkit.Library.Binary;

[SpellkitModule("binary")]
public sealed class BinaryModule : ForeignUnit
{
    public BinaryModule() => AddType<SpellkitByteArrayTypeInfo>();
}
