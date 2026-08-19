using Spellkit.Compiler;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace Spellkit.Hosting;

public enum SpellkitDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record SpellkitDiagnostic(
    SpellkitDiagnosticSeverity Severity,
    int Code,
    string Message,
    string? File,
    int Line,
    int Column)
{
    internal static SpellkitDiagnostic From(BuildMessage message) => new(
        message.Type switch
        {
            BuildMessageType.Error => SpellkitDiagnosticSeverity.Error,
            BuildMessageType.Warning => SpellkitDiagnosticSeverity.Warning,
            _ => SpellkitDiagnosticSeverity.Information
        },
        message.Code,
        message.Message,
        message.File,
        message.Line,
        message.Column);
}

public enum SpellkitFailureKind
{
    Compilation,
    Runtime,
    Host,
    Input,
    Cancelled,
    Limit
}

public sealed record SpellkitFailure(
    SpellkitFailureKind Kind,
    string Message,
    Exception? Exception = null,
    SpellkitExecutionLimitKind? Limit = null)
{
    internal static SpellkitFailure Compilation(IReadOnlyList<SpellkitDiagnostic> diagnostics) => new(
        SpellkitFailureKind.Compilation,
        diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == SpellkitDiagnosticSeverity.Error)?.Message
            ?? "Compilation failed.");

    internal static SpellkitFailure From(Exception exception, SpellkitFailureKind fallback) => exception switch
    {
        SpellkitBuildException { InnerException: { } inner } =>
            From(inner, fallback),
        SpellkitExecutionLimitException limit => new(
            SpellkitFailureKind.Limit,
            limit.Message,
            limit,
            limit.Kind),
        OperationCanceledException => new(SpellkitFailureKind.Cancelled, exception.Message, exception),
        SpellkitRuntimeException => new(SpellkitFailureKind.Runtime, exception.Message, exception),
        _ => new(fallback, exception.Message, exception)
    };
}

public interface ISpellkitOperationResult
{
    bool Success { get; }
    IReadOnlyList<SpellkitFailure> Failures { get; }
    Guid ExecutionId { get; }
    SpellkitExecutionMetrics Metrics { get; }
    SpellkitExecution Execution { get; }
}

public sealed class SpellkitExecutionResult : ISpellkitOperationResult
{
    private readonly SpellkitObject? value;

    internal SpellkitExecutionResult(
        SpellkitObject? value,
        IReadOnlyList<BuildMessage> messages,
        SpellkitFailure? failure,
        string operation,
        Guid executionId,
        SpellkitExecutionMetrics metrics)
    {
        this.value = value;
        Diagnostics = messages.Select(SpellkitDiagnostic.From).ToArray();
        Failure = failure ?? (messages.Any(message => message.Type == BuildMessageType.Error)
            ? SpellkitFailure.Compilation(Diagnostics)
            : null);
        Failures = Failure is null ? Array.Empty<SpellkitFailure>() : new[] { Failure };
        ExecutionId = executionId;
        Metrics = metrics;
        Execution = new(executionId, operation, metrics);
    }

    public bool Success => Failure is null;

    public T? GetValue<T>() =>
        SpellkitHostValueConverter.Convert<T>(value, "Execution result");

    public bool TryGetValue<T>(out T? value) =>
        SpellkitHostValueConverter.TryConvert(this.value, out value);

    public IReadOnlyList<SpellkitDiagnostic> Diagnostics { get; }

    public SpellkitFailure? Failure { get; }

    public IReadOnlyList<SpellkitFailure> Failures { get; }

    public Guid ExecutionId { get; }

    public SpellkitExecutionMetrics Metrics { get; }

    public SpellkitExecution Execution { get; }
}

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
