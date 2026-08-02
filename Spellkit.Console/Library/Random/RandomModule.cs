using Spellkit.Hosting;
using Spellkit.Linker;

namespace Spellkit.Library.Random;

[SpellkitModule("random")]
public sealed class RandomModule : ForeignUnit
{
    public RandomModule() => AddType<SpellkitRandomTypeInfo>();
}
