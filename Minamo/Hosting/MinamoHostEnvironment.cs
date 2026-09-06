using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Minamo.Compiler;
using Minamo.Debug;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Hosting;

public sealed class MinamoCapabilitySet
{
    private readonly HashSet<string> allowed;
    private readonly ReadOnlyCollection<string> allowedView;
    private readonly Action<string>? denied;

    internal MinamoCapabilitySet(
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

public sealed record MinamoCommandCatalogEntry(
    string Name,
    string? Description,
    string? Capability,
    IReadOnlyList<MinamoCommandParameter> Parameters);

public sealed class MinamoCommandCatalog
{
    private readonly MinamoHostEnvironment environment;
    private readonly IReadOnlyList<MinamoCommandCatalogEntry> entries;

    internal MinamoCommandCatalog(
        MinamoHostEnvironment environment,
        IEnumerable<HostModuleDefinition> modules,
        IEnumerable<HostResourceDefinition> resourceTypes)
    {
        this.environment = environment;
        var result = new List<MinamoCommandCatalogEntry>();

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

    public IReadOnlyList<MinamoCommandCatalogEntry> List() =>
        entries.Where(IsVisible).ToArray();

    public IReadOnlyList<MinamoCommandCatalogEntry> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return entries.Where(entry => IsVisible(entry)
            && entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public MinamoCommandCatalogEntry? Describe(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return entries.FirstOrDefault(entry => IsVisible(entry)
            && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsVisible(MinamoCommandCatalogEntry entry) =>
        environment.Capabilities.Allows(entry.Capability);

    private static void AddCommands(
        ICollection<MinamoCommandCatalogEntry> target,
        string prefix,
        IEnumerable<MinamoCommandDescriptor> commands)
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
    MinamoResourceLifetime Lifetime,
    IReadOnlyList<MinamoCommandDescriptor> Commands,
    Func<MinamoResource, IReadOnlyList<MinamoCommandDescriptor>> Bind);

internal sealed class MinamoResourceRegistry : IDisposable
{
    private readonly Dictionary<string, ResourceEntry> resources = new(StringComparer.Ordinal);
    private readonly Dictionary<object, ResourceEntry> sharedResources =
        new(ReferenceEqualityComparer.Instance);
    private readonly MinamoHostEnvironment environment;
    private long nextId;
    private bool disposed;

    internal MinamoResourceRegistry(MinamoHostEnvironment environment) => this.environment = environment;

    internal MinamoObject Create(
        object resource,
        string typeName,
        IReadOnlyList<MinamoCommandDescriptor> commands,
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
            MinamoTraceKind.ResourceCreated,
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

    private MinamoObject CreateView(ResourceEntry entry)
    {
        var labels = new List<MinamoLabel>
        {
            new("Id", new MinamoString(entry.Id)),
            new("Type", new MinamoString(entry.TypeName)),
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

        return new MinamoHostViewData(labels.ToArray());
    }

    private MinamoFunction Api(string name, Func<MinamoObject[], MinamoObject> handler) =>
        new HostApiFunction(name, Array.Empty<Par>(), (ctx, arguments) =>
        {
            EnsureOwner(ctx);
            return handler(arguments);
        });

    private void EnsureOwner(ExecutionContext context)
    {
        if (!ReferenceEquals(
            context.GetContextVariable<MinamoHostEnvironment>(MinamoHostEnvironment.ContextKey),
            environment))
        {
            throw new InvalidOperationException(
                "Resource handles cannot be used by a different host instance.");
        }
    }

    private MinamoCommandDescriptor Guard(ResourceEntry entry, MinamoCommandDescriptor command) => new(
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

    private static MinamoBool Bool(bool value) => value ? MinamoBool.True : MinamoBool.False;

    private void TraceReleased(ResourceEntry entry) => environment.Tracing.Write(
        MinamoTraceKind.ResourceReleased,
        entry.TypeName,
        data: new Dictionary<string, object?> { ["id"] = entry.Id });

    private sealed class ResourceEntry
    {
        public ResourceEntry(
            string id,
            string typeName,
            object resource,
            IReadOnlyList<MinamoCommandDescriptor> commands,
            bool persistent,
            Action? release) =>
            (Id, TypeName, Resource, Commands, Persistent, Release) =
            (id, typeName, resource, commands, persistent, release);

        public string Id { get; }
        public string TypeName { get; }
        public object Resource { get; }
        public IReadOnlyList<MinamoCommandDescriptor> Commands { get; }
        public bool Persistent { get; }
        public Action? Release { get; }
        public MinamoObject View { get; set; } = null!;
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

public sealed class MinamoHostEnvironment : IDisposable
{
    internal const string ContextKey = "Minamo.Hosting.Environment";
    internal const string RootContextKey = "Minamo.Hosting.Root";

    private readonly IReadOnlyDictionary<Type, HostResourceDefinition> resourceDefinitions;
    private bool disposed;

    internal MinamoHostEnvironment(
        object? hostContext,
        IEnumerable<HostModuleDefinition> modules,
        IEnumerable<HostResourceDefinition> resourceTypes,
        IEnumerable<HostSignalDefinition> signals,
        IEnumerable<string> capabilities,
        bool unrestricted,
        IReadOnlyList<Action<MinamoLogEntry>> logHandlers,
        IReadOnlyList<Action<MinamoTraceEvent>> traceHandlers,
        MinamoExecutionLimits limits,
        int? maxPendingSignals)
    {
        HostContext = hostContext;
        Limits = limits;
        Telemetry = new(logHandlers);
        Tracing = new(traceHandlers, Telemetry);
        Capabilities = new(
            capabilities,
            unrestricted,
            capability => Tracing.Write(MinamoTraceKind.CapabilityDenied, capability));
        resourceDefinitions = resourceTypes.ToDictionary(definition => definition.ResourceType);
        Resources = new(this);
        State = new();
        Signals = new(this, signals, maxPendingSignals);
        Commands = new(this, modules, resourceDefinitions.Values);
        Root = CreateRoot();
    }

    public object? HostContext { get; }
    public MinamoCapabilitySet Capabilities { get; }
    public MinamoCommandCatalog Commands { get; }
    internal MinamoResourceRegistry Resources { get; }
    public MinamoStateStore State { get; }
    public MinamoSignalDispatcher Signals { get; }
    public MinamoTelemetry Telemetry { get; }
    public MinamoTracing Tracing { get; }
    public MinamoExecutionLimits Limits { get; }
    internal MinamoObject Root { get; }

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

    internal MinamoObject CreateResource(MinamoResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var resourceType = resource.GetType();
        if (!resourceDefinitions.TryGetValue(resourceType, out var definition))
        {
            throw new InvalidOperationException(
                $"Resource type '{resourceType.FullName}' is not registered on this host.");
        }

        var shared = definition.Lifetime == MinamoResourceLifetime.Shared;
        return Resources.Create(
            resource,
            definition.TypeName,
            definition.Bind(resource),
            persistent: shared,
            release: resource.Release,
            reuseByReference: shared);
    }

    private MinamoObject CreateRoot() => View(
        new("Commands", CreateCommandsApi()),
        new("State", CreateStateApi()),
        new("Signals", CreateSignalsApi()),
        new("Log", CreateLogApi()));

    private MinamoObject CreateCommandsApi() => View(
        new("List", Api("List", Array.Empty<Par>(), _ => CatalogEntries(Commands.List()))),
        new("Find", Api("Find", new[] { new Par("text") }, arguments =>
            CatalogEntries(Commands.Find(arguments[0].ToString())))),
        new("Describe", Api("Describe", new[] { new Par("name") }, arguments =>
        {
            var entry = Commands.Describe(arguments[0].ToString());
            return entry is null ? MinamoNil.Instance : CatalogEntry(entry);
        })));

    private MinamoObject CreateStateApi() => IndexedView(
        (ctx, index) =>
        {
            Capabilities.Demand("state.read");
            return State.GetRaw(Key(index)) ?? MinamoNil.Instance;
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
            return owner is null ? MinamoNil.Instance : new MinamoString(owner.Value.ToString());
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
            return MinamoNil.Instance;
        })));

    private MinamoObject CreateSignalsApi() => View(
        new("List", Api("List", Array.Empty<Par>(), _ => Strings(Signals.Names))),
        new("On", Api("On", new[] { new Par("name"), new Par("handler") }, (ctx, arguments) =>
            SubscribeSignal(ctx, arguments, once: false))),
        new("Once", Api("Once", new[] { new Par("name"), new Par("handler") }, (ctx, arguments) =>
            SubscribeSignal(ctx, arguments, once: true))),
        new("Off", Api("Off", new[] { new Par("subscription") }, (ctx, arguments) =>
        {
            var id = TypeConverter.ConvertTo<long>(ctx, arguments[0]);
            return ctx.HasErrors ? MinamoNil.Instance : Bool(Signals.UnsubscribeScript(id));
        })),
        new("Emit", Api("Emit", new[] { new Par("name"), new Par("payload", MinamoNil.Instance) }, arguments =>
        {
            Signals.EmitFromScript(Key(arguments[0]), arguments[1]);
            return MinamoNil.Instance;
        })),
        new("TryEmit", Api("TryEmit", new[] { new Par("name"), new Par("payload", MinamoNil.Instance) }, arguments =>
            Bool(Signals.TryEmitFromScript(Key(arguments[0]), arguments[1])))));

    private MinamoObject CreateLogApi() => View(
        new("Debug", LogApi("Debug", MinamoLogLevel.Debug)),
        new("Info", LogApi("Info", MinamoLogLevel.Info)),
        new("Warning", LogApi("Warning", MinamoLogLevel.Warning)),
        new("Error", LogApi("Error", MinamoLogLevel.Error)));

    private MinamoFunction LogApi(string name, MinamoLogLevel level) => Api(
        name,
        new[] { new Par("message"), new Par("properties", MinamoNil.Instance) },
        arguments =>
        {
            Capabilities.Demand("log.write");
            Telemetry.Write(level, arguments[0].ToString(), Properties(arguments[1]));
            return MinamoNil.Instance;
        });

    private MinamoObject SubscribeSignal(
        ExecutionContext context,
        MinamoObject[] arguments,
        bool once)
    {
        var handler = arguments[1].ToFunction(context);
        return handler is null || context.HasErrors
            ? MinamoNil.Instance
            : MinamoInteger.Get(Signals.SubscribeScript(Key(arguments[0]), handler, once));
    }

    private static MinamoFunction Api(string name, Par[] parameters, Func<MinamoObject[], MinamoObject> handler) =>
        new HostApiFunction(name, parameters, (_, arguments) => handler(arguments));

    private static MinamoFunction Api(
        string name,
        Par[] parameters,
        Func<ExecutionContext, MinamoObject[], MinamoObject> handler) =>
        new HostApiFunction(name, parameters, handler);

    private static MinamoObject CatalogEntries(IEnumerable<MinamoCommandCatalogEntry> entries) =>
        new MinamoArray(entries.Select(CatalogEntry).ToArray());

    private static MinamoObject CatalogEntry(MinamoCommandCatalogEntry entry) => View(
        new("Name", new MinamoString(entry.Name)),
        new("Description", entry.Description is null ? MinamoNil.Instance : new MinamoString(entry.Description)),
        new("Capability", entry.Capability is null ? MinamoNil.Instance : new MinamoString(entry.Capability)),
        new("Parameters", new MinamoArray(entry.Parameters.Select(Parameter).ToArray())));

    private static MinamoObject Parameter(MinamoCommandParameter parameter) => View(
        new("Name", new MinamoString(parameter.Name)),
        new("Type", new MinamoString(parameter.Type.Name)),
        new("Optional", Bool(parameter.HasDefault)));

    private static MinamoObject Strings(IEnumerable<string> values) =>
        new MinamoArray(values.Select(value => (MinamoObject)new MinamoString(value)).ToArray());

    private static MinamoBool Bool(bool value) => value ? MinamoBool.True : MinamoBool.False;

    private static MinamoObject View(params MinamoLabel[] labels) => new MinamoHostViewData(labels);

    private static MinamoObject IndexedView(
        Func<ExecutionContext, MinamoObject, MinamoObject> getter,
        Action<ExecutionContext, MinamoObject, MinamoObject> setter,
        params MinamoLabel[] labels) => new MinamoHostViewData(labels, getter, setter);

    private static string Key(MinamoObject value)
    {
        if (value.TypeId is not MinamoTypeCodes.String and not MinamoTypeCodes.Char)
        {
            throw new InvalidOperationException("Host keys must be strings or characters.");
        }

        return value.ToString();
    }

    private static IReadOnlyDictionary<string, object?> Properties(MinamoObject value)
    {
        if (value.TypeId == MinamoTypeCodes.Nil)
        {
            return new Dictionary<string, object?>();
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (value is MinamoDictionary dictionary)
        {
            foreach (var (key, item) in dictionary.Dictionary)
            {
                result[key.ToString()] = PropertyValue(item);
            }
        }
        else if (value is MinamoTuple tuple)
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

    private static object? PropertyValue(MinamoObject value) =>
        value.TypeId == MinamoTypeCodes.Nil ? null : value.ToObject();
}

internal sealed class MinamoHostViewData : MinamoTuple
{
    public MinamoHostViewData(MinamoObject[] values) : base(values) { }

    public MinamoHostViewData(
        MinamoObject[] values,
        Func<ExecutionContext, MinamoObject, MinamoObject> getter,
        Action<ExecutionContext, MinamoObject, MinamoObject> setter) : base(values) =>
        (Getter, Setter) = (getter, setter);

    public Func<ExecutionContext, MinamoObject, MinamoObject>? Getter { get; }
    public Action<ExecutionContext, MinamoObject, MinamoObject>? Setter { get; }
}

internal sealed class MinamoHostRoot : MinamoForeignObject
{
    public MinamoHostRoot(MinamoHostRootTypeInfo typeInfo) : base(typeInfo) { }

    public override object ToObject() => this;
    public override MinamoObject Clone() => this;
    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);
}

internal sealed class MinamoHostObject : MinamoForeignObject
{
    public MinamoHostObject(MinamoHostRootTypeInfo typeInfo, MinamoHostViewData data) : base(typeInfo) =>
        Data = data;

    public MinamoHostViewData Data { get; }
    public override object ToObject() => Data;
    public override MinamoObject Clone() => this;
    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);
}

internal sealed class MinamoHostRootTypeInfo : MinamoForeignTypeInfo
{
    public override string ReflectedTypeName => "Host";

    internal override MinamoObject GetInstanceMember(
        MinamoObject self,
        HashString name,
        ExecutionContext ctx)
    {
        var view = self is MinamoHostObject hostObject
            ? hostObject.Data
            : ctx.GetContextVariable<MinamoObject>(MinamoHostEnvironment.RootContextKey) as MinamoHostViewData;
        if (view is not null && view.TryGetItem((string)name, out var value))
        {
            return Wrap(ctx, value!);
        }

        return ctx.OperationNotSupported((string)name, self);
    }

    internal static MinamoObject Wrap(ExecutionContext ctx, MinamoObject value)
    {
        if (value is MinamoHostViewData view)
        {
            return new MinamoHostObject(ctx.Type<MinamoHostRootTypeInfo>(), view);
        }

        if (value is MinamoArray array)
        {
            return new MinamoArray(array.Select(item => Wrap(ctx, item)).ToArray());
        }

        return value;
    }

    protected override MinamoObject GetOp(
        ExecutionContext ctx,
        MinamoObject self,
        MinamoObject index)
    {
        if (self is MinamoHostObject { Data.Getter: not null } hostObject)
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

    protected override MinamoObject SetOp(
        ExecutionContext ctx,
        MinamoObject self,
        MinamoObject index,
        MinamoObject value)
    {
        if (self is MinamoHostObject { Data.Setter: not null } hostObject)
        {
            try
            {
                hostObject.Data.Setter(ctx, index, value);
                return MinamoNil.Instance;
            }
            catch (Exception ex)
            {
                return ctx.Failure(ex.Message);
            }
        }
        return base.SetOp(ctx, self, index, value);
    }
}

internal sealed class HostApiFunction : MinamoForeignFunction
{
    private readonly Func<ExecutionContext, MinamoObject[], MinamoObject> handler;

    public HostApiFunction(
        string name,
        Par[] parameters,
        Func<ExecutionContext, MinamoObject[], MinamoObject> handler)
        : base(name, parameters) => this.handler = handler;

    protected override MinamoObject CallWithMemoryLayout(ExecutionContext ctx, MinamoObject[] args)
    {
        try
        {
            return MinamoHostRootTypeInfo.Wrap(ctx, handler(ctx, args));
        }
        catch (Exception ex)
        {
            return ctx.ExternalFunctionFailure(this, ex.Message);
        }
    }

    protected override bool Equals(MinamoFunction func) => ReferenceEquals(this, func);
}
