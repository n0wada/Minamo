using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Hosting;

internal sealed class SpellkitSelectInvoker(SpellkitInstance instance)
{
    internal const string ContextKey = "Spellkit.Hosting.SelectInvoker";

    internal SpkObject Invoke(string name)
    {
        using var session = instance.OpenSelect(name);
        instance.SpellkitEnvironment.RunSelect(session);
        return SpkNil.Instance;
    }
}
