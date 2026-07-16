using Spellkit.Compiler;
using Spellkit.Linker;
using Spellkit.Parser;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

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

    internal SpellkitInstance CreateInstance(object? hostContext, SpkTuple? arguments) =>
        CreateInstance(new SpellkitEnvironment(hostContext), null, arguments);

    internal SpellkitInstance CreateInstance(SpellkitEnvironment environment, SpkTuple? arguments) =>
        CreateInstance(environment, null, arguments);

    private SpellkitInstance CreateInstance(
        SpellkitEnvironment environment,
        SpellkitProgram? program,
        SpkTuple? arguments = null)
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
        var linker = new SpkLinker(compileLookup);
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
