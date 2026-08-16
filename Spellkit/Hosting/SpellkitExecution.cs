using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

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

public enum SpellkitLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed record SpellkitLogEntry(
    DateTimeOffset Timestamp,
    SpellkitLogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Guid ExecutionId,
    string? Command);

public sealed class SpellkitTelemetry
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyProperties =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    private readonly IReadOnlyList<Action<SpellkitLogEntry>> logHandlers;
    private readonly AsyncLocal<TelemetryContext?> current = new();

    internal SpellkitTelemetry(IReadOnlyList<Action<SpellkitLogEntry>> logHandlers) =>
        this.logHandlers = logHandlers;

    public Guid ExecutionId => Current().ExecutionId;

    public string? Command => Current().Command;

    public void Write(
        SpellkitLogLevel level,
        string message,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var (currentExecution, currentCommand) = Current();
        var entry = new SpellkitLogEntry(
            DateTimeOffset.UtcNow,
            level,
            message,
            Copy(properties),
            currentExecution,
            currentCommand);
        foreach (var handler in logHandlers)
        {
            handler(entry);
        }
    }

    internal void BeginExecution(Guid id) => current.Value = new(id, null);

    internal void EndExecution() => current.Value = null;

    internal IDisposable EnterCommand(string name)
    {
        var previous = current.Value;
        current.Value = new(previous?.ExecutionId ?? Guid.Empty, name);
        return new CommandScope(this, previous);
    }

    private TelemetryContext Current() => current.Value ?? TelemetryContext.Empty;

    private static IReadOnlyDictionary<string, object?> Copy(
        IReadOnlyDictionary<string, object?>? properties) =>
        properties is null || properties.Count == 0
            ? EmptyProperties
            : new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(properties, StringComparer.OrdinalIgnoreCase));

    private sealed class CommandScope : IDisposable
    {
        private readonly SpellkitTelemetry owner;
        private readonly TelemetryContext? previous;
        private bool disposed;

        public CommandScope(SpellkitTelemetry owner, TelemetryContext? previous) =>
            (this.owner, this.previous) = (owner, previous);

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            owner.current.Value = previous;
            disposed = true;
        }
    }

    private sealed record TelemetryContext(Guid ExecutionId, string? Command)
    {
        internal static readonly TelemetryContext Empty = new(Guid.Empty, null);
    }
}
