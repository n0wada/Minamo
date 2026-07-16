using Spellkit.Compiler;
using Spellkit.Debug;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Spellkit.Hosting;

public sealed class SpellkitCapabilitySet
{
    private readonly HashSet<string> allowed;
    private readonly ReadOnlyCollection<string> allowedView;
    private readonly Action<string>? denied;

    internal SpellkitCapabilitySet(
        IEnumerable<string> allowed,
        bool unrestricted,
        Action<string>? denied = null)
    {
        this.allowed = new(allowed, StringComparer.OrdinalIgnoreCase);
        allowedView = new(this.allowed.ToArray());
        this.denied = denied;
        IsUnrestricted = unrestricted;
    }

    public bool IsUnrestricted { get; }

    public IReadOnlyCollection<string> Allowed => allowedView;

    public bool Allows(string? capability)
    {
        if (string.IsNullOrWhiteSpace(capability) || IsUnrestricted)
        {
            return true;
        }

        if (allowed.Contains(capability) || allowed.Contains("*"))
        {
            return true;
        }

        var separator = capability.Length;
        while ((separator = capability.LastIndexOf('.', separator - 1)) >= 0)
        {
            if (allowed.Contains(capability[..separator] + ".*"))
            {
                return true;
            }
        }

        return false;
    }

    internal void Demand(string? capability)
    {
        if (!Allows(capability))
        {
            denied?.Invoke(capability!);
            throw new InvalidOperationException($"Capability '{capability}' is not available in this instance.");
        }
    }
}

public sealed record SpellkitCommandCatalogEntry(
    string Name,
    string? Description,
    string? Capability,
    IReadOnlyList<SpellkitCommandParameter> Parameters);

public sealed class SpellkitCommandCatalog
{
    private readonly SpellkitHostEnvironment environment;
    private readonly IReadOnlyList<SpellkitCommandCatalogEntry> entries;

    internal SpellkitCommandCatalog(
        SpellkitHostEnvironment environment,
        IEnumerable<HostModuleDefinition> modules,
        IEnumerable<HostResourceDefinition> resourceTypes)
    {
        this.environment = environment;
        var result = new List<SpellkitCommandCatalogEntry>();

        foreach (var module in modules)
        {
            AddCommands(result, module.Name, module.Commands);
            foreach (var type in module.Types)
            {
                AddCommands(result, $"{module.Name}.{type.Name}", type.Commands);
            }
        }

        foreach (var resourceType in resourceTypes)
        {
            AddCommands(result, $"resource.{resourceType.TypeName}", resourceType.Commands);
        }

        entries = result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<SpellkitCommandCatalogEntry> List() =>
        entries.Where(IsVisible).ToArray();

    public IReadOnlyList<SpellkitCommandCatalogEntry> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return entries.Where(entry => IsVisible(entry)
            && entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public SpellkitCommandCatalogEntry? Describe(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return entries.FirstOrDefault(entry => IsVisible(entry)
            && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsVisible(SpellkitCommandCatalogEntry entry) =>
        environment.Capabilities.Allows(entry.Capability);

    private static void AddCommands(
        ICollection<SpellkitCommandCatalogEntry> target,
        string prefix,
        IEnumerable<SpellkitCommandDescriptor> commands)
    {
        foreach (var command in commands)
        {
            if (command.IsPropertySetter)
            {
                continue;
            }

            target.Add(new(
                $"{prefix}.{command.Name}",
                command.Description,
                command.Capability,
                command.Parameters));
        }
    }
}

internal sealed record HostResourceDefinition(
    Type ResourceType,
    string TypeName,
    SpellkitResourceLifetime Lifetime,
    IReadOnlyList<SpellkitCommandDescriptor> Commands,
    Func<SpellkitResource, IReadOnlyList<SpellkitCommandDescriptor>> Bind);

internal sealed class SpellkitResourceRegistry : IDisposable
{
    private readonly Dictionary<string, ResourceEntry> resources = new(StringComparer.Ordinal);
    private readonly Dictionary<object, ResourceEntry> sharedResources =
        new(ReferenceEqualityComparer.Instance);
    private readonly SpellkitHostEnvironment environment;
    private long nextId;
    private bool disposed;

    internal SpellkitResourceRegistry(SpellkitHostEnvironment environment) => this.environment = environment;

    internal SpkObject Create(
        object resource,
        string typeName,
        IReadOnlyList<SpellkitCommandDescriptor> commands,
        bool persistent,
        string? stableName = null,
        Action? release = null,
        bool reuseByReference = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var id = stableName ?? (++nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (resources.TryGetValue(id, out var existing))
        {
            return existing.View;
        }

        if (reuseByReference && sharedResources.TryGetValue(resource, out existing))
        {
            return existing.View;
        }

        var entry = new ResourceEntry(id, typeName, resource, commands, persistent, release);
        entry.View = CreateView(entry);
        resources.Add(id, entry);
        if (reuseByReference)
        {
            sharedResources.Add(resource, entry);
        }

        environment.Tracing.Write(
            SpellkitTraceKind.ResourceCreated,
            typeName,
            data: new Dictionary<string, object?> { ["id"] = id });
        return entry.View;
    }

    private bool IsValid(string id) => resources.ContainsKey(id);

    private bool Release(string id)
    {
        if (!resources.TryGetValue(id, out var entry) || entry.Persistent)
        {
            return false;
        }

        resources.Remove(id);
        ReleaseEntry(entry);
        return true;
    }

    internal void Reset()
    {
        List<Exception>? failures = null;
        foreach (var entry in resources.Values.Where(entry => !entry.Persistent).ToArray())
        {
            resources.Remove(entry.Id);
            try
            {
                ReleaseEntry(entry);
            }
            catch (Exception ex)
            {
                (failures ??= new()).Add(ex);
            }
        }

        ThrowReleaseFailures(failures);
    }

    public void Dispose()
    {
        List<Exception>? failures = null;
        var entries = resources.Values.ToArray();
        resources.Clear();
        sharedResources.Clear();
        disposed = true;
        foreach (var entry in entries)
        {
            try
            {
                ReleaseEntry(entry);
            }
            catch (Exception ex)
            {
                (failures ??= new()).Add(ex);
            }
        }

        ThrowReleaseFailures(failures);
    }

    private SpkObject CreateView(ResourceEntry entry)
    {
        var labels = new List<SpkLabel>
        {
            new("Id", new SpkString(entry.Id)),
            new("Type", new SpkString(entry.TypeName)),
            new("IsValid", Api("IsValid", _ => Bool(IsValid(entry.Id))))
        };
        if (!entry.Persistent)
        {
            labels.Add(new("Release", Api("Release", _ => Bool(Release(entry.Id)))));
        }

        foreach (var command in entry.Commands)
        {
            labels.Add(new(command.Name, new HostCommandFunction(Guard(entry, command))));
        }

        return new SpellkitHostViewData(labels.ToArray());
    }

    private SpkFunction Api(string name, Func<SpkObject[], SpkObject> handler) =>
        new HostApiFunction(name, Array.Empty<Par>(), (ctx, arguments) =>
        {
            EnsureOwner(ctx);
            return handler(arguments);
        });

    private void EnsureOwner(ExecutionContext context)
    {
        if (!ReferenceEquals(
            context.GetContextVariable<SpellkitHostEnvironment>(SpellkitHostEnvironment.ContextKey),
            environment))
        {
            throw new InvalidOperationException(
                "Resource handles cannot be used by a different host instance.");
        }
    }

    private SpellkitCommandDescriptor Guard(ResourceEntry entry, SpellkitCommandDescriptor command) => new(
        command.Name,
        command.Description,
        command.Capability,
        command.Parameters,
        context =>
        {
            EnsureOwner(context.ExecutionContext);
            if (!IsValid(entry.Id))
            {
                throw new InvalidOperationException(
                    $"Resource handle '{entry.Id}' is no longer valid.");
            }

            return command.Invoke(context);
        });

    private static SpkBool Bool(bool value) => value ? SpkBool.True : SpkBool.False;

    private void TraceReleased(ResourceEntry entry) => environment.Tracing.Write(
        SpellkitTraceKind.ResourceReleased,
        entry.TypeName,
        data: new Dictionary<string, object?> { ["id"] = entry.Id });

    private sealed class ResourceEntry
    {
        public ResourceEntry(
            string id,
            string typeName,
            object resource,
            IReadOnlyList<SpellkitCommandDescriptor> commands,
            bool persistent,
            Action? release) =>
            (Id, TypeName, Resource, Commands, Persistent, Release) =
            (id, typeName, resource, commands, persistent, release);

        public string Id { get; }
        public string TypeName { get; }
        public object Resource { get; }
        public IReadOnlyList<SpellkitCommandDescriptor> Commands { get; }
        public bool Persistent { get; }
        public Action? Release { get; }
        public SpkObject View { get; set; } = null!;
    }

    private void ReleaseEntry(ResourceEntry entry)
    {
        TraceReleased(entry);
        entry.Release?.Invoke();
    }

    private static void ThrowReleaseFailures(List<Exception>? failures)
    {
        if (failures is { Count: > 0 })
        {
            throw new AggregateException("One or more resource release callbacks failed.", failures);
        }
    }
}

public sealed class SpellkitHostEnvironment : IDisposable
{
    internal const string ContextKey = "Spellkit.Hosting.Environment";
    internal const string RootContextKey = "Spellkit.Hosting.Root";

    private readonly IReadOnlyDictionary<Type, HostResourceDefinition> resourceDefinitions;
    private bool disposed;

    internal SpellkitHostEnvironment(
        object? hostContext,
        IEnumerable<HostModuleDefinition> modules,
        IEnumerable<HostResourceDefinition> resourceTypes,
        IEnumerable<HostSignalDefinition> signals,
        IEnumerable<string> capabilities,
        bool unrestricted,
        IReadOnlyList<Action<SpellkitLogEntry>> logHandlers,
        IReadOnlyList<Action<SpellkitTraceEvent>> traceHandlers,
        SpellkitExecutionLimits limits,
        int? maxPendingSignals)
    {
        HostContext = hostContext;
        Limits = limits;
        Telemetry = new(logHandlers);
        Tracing = new(traceHandlers, Telemetry);
        Capabilities = new(
            capabilities,
            unrestricted,
            capability => Tracing.Write(SpellkitTraceKind.CapabilityDenied, capability));
        resourceDefinitions = resourceTypes.ToDictionary(definition => definition.ResourceType);
        Resources = new(this);
        State = new();
        Signals = new(this, signals, maxPendingSignals);
        Commands = new(this, modules, resourceDefinitions.Values);
        Root = CreateRoot();
    }

    public object? HostContext { get; }
    public SpellkitCapabilitySet Capabilities { get; }
    public SpellkitCommandCatalog Commands { get; }
    internal SpellkitResourceRegistry Resources { get; }
    public SpellkitStateStore State { get; }
    public SpellkitSignalDispatcher Signals { get; }
    public SpellkitTelemetry Telemetry { get; }
    public SpellkitTracing Tracing { get; }
    public SpellkitExecutionLimits Limits { get; }
    internal SpkObject Root { get; }

    internal void Reset()
    {
        try
        {
            Resources.Reset();
        }
        finally
        {
            State.Clear();
            Signals.Reset();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            Resources.Dispose();
        }
        finally
        {
            State.Dispose();
            Signals.Dispose();
            disposed = true;
        }
    }

    internal SpkObject CreateResource(SpellkitResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var resourceType = resource.GetType();
        if (!resourceDefinitions.TryGetValue(resourceType, out var definition))
        {
            throw new InvalidOperationException(
                $"Resource type '{resourceType.FullName}' is not registered on this host.");
        }

        var shared = definition.Lifetime == SpellkitResourceLifetime.Shared;
        return Resources.Create(
            resource,
            definition.TypeName,
            definition.Bind(resource),
            persistent: shared,
            release: resource.Release,
            reuseByReference: shared);
    }

    private SpkObject CreateRoot() => View(
        new("Commands", CreateCommandsApi()),
        new("State", CreateStateApi()),
        new("Signals", CreateSignalsApi()),
        new("Log", CreateLogApi()));

    private SpkObject CreateCommandsApi() => View(
        new("List", Api("List", Array.Empty<Par>(), _ => CatalogEntries(Commands.List()))),
        new("Find", Api("Find", new[] { new Par("text") }, arguments =>
            CatalogEntries(Commands.Find(arguments[0].ToString())))),
        new("Describe", Api("Describe", new[] { new Par("name") }, arguments =>
        {
            var entry = Commands.Describe(arguments[0].ToString());
            return entry is null ? SpkNil.Instance : CatalogEntry(entry);
        })));

    private SpkObject CreateStateApi() => IndexedView(
        (ctx, index) =>
        {
            Capabilities.Demand("state.read");
            return State.GetRaw(Key(index)) ?? SpkNil.Instance;
        },
        (ctx, index, value) =>
        {
            Capabilities.Demand("state.write");
            State.SetFromScript(Key(index), value);
        },
        new("Keys", Api("Keys", Array.Empty<Par>(), _ =>
        {
            Capabilities.Demand("state.read");
            return Strings(State.Keys);
        })),
        new("Has", Api("Has", new[] { new Par("key") }, arguments =>
        {
            Capabilities.Demand("state.read");
            return Bool(State.Contains(Key(arguments[0])));
        })),
        new("Owner", Api("Owner", new[] { new Par("key") }, arguments =>
        {
            Capabilities.Demand("state.read");
            var owner = State.GetOwner(Key(arguments[0]));
            return owner is null ? SpkNil.Instance : new SpkString(owner.Value.ToString());
        })),
        new("Remove", Api("Remove", new[] { new Par("key") }, arguments =>
        {
            Capabilities.Demand("state.write");
            return Bool(State.RemoveFromScript(Key(arguments[0])));
        })),
        new("Clear", Api("Clear", Array.Empty<Par>(), _ =>
        {
            Capabilities.Demand("state.write");
            State.ClearScript();
            return SpkNil.Instance;
        })));

    private SpkObject CreateSignalsApi() => View(
        new("List", Api("List", Array.Empty<Par>(), _ => Strings(Signals.Names))),
        new("On", Api("On", new[] { new Par("name"), new Par("handler") }, (ctx, arguments) =>
            SubscribeSignal(ctx, arguments, once: false))),
        new("Once", Api("Once", new[] { new Par("name"), new Par("handler") }, (ctx, arguments) =>
            SubscribeSignal(ctx, arguments, once: true))),
        new("Off", Api("Off", new[] { new Par("subscription") }, (ctx, arguments) =>
        {
            var id = TypeConverter.ConvertTo<long>(ctx, arguments[0]);
            return ctx.HasErrors ? SpkNil.Instance : Bool(Signals.UnsubscribeScript(id));
        })),
        new("Emit", Api("Emit", new[] { new Par("name"), new Par("payload", SpkNil.Instance) }, arguments =>
        {
            Signals.EmitFromScript(Key(arguments[0]), arguments[1]);
            return SpkNil.Instance;
        })),
        new("TryEmit", Api("TryEmit", new[] { new Par("name"), new Par("payload", SpkNil.Instance) }, arguments =>
            Bool(Signals.TryEmitFromScript(Key(arguments[0]), arguments[1])))));

    private SpkObject CreateLogApi() => View(
        new("Debug", LogApi("Debug", SpellkitLogLevel.Debug)),
        new("Info", LogApi("Info", SpellkitLogLevel.Info)),
        new("Warning", LogApi("Warning", SpellkitLogLevel.Warning)),
        new("Error", LogApi("Error", SpellkitLogLevel.Error)));

    private SpkFunction LogApi(string name, SpellkitLogLevel level) => Api(
        name,
        new[] { new Par("message"), new Par("properties", SpkNil.Instance) },
        arguments =>
        {
            Capabilities.Demand("log.write");
            Telemetry.Write(level, arguments[0].ToString(), Properties(arguments[1]));
            return SpkNil.Instance;
        });

    private SpkObject SubscribeSignal(
        ExecutionContext context,
        SpkObject[] arguments,
        bool once)
    {
        var handler = arguments[1].ToFunction(context);
        return handler is null || context.HasErrors
            ? SpkNil.Instance
            : SpkInteger.Get(Signals.SubscribeScript(Key(arguments[0]), handler, once));
    }

    private static SpkFunction Api(string name, Par[] parameters, Func<SpkObject[], SpkObject> handler) =>
        new HostApiFunction(name, parameters, (_, arguments) => handler(arguments));

    private static SpkFunction Api(
        string name,
        Par[] parameters,
        Func<ExecutionContext, SpkObject[], SpkObject> handler) =>
        new HostApiFunction(name, parameters, handler);

    private static SpkObject CatalogEntries(IEnumerable<SpellkitCommandCatalogEntry> entries) =>
        new SpkArray(entries.Select(CatalogEntry).ToArray());

    private static SpkObject CatalogEntry(SpellkitCommandCatalogEntry entry) => View(
        new("Name", new SpkString(entry.Name)),
        new("Description", entry.Description is null ? SpkNil.Instance : new SpkString(entry.Description)),
        new("Capability", entry.Capability is null ? SpkNil.Instance : new SpkString(entry.Capability)),
        new("Parameters", new SpkArray(entry.Parameters.Select(Parameter).ToArray())));

    private static SpkObject Parameter(SpellkitCommandParameter parameter) => View(
        new("Name", new SpkString(parameter.Name)),
        new("Type", new SpkString(parameter.Type.Name)),
        new("Optional", Bool(parameter.HasDefault)));

    private static SpkObject Strings(IEnumerable<string> values) =>
        new SpkArray(values.Select(value => (SpkObject)new SpkString(value)).ToArray());

    private static SpkBool Bool(bool value) => value ? SpkBool.True : SpkBool.False;

    private static SpkObject View(params SpkLabel[] labels) => new SpellkitHostViewData(labels);

    private static SpkObject IndexedView(
        Func<ExecutionContext, SpkObject, SpkObject> getter,
        Action<ExecutionContext, SpkObject, SpkObject> setter,
        params SpkLabel[] labels) => new SpellkitHostViewData(labels, getter, setter);

    private static string Key(SpkObject value)
    {
        if (value.TypeId is not Spk.String and not Spk.Char)
        {
            throw new InvalidOperationException("Host keys must be strings or characters.");
        }

        return value.ToString();
    }

    private static IReadOnlyDictionary<string, object?> Properties(SpkObject value)
    {
        if (value.TypeId == Spk.Nil)
        {
            return new Dictionary<string, object?>();
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (value is SpkDictionary dictionary)
        {
            foreach (var (key, item) in dictionary.Dictionary)
            {
                result[key.ToString()] = PropertyValue(item);
            }
        }
        else if (value is SpkTuple tuple)
        {
            for (var i = 0; i < tuple.Count; i++)
            {
                result[tuple.GetKey(i) ?? i.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                    PropertyValue(tuple[i]);
            }
        }
        else
        {
            result["value"] = PropertyValue(value);
        }

        return result;
    }

    private static object? PropertyValue(SpkObject value) =>
        value.TypeId == Spk.Nil ? null : value.ToObject();
}

internal sealed class SpellkitHostViewData : SpkTuple
{
    public SpellkitHostViewData(SpkObject[] values) : base(values) { }

    public SpellkitHostViewData(
        SpkObject[] values,
        Func<ExecutionContext, SpkObject, SpkObject> getter,
        Action<ExecutionContext, SpkObject, SpkObject> setter) : base(values) =>
        (Getter, Setter) = (getter, setter);

    public Func<ExecutionContext, SpkObject, SpkObject>? Getter { get; }
    public Action<ExecutionContext, SpkObject, SpkObject>? Setter { get; }
}

internal sealed class SpellkitHostRoot : SpkForeignObject
{
    public SpellkitHostRoot(SpellkitHostRootTypeInfo typeInfo) : base(typeInfo) { }

    public override object ToObject() => this;
    public override SpkObject Clone() => this;
    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);
}

internal sealed class SpellkitHostObject : SpkForeignObject
{
    public SpellkitHostObject(SpellkitHostRootTypeInfo typeInfo, SpellkitHostViewData data) : base(typeInfo) =>
        Data = data;

    public SpellkitHostViewData Data { get; }
    public override object ToObject() => Data;
    public override SpkObject Clone() => this;
    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);
}

internal sealed class SpellkitHostRootTypeInfo : SpkForeignTypeInfo
{
    public override string ReflectedTypeName => "Host";

    internal override SpkObject GetInstanceMember(
        SpkObject self,
        HashString name,
        ExecutionContext ctx)
    {
        var view = self is SpellkitHostObject hostObject
            ? hostObject.Data
            : ctx.GetContextVariable<SpkObject>(SpellkitHostEnvironment.RootContextKey) as SpellkitHostViewData;
        if (view is not null && view.TryGetItem((string)name, out var value))
        {
            return Wrap(ctx, value!);
        }

        return ctx.OperationNotSupported((string)name, self);
    }

    internal static SpkObject Wrap(ExecutionContext ctx, SpkObject value)
    {
        if (value is SpellkitHostViewData view)
        {
            return new SpellkitHostObject(ctx.Type<SpellkitHostRootTypeInfo>(), view);
        }

        if (value is SpkArray array)
        {
            return new SpkArray(array.Select(item => Wrap(ctx, item)).ToArray());
        }

        return value;
    }

    protected override SpkObject GetOp(
        ExecutionContext ctx,
        SpkObject self,
        SpkObject index)
    {
        if (self is SpellkitHostObject { Data.Getter: not null } hostObject)
        {
            try
            {
                return hostObject.Data.Getter(ctx, index);
            }
            catch (Exception ex)
            {
                return ctx.Failure(ex.Message);
            }
        }
        return base.GetOp(ctx, self, index);
    }

    protected override SpkObject SetOp(
        ExecutionContext ctx,
        SpkObject self,
        SpkObject index,
        SpkObject value)
    {
        if (self is SpellkitHostObject { Data.Setter: not null } hostObject)
        {
            try
            {
                hostObject.Data.Setter(ctx, index, value);
                return SpkNil.Instance;
            }
            catch (Exception ex)
            {
                return ctx.Failure(ex.Message);
            }
        }
        return base.SetOp(ctx, self, index, value);
    }
}

internal sealed class HostApiFunction : SpkForeignFunction
{
    private readonly Func<ExecutionContext, SpkObject[], SpkObject> handler;

    public HostApiFunction(
        string name,
        Par[] parameters,
        Func<ExecutionContext, SpkObject[], SpkObject> handler)
        : base(name, parameters) => this.handler = handler;

    protected override SpkObject CallWithMemoryLayout(ExecutionContext ctx, SpkObject[] args)
    {
        try
        {
            return SpellkitHostRootTypeInfo.Wrap(ctx, handler(ctx, args));
        }
        catch (Exception ex)
        {
            return ctx.ExternalFunctionFailure(this, ex.Message);
        }
    }

    protected override bool Equals(SpkFunction func) => ReferenceEquals(this, func);
}
