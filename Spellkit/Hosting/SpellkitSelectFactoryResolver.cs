using Spellkit.Runtime.Types;

namespace Spellkit.Hosting;

/// <summary>Resolves legacy dotted select names while the VM evaluates <c>do</c>.</summary>
internal sealed class SpellkitSelectFactoryResolver(SpellkitInstance instance)
{
    internal const string ContextKey = "Spellkit.Hosting.SelectFactoryResolver";

    internal SpkSelectFactory? Resolve(string name) => instance.ResolveSelectFactory(name);
}
