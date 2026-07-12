using Spellkit.Compiler;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Spellkit.Hosting;

public sealed class SpellkitProgram
{
    internal SpellkitProgram(
        UnitComposition composition,
        IReadOnlyList<BuildMessage> diagnostics,
        object owner)
    {
        Composition = composition ?? throw new ArgumentNullException(nameof(composition));
        Diagnostics = diagnostics.Select(SpellkitDiagnostic.From).ToArray();
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    internal UnitComposition Composition { get; }

    internal object Owner { get; }

    public IReadOnlyList<SpellkitDiagnostic> Diagnostics { get; }
}

public sealed class SpellkitEnvironment
{
    internal const string ContextKey = "Spellkit.Hosting.SpellkitEnvironment";

    private readonly Dictionary<string, object?> bindings = new(StringComparer.OrdinalIgnoreCase);

    public SpellkitEnvironment(object? hostContext = null) => HostContext = hostContext;

    public object? HostContext { get; }

    public IReadOnlyDictionary<string, object?> Bindings =>
        new ReadOnlyDictionary<string, object?>(bindings);

    public SpellkitEnvironment Expose(string name, object? value)
    {
        HostNames.ValidateIdentifier(name, nameof(name), "environment binding");
        bindings[name] = value;
        return this;
    }

    public SpellkitEnvironment Set(string name, object? value) =>
        Expose(name, value);

    public bool TryGet(string name, out object? value) =>
        bindings.TryGetValue(name, out value);

    internal bool TryResolve(string name, out SpkObject value)
    {
        if (!bindings.TryGetValue(name, out var raw))
        {
            value = SpkNil.Instance;
            return false;
        }

        value = TypeConverter.ConvertFrom(raw);
        return true;
    }
}

public sealed class SpellkitExecution
{
    internal SpellkitExecution(
        Guid id,
        string operation,
        SpellkitExecutionMetrics metrics)
    {
        Id = id;
        Operation = operation;
        Metrics = metrics;
    }

    public Guid Id { get; }

    public string Operation { get; }

    public SpellkitExecutionMetrics Metrics { get; }
}
