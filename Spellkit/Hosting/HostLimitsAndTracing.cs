using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Spellkit.Hosting;

public sealed class SpellkitExecutionLimits
{
    public long? MaxInstructions { get; init; }
    public TimeSpan? MaxExecutionTime { get; init; }
    public int? MaxHostCommands { get; init; }
    public int? MaxSignals { get; init; }
    public int? MaxCallDepth { get; init; }
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    internal bool RequiresControl =>
        MaxInstructions is not null
        || MaxExecutionTime is not null
        || MaxHostCommands is not null
        || MaxSignals is not null
        || MaxCallDepth is not null;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(TimeProvider);
        Positive(MaxInstructions, nameof(MaxInstructions));
        Positive(MaxExecutionTime, nameof(MaxExecutionTime));
        Positive(MaxHostCommands, nameof(MaxHostCommands));
        Positive(MaxSignals, nameof(MaxSignals));
        Positive(MaxCallDepth, nameof(MaxCallDepth));
    }

    private static void Positive(long? value, string name)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Execution limits must be positive.");
        }
    }

    private static void Positive(int? value, string name) => Positive((long?)value, name);

    private static void Positive(TimeSpan? value, string name)
    {
        if (value is not null && value.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name, value, "Execution limits must be positive.");
        }
    }
}

public sealed record SpellkitExecutionMetrics(
    TimeSpan TotalDuration,
    TimeSpan CompilationDuration,
    TimeSpan VmDuration,
    long Instructions,
    int HostCommands,
    int Signals);

public enum SpellkitTraceKind
{
    ExecutionStarted,
    ExecutionCompleted,
    Compilation,
    VmExecution,
    HostCommand,
    CapabilityDenied,
    SignalEmitted,
    SignalDelivered,
    ResourceCreated,
    ResourceReleased
}

public sealed record SpellkitTraceEvent(
    DateTimeOffset Timestamp,
    SpellkitTraceKind Kind,
    Guid ExecutionId,
    string? Name,
    TimeSpan? Duration,
    IReadOnlyDictionary<string, object?> Data);

public sealed class SpellkitTracing
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyData =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    private readonly IReadOnlyList<Action<SpellkitTraceEvent>> handlers;
    private readonly SpellkitTelemetry telemetry;

    internal SpellkitTracing(
        IReadOnlyList<Action<SpellkitTraceEvent>> handlers,
        SpellkitTelemetry telemetry) =>
        (this.handlers, this.telemetry) = (handlers, telemetry);

    public bool Enabled => handlers.Count != 0;

    internal void Write(
        SpellkitTraceKind kind,
        string? name = null,
        TimeSpan? duration = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        if (handlers.Count == 0)
        {
            return;
        }

        var traceEvent = new SpellkitTraceEvent(
            DateTimeOffset.UtcNow,
            kind,
            telemetry.ExecutionId,
            name,
            duration,
            Copy(data));
        foreach (var handler in handlers)
        {
            try
            {
                handler(traceEvent);
            }
            catch
            {
                // Tracing is observational and must not change script behavior.
            }
        }
    }

    private static IReadOnlyDictionary<string, object?> Copy(
        IReadOnlyDictionary<string, object?>? data) =>
        data is null || data.Count == 0
            ? EmptyData
            : new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase));
}
