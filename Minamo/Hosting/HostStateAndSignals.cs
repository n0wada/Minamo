using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Linq;

namespace Minamo.Hosting;

public sealed class MinamoSignalOptions
{
    public int? MaxPending { get; init; }

    internal void Validate()
    {
        if (MaxPending is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPending), MaxPending, "The pending signal limit must be positive.");
        }
    }
}

public enum MinamoStateOwner
{
    Host,
    Script
}

public sealed class MinamoStateStore : IDisposable
{
    private readonly Dictionary<string, StateEntry> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Lock syncRoot = new();
    private bool disposed;

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();
                return values.Keys.ToArray();
            }
        }
    }

    public bool Contains(string key)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            return values.ContainsKey(key);
        }
    }

    internal MinamoObject? GetRaw(string key)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            return values.TryGetValue(key, out var entry) ? entry.Value : null;
        }
    }

    public MinamoStateOwner? GetOwner(string key)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            return values.TryGetValue(key, out var entry) ? entry.Owner : null;
        }
    }

    public T? Get<T>(string key)
    {
        var value = GetRaw(key);
        if (value is null)
        {
            return default;
        }

        return MinamoHostValueConverter.Convert<T>(value, $"State value '{key}'");
    }

    public bool TryGet<T>(string key, out T? value)
    {
        MinamoObject raw;
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (!values.TryGetValue(key, out var entry))
            {
                value = default;
                return false;
            }
            raw = entry.Value;
        }

        return MinamoHostValueConverter.TryConvert(raw, out value);
    }

    public void Set<T>(string key, T value) => SetRaw(key, TypeConverter.ConvertFrom(value));

    internal void SetRaw(string key, MinamoObject value) =>
        SetRaw(key, value, MinamoStateOwner.Host);

    public void SetScript<T>(string key, T value) =>
        SetScriptRaw(key, TypeConverter.ConvertFrom(value));

    internal void SetScriptRaw(string key, MinamoObject value) =>
        SetRaw(key, value, MinamoStateOwner.Script);

    internal void SetFromScript(string key, MinamoObject value)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (values.TryGetValue(key, out var entry) && entry.Owner == MinamoStateOwner.Host)
            {
                throw new InvalidOperationException($"State key '{key}' is owned by the host.");
            }

            values[key] = new(value, MinamoStateOwner.Script);
        }
    }

    private void SetRaw(string key, MinamoObject value, MinamoStateOwner owner)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (syncRoot)
        {
            ThrowIfDisposed();
            values[key] = new(value, owner);
        }
    }

    public bool Remove(string key)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            return values.Remove(key);
        }
    }

    internal bool RemoveFromScript(string key)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (!values.TryGetValue(key, out var entry) || entry.Owner == MinamoStateOwner.Host)
            {
                return false;
            }

            return values.Remove(key);
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            values.Clear();
        }
    }

    internal void ClearScript()
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            foreach (var key in values
                .Where(pair => pair.Value.Owner == MinamoStateOwner.Script)
                .Select(pair => pair.Key)
                .ToArray())
            {
                values.Remove(key);
            }
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            values.Clear();
            disposed = true;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("State keys cannot be empty.", nameof(key));
        }
    }

    private sealed record StateEntry(MinamoObject Value, MinamoStateOwner Owner);
}

internal sealed record HostSignalDefinition(
    string Name,
    string? ListenCapability,
    string? EmitCapability);

public sealed class MinamoSignal
{
    private readonly MinamoObject payload;

    internal MinamoSignal(string name, MinamoObject payload)
    {
        Name = name;
        this.payload = payload;
    }

    public string Name { get; }

    public T? GetPayload<T>() =>
        MinamoHostValueConverter.Convert<T>(payload, $"Signal '{Name}' payload");

    public bool TryGetPayload<T>(out T? payload) =>
        MinamoHostValueConverter.TryConvert(this.payload, out payload);

    internal MinamoObject RawPayload => payload;
}

public sealed class MinamoSignalDispatchResult : IMinamoOperationResult
{
    internal MinamoSignalDispatchResult(
        int delivered,
        IReadOnlyList<Exception> errors,
        Guid executionId,
        MinamoExecutionMetrics metrics)
    {
        Delivered = delivered;
        Failures = errors.Select(error => MinamoFailure.From(error, MinamoFailureKind.Host)).ToArray();
        ExecutionId = executionId;
        Metrics = metrics;
        Execution = new(executionId, "DispatchSignals", metrics);
    }

    public int Delivered { get; }
    public IReadOnlyList<MinamoFailure> Failures { get; }
    public Guid ExecutionId { get; }
    public MinamoExecutionMetrics Metrics { get; }
    public MinamoExecution Execution { get; }
    public bool Success => Failures.Count == 0;
}

public sealed class MinamoSignalDispatcher : IDisposable
{
    private readonly Dictionary<string, HostSignalDefinition> definitions;
    private readonly Dictionary<string, List<ScriptSubscription>> scriptSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, HostSubscription> hostSubscriptions = new();
    private readonly Queue<MinamoSignal> queue = new();
    private readonly MinamoHostEnvironment environment;
    private readonly System.Threading.Lock syncRoot = new();
    private long nextSubscription;
    private bool disposed;

    internal MinamoSignalDispatcher(
        MinamoHostEnvironment environment,
        IEnumerable<HostSignalDefinition> definitions,
        int? maxPending)
    {
        this.environment = environment;
        MaxPending = maxPending;
        this.definitions = definitions.ToDictionary(
            definition => definition.Name,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Names => definitions.Values
        .Where(definition => environment.Capabilities.Allows(definition.ListenCapability)
            || environment.Capabilities.Allows(definition.EmitCapability))
        .Select(definition => definition.Name)
        .ToArray();

    public int? MaxPending { get; }

    public int PendingCount
    {
        get
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();
                return queue.Count;
            }
        }
    }

    public long Subscribe(string name, Action<MinamoSignal> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        RequireDefinition(name);
        lock (syncRoot)
        {
            ThrowIfDisposed();
            var id = ++nextSubscription;
            hostSubscriptions.Add(id, new(name, handler));
            return id;
        }
    }

    public bool Unsubscribe(long subscriptionId)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            return hostSubscriptions.Remove(subscriptionId);
        }
    }

    public void Emit(string name, object? payload = null)
    {
        if (!TryEmit(name, payload))
        {
            throw QueueFull();
        }
    }

    public bool TryEmit(string name, object? payload = null) =>
        TryEmitRaw(name, TypeConverter.ConvertFrom(payload));

    internal void EmitRaw(string name, MinamoObject payload)
    {
        if (!TryEmitRaw(name, payload))
        {
            throw QueueFull();
        }
    }

    internal bool TryEmitRaw(string name, MinamoObject payload)
    {
        RequireDefinition(name);
        ArgumentNullException.ThrowIfNull(payload);
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (MaxPending is not null && queue.Count >= MaxPending.Value)
            {
                return false;
            }

            queue.Enqueue(new(name, payload));
        }
        environment.Tracing.Write(MinamoTraceKind.SignalEmitted, name);
        return true;
    }

    internal long SubscribeScript(string name, MinamoFunction handler, bool once)
    {
        var definition = RequireDefinition(name);
        environment.Capabilities.Demand(definition.ListenCapability);

        lock (syncRoot)
        {
            ThrowIfDisposed();
            var id = ++nextSubscription;
            if (!scriptSubscriptions.TryGetValue(name, out var subscriptions))
            {
                subscriptions = new();
                scriptSubscriptions.Add(name, subscriptions);
            }
            subscriptions.Add(new(id, handler, once));
            return id;
        }
    }

    internal long CreateScriptSubscriptionCheckpoint()
    {
        lock (syncRoot)
        {
            return nextSubscription;
        }
    }

    internal void RollbackScriptSubscriptions(long checkpoint)
    {
        lock (syncRoot)
        {
            foreach (var subscriptions in scriptSubscriptions.Values)
            {
                subscriptions.RemoveAll(subscription => subscription.Id > checkpoint);
            }
        }
    }

    internal bool UnsubscribeScript(long subscriptionId)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            foreach (var subscriptions in scriptSubscriptions.Values)
            {
                var index = subscriptions.FindIndex(subscription => subscription.Id == subscriptionId);
                if (index >= 0)
                {
                    subscriptions.RemoveAt(index);
                    return true;
                }
            }
            return false;
        }
    }

    internal void EmitFromScript(string name, MinamoObject payload)
    {
        var definition = RequireDefinition(name);
        environment.Capabilities.Demand(definition.EmitCapability);
        EmitRaw(name, payload);
    }

    internal bool TryEmitFromScript(string name, MinamoObject payload)
    {
        var definition = RequireDefinition(name);
        environment.Capabilities.Demand(definition.EmitCapability);
        return TryEmitRaw(name, payload);
    }

    internal bool TryDequeue(out MinamoSignal signal)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (queue.Count == 0)
            {
                signal = null!;
                return false;
            }

            signal = queue.Dequeue();
            return true;
        }
    }

    internal IReadOnlyList<MinamoFunction> GetScriptHandlers(string name)
    {
        lock (syncRoot)
        {
            if (!scriptSubscriptions.TryGetValue(name, out var subscriptions))
            {
                return Array.Empty<MinamoFunction>();
            }

            var handlers = subscriptions.Select(subscription => subscription.Handler).ToArray();
            subscriptions.RemoveAll(subscription => subscription.Once);
            return handlers;
        }
    }

    internal IReadOnlyList<Action<MinamoSignal>> GetHostHandlers(string name)
    {
        lock (syncRoot)
        {
            return hostSubscriptions.Values
                .Where(subscription => string.Equals(
                    subscription.Name, name, StringComparison.OrdinalIgnoreCase))
                .Select(subscription => subscription.Handler)
                .ToArray();
        }
    }

    internal void Reset()
    {
        lock (syncRoot)
        {
            scriptSubscriptions.Clear();
            queue.Clear();
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            scriptSubscriptions.Clear();
            hostSubscriptions.Clear();
            queue.Clear();
            disposed = true;
        }
    }

    private HostSignalDefinition RequireDefinition(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Signal names cannot be empty.", nameof(name));
        }

        if (!definitions.TryGetValue(name, out var definition))
        {
            throw new KeyNotFoundException($"Host signal '{name}' is not registered.");
        }

        return definition;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private InvalidOperationException QueueFull() => new(
        $"The pending signal limit of {MaxPending} has been reached.");

    private sealed record ScriptSubscription(long Id, MinamoFunction Handler, bool Once);
    private sealed record HostSubscription(string Name, Action<MinamoSignal> Handler);
}
