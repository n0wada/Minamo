using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;

namespace Spellkit.Hosting;

public sealed record SpellkitChoiceParameter(string Name, string? TypeName);

/// <summary>Display data evaluated by a select state or choice.</summary>
public sealed class SpellkitSelectView
{
    private readonly SpellkitObject value;

    internal SpellkitSelectView(SpellkitObject value) => this.value = value;

    /// <summary>Converts the display data to a host type.</summary>
    public T? GetValue<T>() => SpellkitHostValueConverter.Convert<T>(value, "Select view");

    /// <summary>Attempts to convert the display data to a host type.</summary>
    public bool TryGetValue<T>(out T? result) =>
        SpellkitHostValueConverter.TryConvert(value, out result);
}

/// <summary>The currently active state and its display data.</summary>
public sealed record SpellkitSelectState(string Id, SpellkitSelectView? View);

/// <summary>An immutable UI-facing view of a select session.</summary>
public sealed class SpellkitSelectSnapshot
{
    internal SpellkitSelectSnapshot(
        string name,
        long revision,
        SpellkitSelectState state,
        IReadOnlyList<SpellkitChoice> choices,
        bool isCompleted)
    {
        Name = name;
        Revision = revision;
        State = state;
        Choices = choices;
        IsCompleted = isCompleted;
    }

    public string Name { get; }

    /// <summary>Monotonically increases after a successful select action, cancellation, or invalidation.</summary>
    public long Revision { get; }

    public SpellkitSelectState State { get; }

    public IReadOnlyList<SpellkitChoice> Choices { get; }

    public bool IsCompleted { get; }
}

public sealed record SpellkitChoice
{
    public SpellkitChoice(
        string id,
        int parameterCount,
        string? label = null,
        SpellkitSelectView? view = null)
    {
        Id = id;
        ParameterCount = parameterCount;
        Label = label ?? id;
        View = view;
        Parameters = Array.Empty<SpellkitChoiceParameter>();
    }

    internal SpellkitChoice(
        string id,
        IReadOnlyList<SpellkitChoiceParameter> parameters,
        string? label = null,
        SpellkitSelectView? view = null)
    {
        Id = id;
        Parameters = parameters.ToArray();
        ParameterCount = Parameters.Count;
        Label = label ?? id;
        View = view;
    }

    public string Id { get; }

    public string Label { get; }

    public SpellkitSelectView? View { get; }

    public int ParameterCount { get; }

    public IReadOnlyList<SpellkitChoiceParameter> Parameters { get; }
}

public sealed class SpellkitSelectResult
{
    private readonly SpellkitObject? value;

    internal SpellkitSelectResult(
        SpellkitSelectSnapshot snapshot,
        SpellkitObject? value = null)
    {
        Snapshot = snapshot;
        this.value = value;
    }

    public SpellkitSelectSnapshot Snapshot { get; }

    public IReadOnlyList<SpellkitChoice> Choices => Snapshot.Choices;

    public bool IsCompleted => Snapshot.IsCompleted;

    internal SpellkitObject Value => value ?? SpellkitNil.Instance;

    public T? GetValue<T>() => SpellkitHostValueConverter.Convert<T>(value, "Select result");

    public bool TryGetValue<T>(out T? result) =>
        SpellkitHostValueConverter.TryConvert(value, out result);
}

internal sealed class SpellkitSelectRevision
{
    private long value;

    internal long Current => System.Threading.Interlocked.Read(ref value);

    internal void Advance() => System.Threading.Interlocked.Increment(ref value);
}

/// <summary>Thrown when an action was rendered from an older select snapshot.</summary>
public sealed class SpellkitSelectRevisionMismatchException : InvalidOperationException
{
    internal SpellkitSelectRevisionMismatchException(
        long expectedRevision,
        SpellkitSelectSnapshot snapshot)
        : base(
            $"Select revision {expectedRevision} does not match current revision {snapshot.Revision}.")
    {
        ExpectedRevision = expectedRevision;
        Snapshot = snapshot;
    }

    public long ExpectedRevision { get; }

    /// <summary>Gets the current snapshot that supersedes the rejected action.</summary>
    public SpellkitSelectSnapshot Snapshot { get; }
}
