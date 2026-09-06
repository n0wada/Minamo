using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Linq;

namespace Minamo.Hosting;

public sealed record MinamoChoiceParameter(string Name, string? TypeName);

/// <summary>Dictionary metadata declared for a select.</summary>
public sealed class MinamoSelectDescription
{
    private readonly MinamoDictionary value;

    internal MinamoSelectDescription(MinamoDictionary value) => this.value = value;

    /// <summary>Converts the description dictionary to a host type.</summary>
    public T? GetValue<T>() => MinamoHostValueConverter.Convert<T>(value, "Select description");

    /// <summary>Attempts to convert the display data to a host type.</summary>
    public bool TryGetValue<T>(out T? result) =>
        MinamoHostValueConverter.TryConvert(value, out result);
}

internal sealed record MinamoSelectState(string Id);

/// <summary>Immutable data published internally for the current select screen.</summary>
internal sealed class MinamoSelectSnapshot
{
    internal MinamoSelectSnapshot(
        string name,
        long revision,
        MinamoSelectState state,
        MinamoSelectDescription? description,
        IReadOnlyList<MinamoChoice> choices,
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

    public MinamoSelectState State { get; }

    public MinamoSelectDescription? Description { get; }

    public IReadOnlyList<MinamoChoice> Choices { get; }

    public bool IsCompleted { get; }
}

public sealed record MinamoChoice
{
    public MinamoChoice(
        string id,
        int parameterCount,
        string? label = null)
    {
        Id = id;
        ParameterCount = parameterCount;
        Label = label ?? id;
        Parameters = Array.Empty<MinamoChoiceParameter>();
        Revision = 0;
    }

    internal MinamoChoice(
        string id,
        IReadOnlyList<MinamoChoiceParameter> parameters,
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

    public IReadOnlyList<MinamoChoiceParameter> Parameters { get; }

    /// <summary>Identifies the published select screen that produced this choice.</summary>
    public long Revision { get; }
}

public sealed class MinamoSelectResult
{
    private readonly MinamoObject? value;

    internal MinamoSelectResult(
        MinamoSelectSnapshot snapshot,
        MinamoObject? value = null)
    {
        Snapshot = snapshot;
        Choices = snapshot.Choices;
        IsCompleted = snapshot.IsCompleted;
        this.value = value;
    }

    internal MinamoSelectSnapshot Snapshot { get; }

    public IReadOnlyList<MinamoChoice> Choices { get; }

    public bool IsCompleted { get; }

    internal MinamoObject Value => value ?? MinamoNil.Instance;

    public T? GetValue<T>() => MinamoHostValueConverter.Convert<T>(value, "Select result");

    public bool TryGetValue<T>(out T? result) =>
        MinamoHostValueConverter.TryConvert(value, out result);
}

internal sealed class MinamoSelectRevision
{
    private long value;

    internal long Current => System.Threading.Interlocked.Read(ref value);

    internal void Advance() => System.Threading.Interlocked.Increment(ref value);
}

/// <summary>Thrown when an action was rendered from an older select snapshot.</summary>
public sealed class MinamoSelectRevisionMismatchException : InvalidOperationException
{
    internal MinamoSelectRevisionMismatchException(
        long expectedRevision,
        MinamoSelectSnapshot snapshot)
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

    internal MinamoSelectSnapshot Snapshot { get; }
}
