using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Minamo.Compiler;
using Minamo.Linker;
using Minamo.Runtime;
using Minamo.Runtime.Types;

namespace Minamo.Hosting;

public delegate MinamoObject MinamoCommandHandler(MinamoCommandContext context);

public sealed class MinamoCommandParameter
{
    internal MinamoCommandParameter(string name, Type type, bool hasDefault, object? defaultValue)
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

    public static MinamoCommandParameter Required<T>(string name) =>
        new(name, typeof(T), false, null);

    public static MinamoCommandParameter Optional<T>(string name, T defaultValue) =>
        new(name, typeof(T), true, defaultValue);

    internal static ReadOnlyCollection<MinamoCommandParameter> Snapshot(
        MinamoCommandParameter[] parameters) =>
        Array.AsReadOnly((MinamoCommandParameter[])parameters.Clone());
}

public sealed class MinamoCommandDescriptor
{
    private readonly MinamoCommandHandler handler;

    internal MinamoCommandDescriptor(
        string name,
        string? description,
        string? capability,
        IReadOnlyList<MinamoCommandParameter> parameters,
        MinamoCommandHandler handler,
        bool propertyGetter = false,
        bool propertySetter = false) =>
        (Name, Description, Capability, Parameters, this.handler, IsPropertyGetter, IsPropertySetter) =
        (name, description, capability, parameters, handler, propertyGetter, propertySetter);

    public string Name { get; }

    public string? Description { get; }

    public string? Capability { get; }

    public IReadOnlyList<MinamoCommandParameter> Parameters { get; }

    internal bool IsPropertyGetter { get; }

    internal bool IsPropertySetter { get; }

    internal MinamoObject Invoke(MinamoCommandContext context) => handler(context);
}

public sealed class MinamoModuleBuilder
{
    private readonly Dictionary<string, MinamoCommandDescriptor> commands =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MinamoTypeBuilder> types =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Func<MinamoForeignTypeInfo>> foreignTypes = new();
    private Func<ForeignUnit>? unitFactory;

    internal MinamoModuleBuilder(string name)
    {
        HostNames.ValidateDottedName(name, nameof(name), "module");

        Name = name;
    }

    public string Name { get; }

    public IReadOnlyCollection<MinamoCommandDescriptor> Commands => commands.Values;

    public IReadOnlyCollection<MinamoTypeBuilder> Types => types.Values;

    public MinamoModuleBuilder Command(
        string name,
        Func<MinamoCommandContext, object?> handler,
        params MinamoCommandParameter[] parameters) =>
        Command(name, null, null, handler, parameters);

    public MinamoModuleBuilder Command(
        string name,
        string? description,
        Func<MinamoCommandContext, object?> handler,
        params MinamoCommandParameter[] parameters) =>
        Command(name, description, null, handler, parameters);

    public MinamoModuleBuilder Command(
        string name,
        string? description,
        string? capability,
        Func<MinamoCommandContext, object?> handler,
        params MinamoCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => MinamoCommandConvert.FromObject<object?>(handler(context)),
            parameters);
    }

    public MinamoModuleBuilder Command<TResult>(
        string name,
        Func<MinamoCommandContext, TResult> handler,
        params MinamoCommandParameter[] parameters) =>
        Command(name, null, null, handler, parameters);

    public MinamoModuleBuilder Command<TResult>(
        string name,
        string? description,
        Func<MinamoCommandContext, TResult> handler,
        params MinamoCommandParameter[] parameters) =>
        Command(name, description, null, handler, parameters);

    public MinamoModuleBuilder Command<TResult>(
        string name,
        string? description,
        string? capability,
        Func<MinamoCommandContext, TResult> handler,
        params MinamoCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => MinamoCommandConvert.FromObject<TResult>(handler(context)),
            parameters);
    }

    public MinamoModuleBuilder AsyncCommand<TResult>(
        string name,
        Func<MinamoCommandContext, ValueTask<TResult>> handler,
        params MinamoCommandParameter[] parameters) =>
        AsyncCommand(name, null, null, handler, parameters);

    public MinamoModuleBuilder AsyncCommand<TResult>(
        string name,
        string? description,
        string? capability,
        Func<MinamoCommandContext, ValueTask<TResult>> handler,
        params MinamoCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => MinamoCommandConvert.FromAwaitable(handler(context)),
            parameters);
    }

    public MinamoModuleBuilder AsyncCommand(
        string name,
        Func<MinamoCommandContext, ValueTask> handler,
        params MinamoCommandParameter[] parameters) =>
        AsyncCommand(name, null, null, handler, parameters);

    public MinamoModuleBuilder AsyncCommand(
        string name,
        string? description,
        string? capability,
        Func<MinamoCommandContext, ValueTask> handler,
        params MinamoCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => MinamoCommandConvert.FromAwaitable(handler(context)),
            parameters);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public MinamoModuleBuilder RawCommand(
        string name,
        string? description,
        string? capability,
        MinamoCommandHandler handler,
        params MinamoCommandParameter[] parameters)
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

        var descriptor = new MinamoCommandDescriptor(
            name, description, capability, MinamoCommandParameter.Snapshot(parameters), handler);
        commands.Add(name, descriptor);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public MinamoModuleBuilder RawProperty(
        string name,
        string? description,
        string? capability,
        MinamoCommandHandler getter,
        MinamoCommandHandler? setter = null,
        MinamoCommandParameter? valueParameter = null)
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
            Array.Empty<MinamoCommandParameter>(),
            getter,
            propertyGetter: true));
        if (setter is not null)
        {
            commands.Add(setterName, new MinamoCommandDescriptor(
                setterName,
                description,
                capability,
                new[] { valueParameter! },
                setter,
                propertySetter: true));
        }
        return this;
    }

    public MinamoModuleBuilder Type(string name, Action<MinamoTypeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(Type(name));
        return this;
    }

    public MinamoTypeBuilder Type(string name)
    {
        EnsureGeneratedModule();
        HostNames.ValidateIdentifier(name, nameof(name), "host type");

        if (types.ContainsKey(name))
        {
            throw new InvalidOperationException($"Host type '{Name}.{name}' is already registered.");
        }

        var type = new MinamoTypeBuilder(name);
        types.Add(name, type);
        return type;
    }

    public MinamoModuleBuilder ForeignType(Func<MinamoForeignTypeInfo> factory)
    {
        EnsureGeneratedModule();
        ArgumentNullException.ThrowIfNull(factory);
        foreignTypes.Add(factory);
        return this;
    }

    public MinamoModuleBuilder Unit(Func<ForeignUnit> factory)
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

    public MinamoModuleBuilder Command(
        string name,
        Action<MinamoCommandContext> handler,
        params MinamoCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Command(name, null, (Func<MinamoCommandContext, object?>)(context =>
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
    IReadOnlyList<MinamoCommandDescriptor> Commands,
    IReadOnlyList<HostTypeDefinition> Types,
    IReadOnlyList<Func<MinamoForeignTypeInfo>> ForeignTypes,
    Func<ForeignUnit>? UnitFactory);

public sealed class MinamoTypeBuilder
{
    private readonly Dictionary<string, MinamoCommandDescriptor> commands =
        new(StringComparer.OrdinalIgnoreCase);

    internal MinamoTypeBuilder(string name)
    {
        HostNames.ValidateIdentifier(name, nameof(name), "host type");

        Name = name;
    }

    public string Name { get; }

    public IReadOnlyCollection<MinamoCommandDescriptor> Commands => commands.Values;

    public MinamoTypeBuilder Command(
        string name,
        Func<MinamoCommandContext, object?> handler,
        params MinamoCommandParameter[] parameters) =>
        Command(name, null, null, handler, parameters);

    public MinamoTypeBuilder Command(
        string name,
        string? description,
        Func<MinamoCommandContext, object?> handler,
        params MinamoCommandParameter[] parameters) =>
        Command(name, description, null, handler, parameters);

    public MinamoTypeBuilder Command(
        string name,
        string? description,
        string? capability,
        Func<MinamoCommandContext, object?> handler,
        params MinamoCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => MinamoCommandConvert.FromObject<object?>(handler(context)),
            parameters);
    }

    public MinamoTypeBuilder Command<TResult>(
        string name,
        Func<MinamoCommandContext, TResult> handler,
        params MinamoCommandParameter[] parameters) =>
        Command(name, null, null, handler, parameters);

    public MinamoTypeBuilder Command<TResult>(
        string name,
        string? description,
        Func<MinamoCommandContext, TResult> handler,
        params MinamoCommandParameter[] parameters) =>
        Command(name, description, null, handler, parameters);

    public MinamoTypeBuilder Command<TResult>(
        string name,
        string? description,
        string? capability,
        Func<MinamoCommandContext, TResult> handler,
        params MinamoCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => MinamoCommandConvert.FromObject<TResult>(handler(context)),
            parameters);
    }

    public MinamoTypeBuilder AsyncCommand<TResult>(
        string name,
        Func<MinamoCommandContext, ValueTask<TResult>> handler,
        params MinamoCommandParameter[] parameters) =>
        AsyncCommand(name, null, null, handler, parameters);

    public MinamoTypeBuilder AsyncCommand<TResult>(
        string name,
        string? description,
        string? capability,
        Func<MinamoCommandContext, ValueTask<TResult>> handler,
        params MinamoCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => MinamoCommandConvert.FromAwaitable(handler(context)),
            parameters);
    }

    public MinamoTypeBuilder AsyncCommand(
        string name,
        Func<MinamoCommandContext, ValueTask> handler,
        params MinamoCommandParameter[] parameters) =>
        AsyncCommand(name, null, null, handler, parameters);

    public MinamoTypeBuilder AsyncCommand(
        string name,
        string? description,
        string? capability,
        Func<MinamoCommandContext, ValueTask> handler,
        params MinamoCommandParameter[] parameters)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RawCommand(
            name,
            description,
            capability,
            context => MinamoCommandConvert.FromAwaitable(handler(context)),
            parameters);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public MinamoTypeBuilder RawCommand(
        string name,
        string? description,
        string? capability,
        MinamoCommandHandler handler,
        params MinamoCommandParameter[] parameters)
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
            name, description, capability, MinamoCommandParameter.Snapshot(parameters), handler));
        return this;
    }

    internal HostTypeDefinition Build() => new(Name, commands.Values.ToArray());
}

internal sealed record HostTypeDefinition(
    string Name,
    IReadOnlyList<MinamoCommandDescriptor> Commands);

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MinamoModuleAttribute : Attribute
{
    public MinamoModuleAttribute(string name) => Name = name;

    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MinamoResourceAttribute : Attribute
{
    public MinamoResourceAttribute(string name) => Name = name;

    public string Name { get; }

    public MinamoResourceLifetime Lifetime { get; set; } = MinamoResourceLifetime.Shared;
}

public enum MinamoResourceLifetime
{
    Shared,
    Transient
}

public abstract class MinamoResource
{
    protected virtual void OnRelease() { }

    internal void Release() => OnRelease();
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MinamoCommandAttribute : Attribute
{
    public MinamoCommandAttribute() { }

    public MinamoCommandAttribute(string name) => Name = name;

    public string? Name { get; }

    public string? Description { get; set; }

    public string? Capability { get; set; }

    public string? Type { get; set; }
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MinamoPropertyAttribute : Attribute
{
    public MinamoPropertyAttribute() { }

    public MinamoPropertyAttribute(string name) => Name = name;

    public string? Name { get; }

    public string? Description { get; set; }

    public string? Capability { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MinamoForeignTypeAttribute : Attribute
{
    public MinamoForeignTypeAttribute(Type type) => Type = type;

    public Type Type { get; }
}
