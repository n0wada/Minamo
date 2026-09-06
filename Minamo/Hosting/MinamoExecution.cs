using Minamo.Compiler;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace Minamo.Hosting;

public enum MinamoDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record MinamoDiagnostic(
    MinamoDiagnosticSeverity Severity,
    int Code,
    string Message,
    string? File,
    int Line,
    int Column)
{
    internal static MinamoDiagnostic From(BuildMessage message) => new(
        message.Type switch
        {
            BuildMessageType.Error => MinamoDiagnosticSeverity.Error,
            BuildMessageType.Warning => MinamoDiagnosticSeverity.Warning,
            _ => MinamoDiagnosticSeverity.Information
        },
        message.Code,
        message.Message,
        message.File,
        message.Line,
        message.Column);
}

public enum MinamoFailureKind
{
    Compilation,
    Runtime,
    Host,
    Input,
    Cancelled,
    Limit
}

public sealed record MinamoFailure(
    MinamoFailureKind Kind,
    string Message,
    Exception? Exception = null,
    MinamoExecutionLimitKind? Limit = null)
{
    internal static MinamoFailure Compilation(IReadOnlyList<MinamoDiagnostic> diagnostics) => new(
        MinamoFailureKind.Compilation,
        diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == MinamoDiagnosticSeverity.Error)?.Message
            ?? "Compilation failed.");

    internal static MinamoFailure From(Exception exception, MinamoFailureKind fallback) => exception switch
    {
        MinamoBuildException { InnerException: { } inner } =>
            From(inner, fallback),
        MinamoExecutionLimitException limit => new(
            MinamoFailureKind.Limit,
            limit.Message,
            limit,
            limit.Kind),
        OperationCanceledException => new(MinamoFailureKind.Cancelled, exception.Message, exception),
        MinamoRuntimeException => new(MinamoFailureKind.Runtime, exception.Message, exception),
        _ => new(fallback, exception.Message, exception)
    };
}

public interface IMinamoOperationResult
{
    bool Success { get; }
    IReadOnlyList<MinamoFailure> Failures { get; }
    Guid ExecutionId { get; }
    MinamoExecutionMetrics Metrics { get; }
    MinamoExecution Execution { get; }
}

public sealed class MinamoExecutionResult : IMinamoOperationResult
{
    private readonly MinamoObject? value;

    internal MinamoExecutionResult(
        MinamoObject? value,
        IReadOnlyList<BuildMessage> messages,
        MinamoFailure? failure,
        string operation,
        Guid executionId,
        MinamoExecutionMetrics metrics)
    {
        this.value = value;
        Diagnostics = messages.Select(MinamoDiagnostic.From).ToArray();
        Failure = failure ?? (messages.Any(message => message.Type == BuildMessageType.Error)
            ? MinamoFailure.Compilation(Diagnostics)
            : null);
        Failures = Failure is null ? Array.Empty<MinamoFailure>() : new[] { Failure };
        ExecutionId = executionId;
        Metrics = metrics;
        Execution = new(executionId, operation, metrics);
    }

    public bool Success => Failure is null;

    public T? GetValue<T>() =>
        MinamoHostValueConverter.Convert<T>(value, "Execution result");

    public bool TryGetValue<T>(out T? value) =>
        MinamoHostValueConverter.TryConvert(this.value, out value);

    public IReadOnlyList<MinamoDiagnostic> Diagnostics { get; }

    public MinamoFailure? Failure { get; }

    public IReadOnlyList<MinamoFailure> Failures { get; }

    public Guid ExecutionId { get; }

    public MinamoExecutionMetrics Metrics { get; }

    public MinamoExecution Execution { get; }
}

public sealed class MinamoExecutionLimits
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

public sealed record MinamoExecutionMetrics(
    TimeSpan TotalDuration,
    TimeSpan CompilationDuration,
    TimeSpan VmDuration,
    long Instructions,
    int HostCommands,
    int Signals);

public sealed class MinamoExecution
{
    internal MinamoExecution(
        Guid id,
        string operation,
        MinamoExecutionMetrics metrics)
    {
        Id = id;
        Operation = operation;
        Metrics = metrics;
    }

    public Guid Id { get; }

    public string Operation { get; }

    public MinamoExecutionMetrics Metrics { get; }
}

public enum MinamoTraceKind
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

public sealed record MinamoTraceEvent(
    DateTimeOffset Timestamp,
    MinamoTraceKind Kind,
    Guid ExecutionId,
    string? Name,
    TimeSpan? Duration,
    IReadOnlyDictionary<string, object?> Data);

public sealed class MinamoTracing
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyData =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    private readonly IReadOnlyList<Action<MinamoTraceEvent>> handlers;
    private readonly MinamoTelemetry telemetry;

    internal MinamoTracing(
        IReadOnlyList<Action<MinamoTraceEvent>> handlers,
        MinamoTelemetry telemetry) =>
        (this.handlers, this.telemetry) = (handlers, telemetry);

    public bool Enabled => handlers.Count != 0;

    internal void Write(
        MinamoTraceKind kind,
        string? name = null,
        TimeSpan? duration = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        if (handlers.Count == 0)
        {
            return;
        }

        var traceEvent = new MinamoTraceEvent(
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

public enum MinamoLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed record MinamoLogEntry(
    DateTimeOffset Timestamp,
    MinamoLogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Guid ExecutionId,
    string? Command);

public sealed class MinamoTelemetry
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyProperties =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    private readonly IReadOnlyList<Action<MinamoLogEntry>> logHandlers;
    private readonly AsyncLocal<TelemetryContext?> current = new();

    internal MinamoTelemetry(IReadOnlyList<Action<MinamoLogEntry>> logHandlers) =>
        this.logHandlers = logHandlers;

    public Guid ExecutionId => Current().ExecutionId;

    public string? Command => Current().Command;

    public void Write(
        MinamoLogLevel level,
        string message,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var (currentExecution, currentCommand) = Current();
        var entry = new MinamoLogEntry(
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
        private readonly MinamoTelemetry owner;
        private readonly TelemetryContext? previous;
        private bool disposed;

        public CommandScope(MinamoTelemetry owner, TelemetryContext? previous) =>
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
