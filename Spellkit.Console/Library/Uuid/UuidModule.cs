using Spellkit.Hosting;
using Spellkit.Linker;

namespace Spellkit.Library.Uuid;

[SpellkitModule("uuid")]
public sealed class UuidModule : ForeignUnit
{
    public UuidModule() => AddType<SpellkitGuidTypeInfo>();
}
