using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace Spellkit.Hosting;

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
