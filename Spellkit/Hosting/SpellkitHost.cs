using Spellkit.Compiler;
using Spellkit.Linker;
using Spellkit.Parser;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

public enum SpellkitCapabilityMode
{
    Automatic,
    Restricted,
    Unrestricted
}

public sealed class SpellkitHostOptions
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public BuilderOptions? BuilderOptions { get; init; }
    public SpellkitCapabilityMode CapabilityMode { get; init; }
    public SpellkitExecutionLimits Limits { get; init; } = new();
    public SpellkitSignalOptions Signals { get; init; } = new();
    public Action<SpellkitLogEntry>? Log { get; init; }
    public Action<SpellkitTraceEvent>? Trace { get; init; }
    public bool ExposeHostObject { get; init; } = true;
}

public sealed class SpellkitHost
{
    private readonly object programOwner = new();
    private readonly Dictionary<string, SpellkitModuleBuilder> modules =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> capabilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, HostResourceDefinition> resourceTypes = new();
    private readonly Dictionary<string, HostSignalDefinition> signals =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly BuilderOptions options;
    private FileLookup? lookup;
    private bool fileImportsDisabled;
    private readonly IReadOnlyList<Action<SpellkitLogEntry>> logHandlers;
    private readonly IReadOnlyList<Action<SpellkitTraceEvent>> traceHandlers;
    private readonly SpellkitExecutionLimits limits;
    private readonly int? maxPendingSignals;
    private readonly SpellkitCapabilityMode capabilityMode;
    private readonly bool exposeHostObject;

    public SpellkitHost(SpellkitHostOptions? options = null)
    {
        options ??= new();
        this.options = options.BuilderOptions ?? BuilderOptions.Default();
        capabilityMode = options.CapabilityMode;
        if (!Enum.IsDefined(capabilityMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), capabilityMode, "Unknown capability mode.");
        }

        limits = options.Limits
            ?? throw new ArgumentNullException(nameof(options), "Limits cannot be null.");
        limits.Validate();
        var signalOptions = options.Signals
            ?? throw new ArgumentNullException(nameof(options), "Signals cannot be null.");
        signalOptions.Validate();
        maxPendingSignals = signalOptions.MaxPending;
        logHandlers = Handlers(options.Log);
        traceHandlers = Handlers(options.Trace);
        exposeHostObject = options.ExposeHostObject;
    }

    public IReadOnlyCollection<SpellkitModuleBuilder> Modules => modules.Values;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public SpellkitHost UseFileLookup(FileLookup fileLookup)
    {
        lookup = fileLookup ?? throw new ArgumentNullException(nameof(fileLookup));
        fileImportsDisabled = false;
        return this;
    }

    public SpellkitHost DisableFileImports()
    {
        lookup = null;
        fileImportsDisabled = true;
        return this;
    }

    public SpellkitHost AddCapabilities(params string[] capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        foreach (var capability in capabilities)
        {
            HostNames.ValidateCapability(capability, nameof(capabilities));
            this.capabilities.Add(capability);
        }
        return this;
    }

    public SpellkitHost AddResourceType<T>() where T : SpellkitResource
    {
        if (resourceTypes.ContainsKey(typeof(T)))
        {
            throw new InvalidOperationException(
                $"Resource type '{typeof(T).FullName}' is already registered.");
        }

        var definition = SpellkitResourceDefinition.Create<T>();
        if (resourceTypes.Values.Any(
            registered => string.Equals(
                registered.TypeName,
                definition.TypeName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Resource name '{definition.TypeName}' is already registered.");
        }

        resourceTypes.Add(typeof(T), definition);
        return this;
    }

    public SpellkitHost AddSignal(
        string name,
        string? listenCapability = null,
        string? emitCapability = null)
    {
        HostNames.ValidateDottedName(name, nameof(name), "signal");
        HostNames.ValidateCapability(listenCapability, nameof(listenCapability), optional: true);
        HostNames.ValidateCapability(emitCapability, nameof(emitCapability), optional: true);
        if (!signals.TryAdd(name, new(name, listenCapability, emitCapability)))
        {
            throw new InvalidOperationException($"Host signal '{name}' is already registered.");
        }

        return this;
    }

    public SpellkitHost Module(string name, Action<SpellkitModuleBuilder> configure)
    {
        HostNames.ValidateDottedName(name, nameof(name), "module");
        ArgumentNullException.ThrowIfNull(configure);

        if (modules.ContainsKey(name))
        {
            throw new InvalidOperationException($"Host module '{name}' is already registered.");
        }

        var module = new SpellkitModuleBuilder(name);
        configure(module);
        modules.Add(name, module);
        return this;
    }

    public SpellkitInstance CreateInstance(object? hostContext = null) =>
        CreateInstance(new SpellkitEnvironment(hostContext));

    public SpellkitInstance CreateInstance(SpellkitEnvironment environment) =>
        CreateInstance(environment, null);

    public SpellkitInstance CreateInstance(SpellkitProgram program, SpellkitEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (!ReferenceEquals(program.Owner, programOwner))
        {
            throw new InvalidOperationException(
                "A compiled Spellkit program can only be used with the host that compiled it.");
        }

        return CreateInstance(environment ?? new SpellkitEnvironment(), program);
    }

    internal SpellkitInstance CreateInstance(object? hostContext, SpellkitTuple? arguments) =>
        CreateInstance(new SpellkitEnvironment(hostContext), null, arguments);

    internal SpellkitInstance CreateInstance(SpellkitEnvironment environment, SpellkitTuple? arguments) =>
        CreateInstance(environment, null, arguments);

    private SpellkitInstance CreateInstance(
        SpellkitEnvironment environment,
        SpellkitProgram? program,
        SpellkitTuple? arguments = null)
    {
        var definitions = modules.Values.Select(m => m.Build()).ToArray();
        var instanceOptions = CloneOptions(options);
        instanceOptions.AllowEnvironmentNames = true;
        instanceOptions.ExposeHostObject = exposeHostObject;
        instanceOptions.ModuleProvider = new HostModuleProvider(definitions);
        var instanceLookup = fileImportsDisabled || lookup is null
            ? FileLookup.Restricted(instanceOptions).Build()
            : lookup.WithOptions(instanceOptions);
        var hostEnvironment = new SpellkitHostEnvironment(
            environment.HostContext,
            definitions,
            resourceTypes.Values,
            signals.Values,
            capabilities,
            unrestricted: capabilityMode switch
            {
                SpellkitCapabilityMode.Automatic => capabilities.Count == 0,
                SpellkitCapabilityMode.Restricted => false,
                SpellkitCapabilityMode.Unrestricted => true,
                _ => throw new InvalidOperationException("Unknown capability mode.")
            },
            logHandlers.ToArray(),
            traceHandlers.ToArray(),
            limits,
            maxPendingSignals);
        return new SpellkitInstance(instanceLookup, hostEnvironment, environment, program, arguments);
    }

    public Result<SpellkitProgram> Compile(string source, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Compile(SourceBuffer.FromString(source, sourceName));
    }

    public Result<SpellkitProgram> CompileFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var path = Path.GetFullPath(fileName);
        return Compile(SourceBuffer.FromString(File.ReadAllText(path), path));
    }

    public Result<SpellkitProgram> Compile(SourceBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var definitions = modules.Values.Select(m => m.Build()).ToArray();
        var compileOptions = CloneOptions(options);
        compileOptions.AllowEnvironmentNames = true;
        compileOptions.ExposeHostObject = exposeHostObject;
        compileOptions.ModuleProvider = new HostModuleProvider(definitions);
        var compileLookup = fileImportsDisabled || lookup is null
            ? FileLookup.Restricted(compileOptions).Build()
            : lookup.WithOptions(compileOptions);
        var linker = new SpellkitLinker(compileLookup);
        var result = linker.Make(source);
        return result.Success && result.Value is not null
            ? Result.Create(new SpellkitProgram(result.Value, result.Messages, programOwner), result.Messages)
            : Result.Create<SpellkitProgram>(null, result.Messages);
    }

    internal void ConfigureModules(BuilderOptions target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.ModuleProvider = new HostModuleProvider(modules.Values.Select(m => m.Build()));
    }

    private static IReadOnlyList<Action<T>> Handlers<T>(Action<T>? handlers) =>
        handlers is null
            ? Array.Empty<Action<T>>()
            : handlers.GetInvocationList().Cast<Action<T>>().ToArray();

    private static BuilderOptions CloneOptions(BuilderOptions source)
    {
        var clone = new BuilderOptions
        {
            Debug = source.Debug,
            NoLangModule = source.NoLangModule,
            NoWarnings = source.NoWarnings,
            NoWarningsLinker = source.NoWarningsLinker,
            NoOptimizations = source.NoOptimizations,
            LinkerSkipChecksum = source.LinkerSkipChecksum,
            LinkerLog = source.LinkerLog,
            AllowEnvironmentNames = source.AllowEnvironmentNames,
            ExposeHostObject = source.ExposeHostObject
        };

        foreach (var warning in source.IgnoreWarnings)
        {
            clone.IgnoreWarnings.Add(warning);
        }

        return clone;
    }
}

internal static class HostNames
{
    public static void ValidateIdentifier(string name, string parameterName, string kind)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"A host {kind} requires a name.", parameterName);
        }

        if (!IsIdentifier(name))
        {
            throw new ArgumentException(
                $"Host {kind} name '{name}' is not a valid Spellkit identifier.",
                parameterName);
        }
    }

    public static void ValidateDottedName(string name, string parameterName, string kind)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"A host {kind} requires a name.", parameterName);
        }

        foreach (var segment in name.Split('.'))
        {
            if (!IsIdentifier(segment))
            {
                throw new ArgumentException(
                    $"Host {kind} name '{name}' must contain only Spellkit identifier segments.",
                    parameterName);
            }
        }
    }

    public static void ValidateCapability(
        string? capability,
        string parameterName,
        bool optional = false)
    {
        if (capability is null)
        {
            if (optional)
            {
                return;
            }

            throw new ArgumentException("Capability names cannot be null.", parameterName);
        }

        if (string.IsNullOrWhiteSpace(capability))
        {
            if (optional)
            {
                return;
            }

            throw new ArgumentException("Capability names cannot be empty.", parameterName);
        }

        if (capability == "*")
        {
            return;
        }

        var name = capability.EndsWith(".*", StringComparison.Ordinal)
            ? capability[..^2]
            : capability;
        ValidateDottedName(name, parameterName, "capability");
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !IsIdentifierStart(value[0]))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char value) =>
        value == '_' || char.IsLetter(value);

    private static bool IsIdentifierPart(char value) =>
        value == '_' || char.IsLetterOrDigit(value);
}

internal static class SpellkitResourceDefinition
{
    internal static HostResourceDefinition Create<T>() where T : SpellkitResource
    {
        var type = typeof(T);
        if (type.IsAbstract)
        {
            throw new InvalidOperationException(
                $"Resource type '{type.FullName}' must be concrete.");
        }

        var resource = type.GetCustomAttribute<SpellkitResourceAttribute>(inherit: false)
            ?? throw new InvalidOperationException(
                $"Resource type '{type.FullName}' requires SpellkitResourceAttribute.");
        HostNames.ValidateDottedName(resource.Name, nameof(resource.Name), "resource type");

        var methods = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => (Method: method, Command:
                method.GetCustomAttribute<SpellkitCommandAttribute>(inherit: false)))
            .Where(entry => entry.Command is not null)
            .Select(entry => CreateCommand(entry.Method, entry.Command!))
            .ToArray();

        var duplicate = methods
            .GroupBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Resource command '{duplicate.Key}' is registered more than once on '{type.FullName}'.");
        }

        var catalog = methods.Select(method => method.CatalogDescriptor()).ToArray();
        return new(
            type,
            resource.Name,
            resource.Lifetime,
            catalog,
            instance => methods.Select(method => method.Bind(instance)).ToArray());
    }

    private static ResourceMethod CreateCommand(
        MethodInfo method,
        SpellkitCommandAttribute command)
    {
        var name = command.Name ?? method.Name;
        HostNames.ValidateIdentifier(name, nameof(command.Name), "resource command");
        HostNames.ValidateCapability(command.Capability, nameof(command.Capability), optional: true);

        if (method.IsGenericMethodDefinition || method.ContainsGenericParameters)
        {
            throw Invalid(method, "generic methods are not supported");
        }

        if (method.ReturnType.IsByRef)
        {
            throw Invalid(method, "by-reference return values are not supported");
        }

        var contextCount = 0;
        var exposed = new List<SpellkitCommandParameter>();
        foreach (var parameter in method.GetParameters())
        {
            if (parameter.ParameterType.IsByRef || parameter.IsOut || parameter.IsIn)
            {
                throw Invalid(method, "ref, in, and out parameters are not supported");
            }

            if (parameter.GetCustomAttribute<ParamArrayAttribute>() is not null)
            {
                throw Invalid(method, "params arrays are not supported");
            }

            if (parameter.ParameterType == typeof(SpellkitCommandContext))
            {
                contextCount++;
                if (contextCount > 1 || parameter.HasDefaultValue)
                {
                    throw Invalid(
                        method,
                        "only one non-optional SpellkitCommandContext parameter is allowed");
                }

                continue;
            }

            var parameterName = parameter.Name
                ?? throw Invalid(method, "all parameters require names");
            exposed.Add(new(
                parameterName,
                parameter.ParameterType,
                parameter.HasDefaultValue,
                parameter.HasDefaultValue ? parameter.DefaultValue : null));
        }

        return new(
            method,
            name,
            command.Description,
            command.Capability,
            exposed);
    }

    private static InvalidOperationException Invalid(MethodInfo method, string reason) =>
        new($"Method '{method.DeclaringType?.FullName}.{method.Name}' cannot be exposed "
            + $"as a resource command: {reason}.");

    private sealed record ResourceMethod(
        MethodInfo Method,
        string Name,
        string? Description,
        string? Capability,
        IReadOnlyList<SpellkitCommandParameter> Parameters)
    {
        internal SpellkitCommandDescriptor CatalogDescriptor() =>
            new(
                Name,
                Description,
                Capability,
                Parameters,
                (SpellkitCommandHandler)(_ => SpellkitNil.Instance));

        internal SpellkitCommandDescriptor Bind(SpellkitResource resource) =>
            new(
                Name,
                Description,
                Capability,
                Parameters,
                context => Invoke(resource, context));

        private SpellkitObject Invoke(SpellkitResource resource, SpellkitCommandContext context)
        {
            var parameters = Method.GetParameters();
            var arguments = new object?[parameters.Length];
            var argumentIndex = 0;
            for (var i = 0; i < parameters.Length; i++)
            {
                arguments[i] = parameters[i].ParameterType == typeof(SpellkitCommandContext)
                    ? context
                    : context.Argument(argumentIndex++, parameters[i].ParameterType);
            }

            if (context.ExecutionContext.HasErrors)
            {
                return SpellkitNil.Instance;
            }

            object? result;
            try
            {
                result = Method.Invoke(resource, arguments);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException is { } inner)
            {
                ExceptionDispatchInfo.Capture(inner).Throw();
                throw;
            }

            if (Method.ReturnType == typeof(void))
            {
                return SpellkitNil.Instance;
            }
            if (result is Task task)
            {
                return Method.ReturnType.IsGenericType
                    ? SpellkitCommandConvert.FromAwaitable(
                        task,
                        Method.ReturnType.GetGenericArguments()[0])
                    : SpellkitCommandConvert.FromAwaitable(task);
            }
            if (Method.ReturnType == typeof(ValueTask))
            {
                return SpellkitCommandConvert.FromAwaitable((ValueTask)result!);
            }
            if (Method.ReturnType.IsGenericType
                && Method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                var asTask = Method.ReturnType.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(result, null)!;
                return SpellkitCommandConvert.FromAwaitable(
                    (Task)asTask,
                    Method.ReturnType.GetGenericArguments()[0]);
            }

            return SpellkitCommandConvert.FromObject(result, Method.ReturnType);
        }
    }
}
