using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Spellkit.Compiler;
using Spellkit.Linker;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Hosting;

public delegate SpellkitObject SpellkitCommandHandler(SpellkitCommandContext context);

public sealed class SpellkitCommandParameter
{
    internal SpellkitCommandParameter(string name, Type type, bool hasDefault, object? defaultValue)
    {
        HostNames.ValidateIdentifier(name, nameof(name), "command parameter");

        Name = name;
        Type = type ?? throw new ArgumentNullException(nameof(type));
        HasDefault = hasDefault;
        DefaultValue = defaultValue;
    }

    public string Name { get; }

    public Type Type { get; }

    public bool HasDefault { get; }

    public object? DefaultValue { get; }

    public static SpellkitCommandParameter Required<T>(string name) =>
        new(name, typeof(T), false, null);

    public static SpellkitCommandParameter Optional<T>(string name, T defaultValue) =>
        new(name, typeof(T), true, defaultValue);
}

public sealed class SpellkitCommandDescriptor
{
    private readonly SpellkitCommandHandler handler;

    internal SpellkitCommandDescriptor(
        string name,
        string? description,
        string? capability,
        IReadOnlyList<SpellkitCommandParameter> parameters,
        SpellkitCommandHandler handler,
        bool propertyGetter = false,
        bool propertySetter = false) =>
        (Name, Description, Capability, Parameters, this.handler, IsPropertyGetter, IsPropertySetter) =
        (name, description, capability, parameters, handler, propertyGetter, propertySetter);

    public string Name { get; }

    public string? Description { get; }

    public string? Capability { get; }

    public IReadOnlyList<SpellkitCommandParameter> Parameters { get; }

    internal bool IsPropertyGetter { get; }

    internal bool IsPropertySetter { get; }

    internal SpellkitObject Invoke(SpellkitCommandContext context) => handler(context);
}

public sealed class SpellkitModuleBuilder
{
    private readonly Dictionary<string, SpellkitCommandDescriptor> commands =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpellkitTypeBuilder> types =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Func<SpellkitForeignTypeInfo>> foreignTypes = new();
    private Func<ForeignUnit>? unitFactory;

    internal SpellkitModuleBuilder(string name)
    {
        HostNames.ValidateDottedName(name, nameof(name), "module");

        Name = name;
    }

    public string Name { get; }

    public IReadOnlyCollection<SpellkitCommandDescriptor> Commands => commands.Values;

    public IReadOnlyCollection<SpellkitTypeBuilder> Types => types.Values;

    public SpellkitModuleBuilder Command(
        string name,
        Func<SpellkitCommandContext, object?> handler,
        params SpellkitCommandParameter[] parameters) =>
        Command(name, null, null, handler, parameters);

    public SpellkitModuleBuilder Command(
        string name,
        string? description,
        Func<SpellkitCommandContext, object?> handler,
        params SpellkitCommandParameter[] parameters) =>
        Command(name, description, null, handler, parameters);

    public SpellkitModuleBuilder Command(
        string name,
        string? description,
        string? capability,
        Func<SpellkitCommandContext, object?> handler,
        params SpellkitCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => SpellkitCommandConvert.FromObject<object?>(handler(context)),
            parameters);
    }

    public SpellkitModuleBuilder Command<TResult>(
        string name,
        Func<SpellkitCommandContext, TResult> handler,
        params SpellkitCommandParameter[] parameters) =>
        Command(name, null, null, handler, parameters);

    public SpellkitModuleBuilder Command<TResult>(
        string name,
        string? description,
        Func<SpellkitCommandContext, TResult> handler,
        params SpellkitCommandParameter[] parameters) =>
        Command(name, description, null, handler, parameters);

    public SpellkitModuleBuilder Command<TResult>(
        string name,
        string? description,
        string? capability,
        Func<SpellkitCommandContext, TResult> handler,
        params SpellkitCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => SpellkitCommandConvert.FromObject<TResult>(handler(context)),
            parameters);
    }

    public SpellkitModuleBuilder AsyncCommand<TResult>(
        string name,
        Func<SpellkitCommandContext, ValueTask<TResult>> handler,
        params SpellkitCommandParameter[] parameters) =>
        AsyncCommand(name, null, null, handler, parameters);

    public SpellkitModuleBuilder AsyncCommand<TResult>(
        string name,
        string? description,
        string? capability,
        Func<SpellkitCommandContext, ValueTask<TResult>> handler,
        params SpellkitCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => SpellkitCommandConvert.FromAwaitable(handler(context)),
            parameters);
    }

    public SpellkitModuleBuilder AsyncCommand(
        string name,
        Func<SpellkitCommandContext, ValueTask> handler,
        params SpellkitCommandParameter[] parameters) =>
        AsyncCommand(name, null, null, handler, parameters);

    public SpellkitModuleBuilder AsyncCommand(
        string name,
        string? description,
        string? capability,
        Func<SpellkitCommandContext, ValueTask> handler,
        params SpellkitCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => SpellkitCommandConvert.FromAwaitable(handler(context)),
            parameters);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public SpellkitModuleBuilder RawCommand(
        string name,
        string? description,
        string? capability,
        SpellkitCommandHandler handler,
        params SpellkitCommandParameter[] parameters)
    {
        EnsureGeneratedModule();
        HostNames.ValidateIdentifier(name, nameof(name), "command");
        HostNames.ValidateCapability(capability, nameof(capability), optional: true);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(parameters);

        if (commands.ContainsKey(name))
        {
            throw new InvalidOperationException($"Command '{Name}.{name}' is already registered.");
        }

        var descriptor = new SpellkitCommandDescriptor(
            name, description, capability, HostNames.Snapshot(parameters), handler);
        commands.Add(name, descriptor);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public SpellkitModuleBuilder RawProperty(
        string name,
        string? description,
        string? capability,
        SpellkitCommandHandler getter,
        SpellkitCommandHandler? setter = null,
        SpellkitCommandParameter? valueParameter = null)
    {
        EnsureGeneratedModule();
        HostNames.ValidateIdentifier(name, nameof(name), "property");
        HostNames.ValidateCapability(capability, nameof(capability), optional: true);
        ArgumentNullException.ThrowIfNull(getter);
        if (setter is not null && valueParameter is null)
        {
            throw new ArgumentNullException(nameof(valueParameter));
        }

        if (setter is null && valueParameter is not null)
        {
            throw new ArgumentException(
                "A value parameter requires a property setter.",
                nameof(valueParameter));
        }

        var setterName = Builtins.Setter(name);
        if (commands.ContainsKey(name) || commands.ContainsKey(setterName))
        {
            throw new InvalidOperationException(
                $"Property '{Name}.{name}' is already registered.");
        }

        commands.Add(name, new(
            name,
            description,
            capability,
            Array.Empty<SpellkitCommandParameter>(),
            getter,
            propertyGetter: true));
        if (setter is not null)
        {
            commands.Add(setterName, new SpellkitCommandDescriptor(
                setterName,
                description,
                capability,
                new[] { valueParameter! },
                setter,
                propertySetter: true));
        }
        return this;
    }

    public SpellkitModuleBuilder Type(string name, Action<SpellkitTypeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(Type(name));
        return this;
    }

    public SpellkitTypeBuilder Type(string name)
    {
        EnsureGeneratedModule();
        HostNames.ValidateIdentifier(name, nameof(name), "host type");

        if (types.ContainsKey(name))
        {
            throw new InvalidOperationException($"Host type '{Name}.{name}' is already registered.");
        }

        var type = new SpellkitTypeBuilder(name);
        types.Add(name, type);
        return type;
    }

    public SpellkitModuleBuilder ForeignType(Func<SpellkitForeignTypeInfo> factory)
    {
        EnsureGeneratedModule();
        ArgumentNullException.ThrowIfNull(factory);
        foreignTypes.Add(factory);
        return this;
    }

    public SpellkitModuleBuilder Unit(Func<ForeignUnit> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (unitFactory is not null)
        {
            throw new InvalidOperationException($"Host module '{Name}' already has a custom unit factory.");
        }

        if (commands.Count != 0 || types.Count != 0 || foreignTypes.Count != 0)
        {
            throw new InvalidOperationException(
                $"Host module '{Name}' cannot combine a custom unit with generated registrations.");
        }

        unitFactory = factory;
        return this;
    }

    public SpellkitModuleBuilder Command(
        string name,
        Action<SpellkitCommandContext> handler,
        params SpellkitCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Command(name, null, (Func<SpellkitCommandContext, object?>)(context =>
        {
            handler(context);
            return null;
        }), parameters);
    }

    internal HostModuleDefinition Build() => new(
        Name,
        commands.Values.ToArray(),
        types.Values.Select(t => t.Build()).ToArray(),
        foreignTypes.ToArray(),
        unitFactory);

    private void EnsureGeneratedModule()
    {
        if (unitFactory is not null)
        {
            throw new InvalidOperationException(
                $"Host module '{Name}' uses a custom unit and cannot add generated registrations.");
        }
    }
}

internal sealed record HostModuleDefinition(
    string Name,
    IReadOnlyList<SpellkitCommandDescriptor> Commands,
    IReadOnlyList<HostTypeDefinition> Types,
    IReadOnlyList<Func<SpellkitForeignTypeInfo>> ForeignTypes,
    Func<ForeignUnit>? UnitFactory);

public sealed class SpellkitTypeBuilder
{
    private readonly Dictionary<string, SpellkitCommandDescriptor> commands =
        new(StringComparer.OrdinalIgnoreCase);

    internal SpellkitTypeBuilder(string name)
    {
        HostNames.ValidateIdentifier(name, nameof(name), "host type");

        Name = name;
    }

    public string Name { get; }

    public IReadOnlyCollection<SpellkitCommandDescriptor> Commands => commands.Values;

    public SpellkitTypeBuilder Command(
        string name,
        Func<SpellkitCommandContext, object?> handler,
        params SpellkitCommandParameter[] parameters) =>
        Command(name, null, null, handler, parameters);

    public SpellkitTypeBuilder Command(
        string name,
        string? description,
        Func<SpellkitCommandContext, object?> handler,
        params SpellkitCommandParameter[] parameters) =>
        Command(name, description, null, handler, parameters);

    public SpellkitTypeBuilder Command(
        string name,
        string? description,
        string? capability,
        Func<SpellkitCommandContext, object?> handler,
        params SpellkitCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => SpellkitCommandConvert.FromObject<object?>(handler(context)),
            parameters);
    }

    public SpellkitTypeBuilder Command<TResult>(
        string name,
        Func<SpellkitCommandContext, TResult> handler,
        params SpellkitCommandParameter[] parameters) =>
        Command(name, null, null, handler, parameters);

    public SpellkitTypeBuilder Command<TResult>(
        string name,
        string? description,
        Func<SpellkitCommandContext, TResult> handler,
        params SpellkitCommandParameter[] parameters) =>
        Command(name, description, null, handler, parameters);

    public SpellkitTypeBuilder Command<TResult>(
        string name,
        string? description,
        string? capability,
        Func<SpellkitCommandContext, TResult> handler,
        params SpellkitCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => SpellkitCommandConvert.FromObject<TResult>(handler(context)),
            parameters);
    }

    public SpellkitTypeBuilder AsyncCommand<TResult>(
        string name,
        Func<SpellkitCommandContext, ValueTask<TResult>> handler,
        params SpellkitCommandParameter[] parameters) =>
        AsyncCommand(name, null, null, handler, parameters);

    public SpellkitTypeBuilder AsyncCommand<TResult>(
        string name,
        string? description,
        string? capability,
        Func<SpellkitCommandContext, ValueTask<TResult>> handler,
        params SpellkitCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => SpellkitCommandConvert.FromAwaitable(handler(context)),
            parameters);
    }

    public SpellkitTypeBuilder AsyncCommand(
        string name,
        Func<SpellkitCommandContext, ValueTask> handler,
        params SpellkitCommandParameter[] parameters) =>
        AsyncCommand(name, null, null, handler, parameters);

    public SpellkitTypeBuilder AsyncCommand(
        string name,
        string? description,
        string? capability,
        Func<SpellkitCommandContext, ValueTask> handler,
        params SpellkitCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => SpellkitCommandConvert.FromAwaitable(handler(context)),
            parameters);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public SpellkitTypeBuilder RawCommand(
        string name,
        string? description,
        string? capability,
        SpellkitCommandHandler handler,
        params SpellkitCommandParameter[] parameters)
    {
        HostNames.ValidateIdentifier(name, nameof(name), "command");
        HostNames.ValidateCapability(capability, nameof(capability), optional: true);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(parameters);

        if (commands.ContainsKey(name))
        {
            throw new InvalidOperationException($"Command '{Name}.{name}' is already registered.");
        }

        commands.Add(name, new(
            name, description, capability, HostNames.Snapshot(parameters), handler));
        return this;
    }

    internal HostTypeDefinition Build() => new(Name, commands.Values.ToArray());
}

internal sealed record HostTypeDefinition(
    string Name,
    IReadOnlyList<SpellkitCommandDescriptor> Commands);
