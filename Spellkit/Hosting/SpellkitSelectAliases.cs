using Spellkit.Runtime;
using System.Collections.Generic;

namespace Spellkit.Hosting;

internal static class SpellkitSelectAliases
{
    private const string ContextKey = "Spellkit.Hosting.SelectAliases";

    internal static void Register(ExecutionContext context, string selectName, string alias)
    {
        HostNames.ValidateDottedName(alias, nameof(alias), "select alias");
        lock (context.RuntimeContext.SyncRoot)
        {
            if (!context.RuntimeContext.Variables.TryGetValue(ContextKey, out var existing))
            {
                existing = new Dictionary<string, string>(StringComparer.Ordinal);
                context.RuntimeContext.Variables.Add(ContextKey, existing);
            }

            var aliases = (Dictionary<string, string>)existing;
            if (!aliases.TryAdd(alias, selectName))
            {
                throw new InvalidOperationException($"The select alias '{alias}' is already registered.");
            }
        }
    }

    internal static string Resolve(RuntimeContext context, string name)
    {
        lock (context.SyncRoot)
        {
            return context.Variables.TryGetValue(ContextKey, out var existing)
                && ((Dictionary<string, string>)existing).TryGetValue(name, out var target)
                    ? target
                    : name;
        }
    }
}
