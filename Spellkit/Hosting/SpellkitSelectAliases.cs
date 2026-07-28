using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;

namespace Spellkit.Hosting;

internal static class SpellkitSelectAliases
{
    private const string ContextKey = "Spellkit.Hosting.SelectAliases";

    internal static void Register(ExecutionContext context, SpkObject select, string alias)
    {
        HostNames.ValidateDottedName(alias, nameof(alias), "select alias");
        if (select is not SpkSelectFactory and not SpkString)
        {
            throw new InvalidOperationException("Alias expects a select factory.");
        }

        lock (context.RuntimeContext.SyncRoot)
        {
            if (!context.RuntimeContext.Variables.TryGetValue(ContextKey, out var existing))
            {
                existing = new Dictionary<string, SpkObject>(StringComparer.Ordinal);
                context.RuntimeContext.Variables.Add(ContextKey, existing);
            }

            var aliases = (Dictionary<string, SpkObject>)existing;
            if (!aliases.TryAdd(alias, select))
            {
                throw new InvalidOperationException($"The select alias '{alias}' is already registered.");
            }
        }
    }

    internal static SpkSelectFactory? ResolveFactory(RuntimeContext context, string name)
    {
        lock (context.SyncRoot)
        {
            return context.Variables.TryGetValue(ContextKey, out var existing)
                && ((Dictionary<string, SpkObject>)existing).TryGetValue(name, out var target)
                    ? target as SpkSelectFactory
                    : null;
        }
    }

    internal static string ResolveName(RuntimeContext context, string name)
    {
        lock (context.SyncRoot)
        {
            return context.Variables.TryGetValue(ContextKey, out var existing)
                && ((Dictionary<string, SpkObject>)existing).TryGetValue(name, out var target)
                && target is SpkString text
                    ? text.Value
                    : name;
        }
    }
}
