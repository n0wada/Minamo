using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;

namespace Spellkit.Hosting;

public sealed record SpellkitChoiceParameter(string Name, string? TypeName);

/// <summary>Dictionary metadata declared for a select.</summary>
public sealed class SpellkitSelectDescription
{
    private readonly SpellkitDictionary value;

    internal SpellkitSelectDescription(SpellkitDictionary value) => this.value = value;

    /// <summary>Converts the description dictionary to a host type.</summary>
    public T? GetValue<T>() => SpellkitHostValueConverter.Convert<T>(value, "Select description");

    /// <summary>Attempts to convert the display data to a host type.</summary>
    public bool TryGetValue<T>(out T? result) =>
        SpellkitHostValueConverter.TryConvert(value, out result);
}

internal sealed record SpellkitSelectState(string Id);

/// <summary>Immutable data published internally for the current select screen.</summary>
internal sealed class SpellkitSelectSnapshot
{
    internal SpellkitSelectSnapshot(
        string name,
        long revision,
        SpellkitSelectState state,
        SpellkitSelectDescription? description,
        IReadOnlyList<SpellkitChoice> choices,
        bool isCompleted)
    {
        Name = name;
        Revision = revision;
        State = state;
        Description = description;
        Choices = choices;
        IsCompleted = isCompleted;
    }

    public string Name { get; }

    /// <summary>Monotonically increases after a successful select action, cancellation, or invalidation.</summary>
    public long Revision { get; }

    public SpellkitSelectState State { get; }

    public SpellkitSelectDescription? Description { get; }

    public IReadOnlyList<SpellkitChoice> Choices { get; }

    public bool IsCompleted { get; }
}

public sealed record SpellkitChoice
{
    public SpellkitChoice(
        string id,
        int parameterCount,
        string? label = null)
    {
        Id = id;
        ParameterCount = parameterCount;
        Label = label ?? id;
        Parameters = Array.Empty<SpellkitChoiceParameter>();
        Revision = 0;
    }

    internal SpellkitChoice(
        string id,
        IReadOnlyList<SpellkitChoiceParameter> parameters,
        string? label = null,
        long revision = 0)
    {
        Id = id;
        Parameters = parameters.ToArray();
        ParameterCount = Parameters.Count;
        Label = label ?? id;
        Revision = revision;
    }

    public string Id { get; }

    public string Label { get; }

    public int ParameterCount { get; }

    public IReadOnlyList<SpellkitChoiceParameter> Parameters { get; }

    /// <summary>Identifies the published select screen that produced this choice.</summary>
    public long Revision { get; }
}

public sealed class SpellkitSelectResult
{
    private readonly SpellkitObject? value;

    internal SpellkitSelectResult(
        SpellkitSelectSnapshot snapshot,
        SpellkitObject? value = null)
    {
        Snapshot = snapshot;
        Choices = snapshot.Choices;
        IsCompleted = snapshot.IsCompleted;
        this.value = value;
    }

    internal SpellkitSelectSnapshot Snapshot { get; }

    public IReadOnlyList<SpellkitChoice> Choices { get; }

    public bool IsCompleted { get; }

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
        CurrentRevision = snapshot.Revision;
        Snapshot = snapshot;
    }

    public long ExpectedRevision { get; }

    /// <summary>Gets the revision that supersedes the rejected action.</summary>
    public long CurrentRevision { get; }

    internal SpellkitSelectSnapshot Snapshot { get; }
}
