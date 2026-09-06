using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;

namespace Minamo.Hosting;

internal static class MinamoSelectAliases
{
    private const string ContextKey = "Minamo.Hosting.SelectAliases";

    internal static void Register(ExecutionContext context, MinamoObject select, string alias)
    {
        HostNames.ValidateDottedName(alias, nameof(alias), "select alias");
        if (select is not MinamoSelectFactory and not MinamoString)
        {
            throw new InvalidOperationException("Alias expects a select factory.");
        }

        lock (context.RuntimeContext.SyncRoot)
        {
            if (!context.RuntimeContext.Variables.TryGetValue(ContextKey, out var existing))
            {
                existing = new Dictionary<string, MinamoObject>(StringComparer.Ordinal);
                context.RuntimeContext.Variables.Add(ContextKey, existing);
            }

            var aliases = (Dictionary<string, MinamoObject>)existing;
            if (!aliases.TryAdd(alias, select))
            {
                throw new InvalidOperationException($"The select alias '{alias}' is already registered.");
            }
        }
    }

    internal static MinamoSelectFactory? ResolveFactory(RuntimeContext context, string name)
    {
        lock (context.SyncRoot)
        {
            return context.Variables.TryGetValue(ContextKey, out var existing)
                && ((Dictionary<string, MinamoObject>)existing).TryGetValue(name, out var target)
                    ? target as MinamoSelectFactory
                    : null;
        }
    }

    internal static string ResolveName(RuntimeContext context, string name)
    {
        lock (context.SyncRoot)
        {
            return context.Variables.TryGetValue(ContextKey, out var existing)
                && ((Dictionary<string, MinamoObject>)existing).TryGetValue(name, out var target)
                && target is MinamoString text
                    ? text.Value
                    : name;
        }
    }
}
