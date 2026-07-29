using Spellkit.Compiler;
using Spellkit.Hosting;
using Spellkit.Linker;
using Spellkit.Runtime;
using System.Collections.Concurrent;
using System.Text;

namespace Spellkit.UnitTesting;

internal static class HostingScenarios
{
    internal static void HostConfigurationValidation()
    {
        AssertThrows<ArgumentException>(
            () => new SpellkitHost().Module("bad-name", _ => { }),
            "invalid module name");
        AssertThrows<ArgumentException>(
            () => new SpellkitHost().AddCapabilities("scene..read"),
            "invalid capability name");
        AssertThrows<ArgumentOutOfRangeException>(
            () => new SpellkitHost(new()
            {
                CapabilityMode = (SpellkitCapabilityMode)int.MaxValue
            }),
            "invalid capability mode");
        AssertThrows<ArgumentException>(
            () => new SpellkitHost().AddSignal("player-hit"),
            "invalid signal name");
        AssertThrows<InvalidOperationException>(
            () => new SpellkitHost().AddResourceType<UnattributedResource>(),
            "resource attribute is required");
        AssertThrows<InvalidOperationException>(
            () => new SpellkitHost().AddResourceType<DuplicateCommandResource>(),
            "duplicate attributed resource commands");

        AssertThrows<ArgumentException>(
            () => new SpellkitHost().Module("game", module => module.Command("bad-name", _ => null)),
            "invalid command name");
        AssertThrows<ArgumentException>(
            () => new SpellkitHost().Module("game", module => module.Type("bad-type")),
            "invalid host type name");
        AssertThrows<ArgumentException>(
            () => new SpellkitHost().Module("game", module => module.Command(
                "Move",
                _ => null,
                SpellkitCommandParameter.Required<int>("bad-name"))),
            "invalid command parameter name");

        AssertThrows<InvalidOperationException>(
            () => new SpellkitHost().Module(
                "custom",
                module => module.Unit(() => new EmptyUnit()).Command("Ignored", _ => null)),
            "custom unit then generated registration");
        AssertThrows<InvalidOperationException>(
            () => new SpellkitHost().Module(
                "custom",
                module => module.Command("Ignored", _ => null).Unit(() => new EmptyUnit())),
            "generated registration then custom unit");

        var parameters = new[] { SpellkitCommandParameter.Required<int>("value") };
        using var session = new SpellkitHost()
            .Module("safe", module => module.Command(
                "Echo",
                context => context.Argument<int>("value"),
                parameters))
            .CreateInstance();
        parameters[0] = SpellkitCommandParameter.Required<string>("changed");
        Success(session, "import safe\nassert(3, safe.Echo(3))");

        using var numericSession = new SpellkitHost()
            .Module("numeric", module => module.Command(
                "Int32",
                context => context.Argument<int>("value"),
                SpellkitCommandParameter.Required<int>("value")))
            .CreateInstance();
        var overflow = FailureResult(
            numericSession,
            "import numeric\nnumeric.Int32(4294967296)");
        Assert(overflow.Failure?.Kind == SpellkitFailureKind.Runtime,
            "host numeric overflow failure");

        using var interopSession = new SpellkitHost()
            .Module("interopcache", module =>
            {
                module.Command<FirstInteropValue>("First", _ => new FirstInteropValue());
                module.Command<SecondInteropValue>("Second", _ => new SecondInteropValue());
            })
            .CreateInstance();
        Success(interopSession, """
            import interopcache
            assert("first", interopcache.First().Name())
            assert("second", interopcache.Second().Name())
            """);

        using var boundedInteropSession = new SpellkitHost()
            .Module("interopboundary", module =>
            {
                module.Command<IVisibleInteropValue>(
                    "Declared",
                    _ => new VisibleInteropValue());
            })
            .CreateInstance();
        Success(boundedInteropSession, """
            import interopboundary
            assert("visible", interopboundary.Declared().Name())
            assert("visible", interopboundary.Declared().Child().Name())
            assert("visible", interopboundary.Declared().Children()[0].Name())
            assert("visible", interopboundary.Declared().ChildrenByName()["first"].Name())
            """);
        FailureResult(
            boundedInteropSession,
            "import interopboundary\ninteropboundary.Declared().Secret()");
        FailureResult(
            boundedInteropSession,
            "import interopboundary\ninteropboundary.Declared().Child().Secret()");
        FailureResult(
            boundedInteropSession,
            "import interopboundary\ninteropboundary.Declared().Children()[0].Secret()");
        FailureResult(
            boundedInteropSession,
            "import interopboundary\ninteropboundary.Declared().ChildrenByName()[\"first\"].Secret()");
        FailureResult(
            interopSession,
            "import interopcache\ninteropcache.First().StaticSecret()");
        FailureResult(
            interopSession,
            "import interopcache\ninteropcache.First().new()");
    }

    internal static void PublicApiBoundary()
    {
        var assembly = typeof(SpellkitHost).Assembly;
        var unexpectedPublicTypes = assembly.GetExportedTypes()
            .Where(type => !IsAllowedPublicType(type))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert(
            unexpectedPublicTypes.Length == 0,
            "unexpected public API: " + string.Join(", ", unexpectedPublicTypes));

        foreach (var name in new[]
        {
            "Spellkit.CultureInfoSettings",
            "Spellkit.FileProbe",
            "Spellkit.Runtime.SpellkitMachine",
            "Spellkit.Runtime.EvalStack",
            "Spellkit.Runtime.ExecutionResult",
            "Spellkit.Runtime.TerminationReason",
            "Spellkit.Compiler.SpellkitCompilerEngine",
            "Spellkit.Linker.SpellkitIncrementalLinker",
            "Spellkit.Debug.SpellkitDebugger"
        })
        {
            var type = assembly.GetType(name, throwOnError: true)!;
            Assert(!type.IsPublic, $"internal API boundary for {name}");
        }

        var runtimeContext = typeof(SpellkitInstance).GetProperty("RuntimeContext");
        Assert(runtimeContext is null, "session runtime context is not public");

        AssertPublicApiLayer(
            assembly,
            "application",
            type => type.Namespace is "Spellkit.Hosting",
            typeof(SpellkitHost));
        AssertPublicApiLayer(
            assembly,
            "tooling",
            IsToolingApi,
            typeof(Spellkit.Parser.SpellkitParser),
            typeof(Spellkit.Compiler.Op),
            typeof(Spellkit.Compiler.Unit),
            typeof(Spellkit.Parser.Model.SyntaxNode),
            typeof(Spellkit.Debug.DebugInfo));
        AssertPublicApiLayer(
            assembly,
            "runtime extension",
            IsRuntimeExtensionApi,
            typeof(Spellkit.Runtime.RuntimeContext),
            typeof(Spellkit.Runtime.Types.SpellkitObject));
    }

    internal static void PublicApiNames()
    {
        AssertHasMethod<SpellkitHost>("AddCapabilities", "host capability setup");
        AssertNoMethod<SpellkitHost>("Allow", "old host capability setup name");
        AssertNoMethod<SpellkitHost>("WithLimits", "limits moved to host options");
        AssertHasMethod<SpellkitHost>("UseFileLookup", "file import lookup setup");
        AssertHasMethod<SpellkitHost>("AddResourceType", "reusable resource type registration");
        AssertNoMethod<SpellkitHost>("ResourceType", "old resource type registration");
        AssertHasMethod<SpellkitHost>("DisableFileImports", "explicit file import restriction");
        AssertNoMethod<SpellkitHost>("OnLog", "logging moved to host options");
        AssertNoMethod<SpellkitHost>("OnProgress", "removed progress registration");
        AssertNoMethod<SpellkitHost>("OnTrace", "tracing moved to host options");
        AssertHasProperty<SpellkitHostOptions>("Limits", "host execution limits");
        AssertHasProperty<SpellkitHostOptions>("Signals", "host signal queue options");
        AssertHasProperty<SpellkitHostOptions>("CapabilityMode", "host capability mode");
        AssertHasProperty<SpellkitHostOptions>("Log", "host log handler");
        AssertNoProperty<SpellkitHostOptions>("Progress", "removed host progress handler");
        AssertHasProperty<SpellkitHostOptions>("Trace", "host trace handler");
        AssertHasProperty<SpellkitHostOptions>("ExposeHostObject", "host object visibility");
        AssertNoMethod<SpellkitCommandContext>("ReportProgress", "removed command progress reporting");
        AssertNoMethod<SpellkitTelemetry>("Report", "removed telemetry progress reporting");
        Assert(
            typeof(SpellkitHost).Assembly.GetType("Spellkit.Hosting.SpellkitProgressUpdate") is null,
            "removed progress payload");
        foreach (var name in new[]
        {
            "Spellkit.Hosting.ISpellkitLogHandler",
            "Spellkit.Hosting.ISpellkitProgressHandler",
            "Spellkit.Hosting.ISpellkitTraceHandler"
        })
        {
            Assert(
                typeof(SpellkitHost).Assembly.GetType(name) is null,
                $"removed handler interface {name}");
        }
        AssertHasMethod<SpellkitHost>("AddSignal", "signal registration");
        AssertNoMethod<SpellkitHost>("Signal", "old signal registration name");
        AssertNoMethod<SpellkitHost>("ApplyTo", "removed low-level module injection");
        AssertNoMethod<SpellkitHost>("LogTo", "old log registration name");
        AssertNoMethod<SpellkitHost>("ProgressTo", "old progress registration name");
        AssertNoMethod<SpellkitHost>("TraceTo", "old trace registration name");

        AssertHasMethod<SpellkitHost>("CreateInstance", "instance creation");
        AssertNoMethod<SpellkitHost>("CreateSession", "removed session creation");
        AssertHasMethod<SpellkitHost>("Compile", "program compilation");
        AssertHasMethod<SpellkitHost>("CompileFile", "program file compilation");
        AssertHasMethod<SpellkitInstance>("Execute", "instance execution");
        AssertHasMethod<SpellkitInstance>("ExecuteAsync", "asynchronous instance execution");
        AssertHasMethod<SpellkitInstance>("ExecuteFile", "explicit script file execution");
        AssertHasMethod<SpellkitInstance>("ExecuteFileAsync", "asynchronous file execution");
        AssertHasMethod<SpellkitInstance>("DispatchSignals", "pending signal dispatch");
        AssertHasMethod<SpellkitInstance>("DispatchSignalsAsync", "asynchronous signal dispatch");
        AssertHasMethod<SpellkitExecutionResult>("GetValue", "typed execution result");
        AssertHasMethod<SpellkitExecutionResult>("TryGetValue", "optional typed execution result");
        AssertNoProperty<SpellkitExecutionResult>("Value", "removed raw execution result");
        AssertHasProperty<SpellkitExecutionResult>("Execution", "execution details");
        AssertHasProperty<SpellkitSignalDispatchResult>("Execution", "signal dispatch execution details");
        AssertHasProperty<SpellkitProgram>("Diagnostics", "compiled program diagnostics");
        AssertHasMethod<SpellkitEnvironment>("Expose", "environment name exposure");
        AssertHasMethod<SpellkitEnvironment>("Set", "environment bindings");
        AssertHasMethod<SpellkitEnvironment>("UseInput", "instance input setup");
        AssertHasMethod<SpellkitEnvironment>("UseOutput", "instance output setup");

        AssertHasProperty<SpellkitExecutionLimits>("MaxExecutionTime", "operation time limit");
        AssertNoProperty<SpellkitExecutionLimits>("MaxTime", "old time limit name");

        AssertHasMethod<SpellkitStateStore>("Set", "host-owned state setter");
        AssertHasMethod<SpellkitStateStore>("SetScript", "script-owned state setter");
        AssertHasMethod<SpellkitStateStore>("TryGet", "typed state lookup");
        AssertHasMethod<SpellkitStateStore>("GetOwner", "state ownership inspection");
        AssertNoMethod<SpellkitStateStore>("GetRaw", "internal raw state getter");
        AssertNoMethod<SpellkitStateStore>("SetRaw", "internal raw host state setter");
        AssertNoMethod<SpellkitStateStore>("SetScriptRaw", "internal raw script state setter");
        AssertNoMethod<SpellkitSignalDispatcher>("EmitRaw", "internal raw signal emission");
        AssertHasMethod<SpellkitSignalDispatcher>("TryEmit", "bounded signal emission");
        AssertHasProperty<SpellkitSignalDispatcher>("MaxPending", "pending signal limit");
        AssertHasProperty<SpellkitSignalDispatcher>("PendingCount", "pending signal count");
        AssertHasMethod<SpellkitSignal>("GetPayload", "typed signal payload");
        AssertHasMethod<SpellkitSignal>("TryGetPayload", "optional typed signal payload");
        AssertNoProperty<SpellkitSignal>("Payload", "removed raw signal payload");
        AssertNoProperty<SpellkitHostEnvironment>("Resources", "internal resource registry");
        Assert(
            typeof(SpellkitHost).Assembly.GetType(
                "Spellkit.Hosting.SpellkitResourceRegistry") is { IsPublic: false },
            "resource registry is internal");

        AssertHasMethod<SpellkitCommandContext>("Resource", "host resource creation");
        AssertHasMethod<SpellkitCommandContext>("Callback", "host callback argument");
        AssertHasMethod<SpellkitCommandContext>("CallbackTuple", "host tuple callback argument");
        AssertHasMethod<SpellkitCommandContext>("CallbackAction", "host callback action argument");
        AssertNoMethod<SpellkitCallback>("InvokeRaw", "removed raw callback invocation");
        AssertNoMethod<SpellkitHost>("Service", "removed service registration");
        AssertHasMethod<SpellkitCommandParameter>("Required", "required command parameter factory");
        AssertHasMethod<SpellkitCommandParameter>("Optional", "optional command parameter factory");
        AssertHasMethod<SpellkitModuleBuilder>("AsyncCommand", "asynchronous module command");
        AssertHasMethod<SpellkitTypeBuilder>("AsyncCommand", "asynchronous type command");
        AssertNoMethod<SpellkitHost>("Value", "removed bound value registration");
        AssertNoMethod<SpellkitModuleBuilder>(
            "RuntimeInteropCommand",
            "removed runtime interop command");
        AssertNoMethod<SpellkitTypeBuilder>(
            "RuntimeInteropCommand",
            "removed runtime interop type command");
        AssertEditorBrowsableNever<SpellkitModuleBuilder>("RawCommand");
        AssertEditorBrowsableNever<SpellkitModuleBuilder>("RawProperty");
        AssertEditorBrowsableNever<SpellkitTypeBuilder>("RawCommand");
    }

    internal static void HostCommandCallbacks()
    {
        Func<long, long>? escapedCallback = null;
        using var session = new SpellkitHost()
            .Module("callbacks", module =>
            {
                module.Command("Apply", context =>
                {
                    var callback = context.Callback<long, long>("callback");
                    return callback(context.Argument<long>("value"));
                },
                SpellkitCommandParameter.Required<long>("value"),
                SpellkitCommandParameter.Required<object>("callback"));

                module.Command("Combine", context =>
                {
                    var callback = context.Callback<string, string, string>("callback");
                    return callback(
                        context.Argument<string>("first"),
                        context.Argument<string>("second"));
                },
                SpellkitCommandParameter.Required<string>("first"),
                SpellkitCommandParameter.Required<string>("second"),
                SpellkitCommandParameter.Required<object>("callback"));

                module.Command("SumTuple", context =>
                {
                    var callback = context.CallbackTuple<(long First, long Second, long Third, long Fourth), long>(
                        "callback");
                    return callback((
                        context.Argument<long>("first"),
                        context.Argument<long>("second"),
                        context.Argument<long>("third"),
                        context.Argument<long>("fourth")));
                },
                SpellkitCommandParameter.Required<long>("first"),
                SpellkitCommandParameter.Required<long>("second"),
                SpellkitCommandParameter.Required<long>("third"),
                SpellkitCommandParameter.Required<long>("fourth"),
                SpellkitCommandParameter.Required<object>("callback"));

                module.Command("Notify", context =>
                {
                    var callback = context.CallbackAction<string>("callback");
                    callback(context.Argument<string>("value"));
                    return context.Environment.State.Get<string>("seen");
                },
                SpellkitCommandParameter.Required<string>("value"),
                SpellkitCommandParameter.Required<object>("callback"));

                module.Command("Capture", context =>
                {
                    escapedCallback = context.Callback<long, long>("callback");
                    return null;
                },
                SpellkitCommandParameter.Required<object>("callback"));
            })
            .CreateInstance();

        Success(session, """
            import callbacks
            assert(7, callbacks.Apply(5, value => value + 2))
            assert("left:right", callbacks.Combine(
                "left",
                "right",
                (first, second) => fmt("{0}:{1}", first, second)))
            assert(10, callbacks.SumTuple(
                1,
                2,
                3,
                4,
                (first, second, third, fourth) => first + second + third + fourth))
            assert("ready", callbacks.Notify(
                "ready",
                value => { host.State["seen"] = value; nil }))
            callbacks.Capture(value => value + 1)
            """);
        Assert(escapedCallback is not null, "callback is captured by the host command");
        AssertThrows<InvalidOperationException>(
            () => escapedCallback!(5),
            "callback cannot outlive its host command");
        Failure(session, "import callbacks\ncallbacks.Apply(5, 42)");
    }

    internal static void ProgramBackedInstances()
    {
        var host = new SpellkitHost()
            .AddCapabilities("state.*");
        var compiled = host.Compile("""
            let current = if host.State["runs"] is nil { 0 } else { host.State["runs"] }
            host.State["runs"] = current + 1
            host.State["runs"]
            """);

        Assert(compiled.Success && compiled.Value is not null, "program compiles");
        var program = compiled.GetValueOrThrow();

        using var first = host.CreateInstance(program);
        using var second = host.CreateInstance(program);

        var firstRun = first.Execute();
        var secondRun = second.Execute();
        var firstAgain = first.Execute();

        Assert(firstRun.Success, Describe(firstRun));
        Assert(secondRun.Success, Describe(secondRun));
        Assert(firstAgain.Success, Describe(firstAgain));
        AssertEqual(1L, firstRun.GetValue<long>(), "first instance first run");
        AssertEqual(1L, secondRun.GetValue<long>(), "second instance isolated state");
        AssertEqual(2L, firstAgain.GetValue<long>(), "first instance preserves own state");
        Assert(firstRun.Execution.Operation == "ExecuteProgram", "execution details operation");

        var otherHost = new SpellkitHost(new()
        {
            ExposeHostObject = false
        });
        AssertThrows<InvalidOperationException>(
            () => otherHost.CreateInstance(program),
            "compiled program cannot cross its host boundary");
    }

    internal static void InstanceEnvironmentNames()
    {
        var host = new SpellkitHost();
        var compiled = host.Compile("self + world");
        Assert(compiled.Success && compiled.Value is not null, "environment-name program compiles");
        var program = compiled.GetValueOrThrow();

        using var first = host.CreateInstance(
            program,
            new SpellkitEnvironment()
                .Expose("self", 2)
                .Expose("world", 3));
        using var second = host.CreateInstance(
            program,
            new SpellkitEnvironment()
                .Expose("self", 10)
                .Expose("world", 20));

        var firstRun = first.Execute();
        var secondRun = second.Execute();

        Assert(firstRun.Success, Describe(firstRun));
        Assert(secondRun.Success, Describe(secondRun));
        AssertEqual(5L, firstRun.GetValue<long>(), "first environment names");
        AssertEqual(30L, secondRun.GetValue<long>(), "second environment names");

        using var missing = host.CreateInstance(
            program,
            new SpellkitEnvironment()
                .Expose("self", 1));
        var missingRun = missing.Execute();
        Assert(!missingRun.Success, "missing environment name fails");
        Assert(missingRun.Failure?.Kind == SpellkitFailureKind.Runtime,
            "missing environment name fails at runtime");
        Assert(Describe(missingRun).Contains("world", StringComparison.Ordinal),
            "missing environment name identifies name");

        var assignmentEnvironment = new SpellkitEnvironment()
            .Expose("self", 1);
        using var assignment = host.CreateInstance(assignmentEnvironment);
        var assignmentRun = assignment.Execute("""
            self = 2
            self
            """);
        Assert(assignmentRun.Success, Describe(assignmentRun));
        AssertEqual(2L, assignmentRun.GetValue<long>(), "assignment creates script binding");
        Assert(
            assignmentEnvironment.TryGet("self", out var exposedSelf)
            && Equals(1, exposedSelf),
            "environment exposure is not mutated by script assignment");
    }

    internal static void HiddenHostObject()
    {
        var host = new SpellkitHost(new()
        {
            ExposeHostObject = false
        });

        var hidden = host.Compile("host.State[\"value\"]");
        Assert(!hidden.Success, "hidden host object is not compiled");
        Assert(hidden.Errors.Any(error =>
                error.Message.Contains("\"host\"", StringComparison.Ordinal)
                && error.Message.Contains("not declared", StringComparison.OrdinalIgnoreCase)),
            "hidden host object is reported as undeclared");

        var program = host.Compile("self + world");
        Assert(program.Success && program.Value is not null,
            "environment names compile while host object is hidden");

        using var instance = host.CreateInstance(
            program.GetValueOrThrow(),
            new SpellkitEnvironment()
                .Expose("self", 2)
                .Expose("world", 4)
                .Expose("host", 100));

        var result = instance.Execute();
        Assert(result.Success, Describe(result));
        AssertEqual(6L, result.GetValue<long>(), "hidden host object still allows environment names");

        var exposedHostName = host.Compile("host");
        Assert(!exposedHostName.Success,
            "reserved host name does not fall back to environment exposure");
    }

    private static bool IsAllowedPublicType(Type type) =>
        type.Namespace is "Spellkit.Hosting"
        || IsToolingApi(type)
        || IsRuntimeExtensionApi(type);

    private static bool IsToolingApi(Type type) =>
        type.Namespace is "Spellkit.Codegen"
        || type.Namespace is "Spellkit.Compiler"
        || type.Namespace is "Spellkit.Debug"
        || type.Namespace is "Spellkit.Linker"
        || type.Namespace is "Spellkit.Parser"
        || type.Namespace is "Spellkit.Parser.Model";

    private static bool IsRuntimeExtensionApi(Type type) =>
        type.Namespace is "Spellkit"
        || type.Namespace is "Spellkit.Runtime"
        || type.Namespace?.StartsWith("Spellkit.Runtime.Types", StringComparison.Ordinal) == true
        || type.Namespace is "Spellkit.Runtime.Interop";

    private static void AssertPublicApiLayer(
        System.Reflection.Assembly assembly,
        string layer,
        Func<Type, bool> belongsToLayer,
        params Type[] representativeTypes)
    {
        var exportedTypes = assembly.GetExportedTypes();
        foreach (var type in representativeTypes)
        {
            Assert(exportedTypes.Contains(type), $"{layer} API exports {type.FullName}");
            Assert(belongsToLayer(type), $"{type.FullName} belongs to {layer} API");
        }
    }

    internal static void FileImportConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "spellkit-hosting-" + Guid.NewGuid());
        var outside = Path.Combine(Path.GetTempPath(), "spellkit-outside-" + Guid.NewGuid() + ".kit");
        var outsideDirectory = Path.Combine(
            Path.GetTempPath(),
            "spellkit-outside-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideDirectory);
        try
        {
            File.WriteAllText(outside, "func value() => 99", Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(outsideDirectory, "linked.kit"),
                "func value() => 100",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(root, "hostmod.kit"),
                "func value() => 42",
                Encoding.UTF8);

            var options = BuilderOptions.Default();
            var lookup = FileLookup.Restricted(options)
                .AddStartupPath(root)
                .Build();

            Assert(
                !lookup.Find(null, outside, out _),
                "absolute module path is rejected");
            Assert(
                !lookup.Find(null, Path.Combine("..", Path.GetFileName(outside)), out _),
                "parent traversal outside lookup root is rejected");

            var link = Path.Combine(root, "linked");
            try
            {
                Directory.CreateSymbolicLink(link, outsideDirectory);
                Assert(
                    !lookup.Find(null, Path.Combine("linked", "linked.kit"), out _),
                    "symbolic link traversal outside lookup root is rejected");
            }
            catch (Exception ex) when (ex is IOException
                or PlatformNotSupportedException
                or UnauthorizedAccessException)
            {
                // Symbolic-link creation is not available in every test environment.
            }

            using (var allowed = new SpellkitHost(new() { BuilderOptions = options })
                .UseFileLookup(lookup)
                .CreateInstance())
            {
                Success(allowed, "import hostmod\nassert(42, hostmod.value())");
            }

            using (var disabled = new SpellkitHost(new() { BuilderOptions = options })
                .UseFileLookup(lookup)
                .DisableFileImports()
                .CreateInstance())
            {
                Failure(disabled, "import hostmod");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(outside))
            {
                File.Delete(outside);
            }

            if (Directory.Exists(outsideDirectory))
            {
                Directory.Delete(outsideDirectory, recursive: true);
            }
        }
    }

    internal static void FileExecutionAndOperationResults()
    {
        var root = Path.Combine(Path.GetTempPath(), "spellkit-execute-file-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var validPath = Path.Combine(root, "valid.kit");
            var invalidPath = Path.Combine(root, "invalid.kit");
            File.WriteAllText(validPath, "let answer = 42", Encoding.UTF8);
            File.WriteAllText(invalidPath, "let =", Encoding.UTF8);

            using var session = new SpellkitHost()
                .AddSignal("tick")
                .CreateInstance();

            ISpellkitOperationResult execution = session.ExecuteFile(validPath);
            Assert(execution.Success, "file execution succeeds");
            AssertEqual(0, execution.Failures.Count, "successful operation failures");
            Assert(execution.ExecutionId != Guid.Empty, "file execution ID");

            var invalid = session.ExecuteFile(invalidPath);
            Assert(!invalid.Success, "invalid file execution fails");
            AssertEqual(1, invalid.Failures.Count, "failed operation failures");
            Assert(
                invalid.Diagnostics.Any(diagnostic =>
                    diagnostic.File is not null
                    && string.Equals(
                        Path.GetFullPath(diagnostic.File),
                        invalidPath,
                        StringComparison.OrdinalIgnoreCase)),
                "file diagnostics preserve source path");

            session.Environment.Signals.Emit("tick", 1);
            ISpellkitOperationResult dispatch = session.DispatchSignals();
            Assert(dispatch.Success, "signal result uses common operation contract");
            AssertEqual(0, dispatch.Failures.Count, "signal operation failures");

            var missing = session.ExecuteFile(Path.Combine(root, "missing.kit"));
            Assert(missing.Failure is
                { Kind: SpellkitFailureKind.Input, Exception: FileNotFoundException },
                "missing explicit script file result");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    internal static void CapabilityAndCatalog()
    {
        using (var automatic = new SpellkitHost()
            .Module("mode", module => module.Command("Protected", null, "mode.use", _ => 1))
            .CreateInstance())
        {
            Assert(automatic.Environment.Capabilities.IsUnrestricted,
                "automatic mode without allow-list is unrestricted");
            Success(automatic, "import mode\nassert(1, mode.Protected())");
        }

        using (var restricted = new SpellkitHost(new()
        {
            CapabilityMode = SpellkitCapabilityMode.Restricted
        })
            .Module("mode", module => module.Command("Protected", null, "mode.use", _ => 1))
            .CreateInstance())
        {
            Assert(!restricted.Environment.Capabilities.IsUnrestricted,
                "explicit restricted mode");
            Failure(restricted, "import mode\nmode.Protected()");
        }

        using (var unrestricted = new SpellkitHost(new()
        {
            CapabilityMode = SpellkitCapabilityMode.Unrestricted
        })
            .AddCapabilities("other")
            .Module("mode", module => module.Command("Protected", null, "mode.use", _ => 1))
            .CreateInstance())
        {
            Assert(unrestricted.Environment.Capabilities.IsUnrestricted,
                "explicit unrestricted mode");
            Success(unrestricted, "import mode\nassert(1, mode.Protected())");
        }

        var counter = new Counter(5);
        using var session = new SpellkitHost()
            .AddCapabilities("counter.read")
            .Module("math", module =>
            {
                module.Command("Public", _ => 1);
                module.Command("Secret", null, "admin", _ => 2);
            })
            .Module("counter", module =>
            {
                module.Command("Value", null, "counter.read", _ => counter.Value);
                module.Command("Increment", null, "counter.write", _ => ++counter.Value);
            })
            .CreateInstance();

        Assert(session.Environment.Capabilities.Allowed is not ISet<string>,
            "capability allow-list does not expose its mutable set");
        var allowed = (ICollection<string>)session.Environment.Capabilities.Allowed;
        AssertThrows<NotSupportedException>(
            () => allowed.Add("*"),
            "capability allow-list is read-only");
        Assert(!session.Environment.Capabilities.Allows("counter.write"),
            "capability allow-list cannot be mutated");

        Success(session, """
            import counter
            assert(5, counter.Value())
            assert("math.Public", host.Commands.Describe("math.Public").Name)
            assert(nil, host.Commands.Describe("math.Secret"))
            assert(nil, host.Commands.Describe("counter.Increment"))
            """);

        Failure(session, "host.Capabilities");
        Failure(session, "import counter\ncounter.Increment()");
    }

    internal static void ResourceLifetime()
    {
        using var session = new SpellkitHost()
            .AddResourceType<TransientCounterResource>()
            .Module("factory", module => module.Command("Create", context =>
                context.Resource(new TransientCounterResource(
                    context.Argument<int>("value"))),
                SpellkitCommandParameter.Required<int>("value")))
            .CreateInstance();

        Success(session, """
            import factory
            let counter = factory.Create(4)
            assert("Counter", counter.Type)
            assert(4, counter.Value())
            assert(7, counter.Add(3))
            assert(true, counter.IsValid())
            assert(true, counter.Release())
            assert(false, counter.IsValid())
            """);

        Failure(session, """
            import factory
            let counter = factory.Create(1)
            counter.Release()
            counter.Value()
            """);
    }

    internal static void RegisteredResourceTypeAndCatalog()
    {
        var host = new SpellkitHost()
            .AddCapabilities("counter.read")
            .AddResourceType<CounterResource>()
            .Module("factory", module => module.Command(
                "Create",
                context => context.Resource(
                    new CounterResource(context.Argument<int>("value"))),
                SpellkitCommandParameter.Required<int>("value")));

        using var session = host.CreateInstance();
        Success(session, """
            import factory
            let counter = factory.Create(4)
            assert(4, counter.Value())
            assert(4, counter.AsyncValue())
            assert("resource.Counter.Value",
                host.Commands.Describe("resource.Counter.Value").Name)
            assert(nil, host.Commands.Describe("resource.Counter.Add"))
            """);
        Failure(session, """
            import factory
            factory.Create(4).Add(1)
            """);
        Failure(session, """
            import factory
            factory.Create(4).Hidden()
            """);

        AssertThrows<InvalidOperationException>(
            () => host.AddResourceType<CounterResource>(),
            "duplicate CLR resource type registration");

        using var unregistered = new SpellkitHost()
            .Module("factory", module => module.Command(
                "Create",
                context => context.Resource(new CounterResource(1))))
            .CreateInstance();
        Failure(unregistered, "import factory\nfactory.Create()");
    }

    internal static void ResourceReleaseCallbacks()
    {
        var released = new List<string>();
        var host = new SpellkitHost()
            .AddResourceType<TransientReleaseResource>()
            .Module("factory", module => module.Command(
                "Create",
                context => context.Resource(
                    new TransientReleaseResource(
                        context.Argument<string>("name"),
                        released)),
                SpellkitCommandParameter.Required<string>("name")));

        var session = host.CreateInstance();
        Success(session, """
            import factory
            let resource = factory.Create("explicit")
            assert(true, resource.Release())
            assert(false, resource.Release())
            """);
        Assert(released.SequenceEqual(new[] { "explicit" }), "explicit release callback once");

        Success(session, """
            let resetResource = factory.Create("reset")
            """);
        session.Reset();
        Assert(
            released.SequenceEqual(new[] { "explicit", "reset" }),
            "reset release callback");

        Success(session, """
            import factory
            let disposeResource = factory.Create("dispose")
            """);
        session.Dispose();
        Assert(
            released.SequenceEqual(new[] { "explicit", "reset", "dispose" }),
            "session disposal release callback");

        var attempted = new List<string>();
        var failingHost = new SpellkitHost()
            .AddResourceType<FailingReleaseResource>()
            .Module("factory", module => module.Command(
                "Create",
                context => context.Resource(
                    new FailingReleaseResource(
                        context.Argument<string>("name"),
                        attempted)),
                SpellkitCommandParameter.Required<string>("name")));
        var failingSession = failingHost.CreateInstance();
        Success(failingSession, """
            import factory
            let first = factory.Create("first")
            let second = factory.Create("second")
            """);
        AssertThrows<AggregateException>(
            failingSession.Dispose,
            "release callback failure aggregation");
        Assert(
            attempted.SequenceEqual(new[] { "first", "second" }),
            "all release callbacks run after a failure");
        failingSession.Dispose();
    }

    internal static void SharedResourceHandles()
    {
        var releases = 0;
        var resource = new SharedReleaseResource("shared", () => releases++);
        var host = new SpellkitHost()
            .AddResourceType<SharedReleaseResource>()
            .Module("factory", module => module.Command(
                "Shared",
                context => context.Resource(resource)));

        var session = host.CreateInstance();
        Success(session, """
            import factory
            let first = factory.Shared()
            let second = factory.Shared()
            assert(first.Id, second.Id)
            """);
        Failure(session, "first.Release()");
        session.Reset();
        AssertEqual(0, releases, "shared resource survives reset");

        Success(session, """
            import factory
            let afterReset = factory.Shared()
            assert("shared", afterReset.Name())
            """);
        Failure(session, "afterReset.Release()");
        session.Dispose();
        AssertEqual(1, releases, "shared resource released once with session");
    }

    internal static void SharedState()
    {
        using var session = new SpellkitHost()
            .AddCapabilities("state.*")
            .CreateInstance();

        Success(session, """
            host.State["score"] = 10
            assert(10, host.State["score"])
            assert(true, host.State.Has("score"))
            """);

        AssertEqual(10L, session.Environment.State.Get<long>("score"), "shared state");
        Assert(session.Environment.State.TryGet<long>("score", out var score) && score == 10,
            "typed state lookup");
        Assert(!session.Environment.State.TryGet<long>("missing", out var missing) && missing == 0,
            "missing typed state lookup");
        session.Environment.State.Set<object?>("nil", null);
        Assert(session.Environment.State.TryGet<int>("nil", out var nil) && nil == 0,
            "nil typed state lookup");
        session.Environment.State.Set("list", new[] { 1, 2, 3 });
        Assert(
            session.Environment.State.TryGet<int[]>("list", out var list)
            && list is not null
            && list.SequenceEqual(new[] { 1, 2, 3 }),
            "state uses common host type conversion");
        AssertThrows<InvalidCastException>(
            () => session.Environment.State.Get<DateTime>("score"),
            "invalid state conversion");
        Assert(!session.Environment.State.TryGet<DateTime>("score", out _),
            "invalid optional state conversion");

        session.Environment.State.Set("fromHost", 42);
        AssertEqual(SpellkitStateOwner.Host, session.Environment.State.GetOwner("fromHost")!.Value,
            "host-owned state owner");
        session.Environment.State.SetScript("fromScriptHost", 11);
        AssertEqual(SpellkitStateOwner.Script, session.Environment.State.GetOwner("fromScriptHost")!.Value,
            "script-owned state owner from C#");
        Success(session, """
            assert(42, host.State["fromHost"])
            assert("Host", host.State.Owner("fromHost"))
            assert(false, host.State.Remove("fromHost"))
            assert(42, host.State["fromHost"])

            assert(11, host.State["fromScriptHost"])
            host.State["fromScriptHost"] = 12
            assert("Script", host.State.Owner("fromScriptHost"))
            assert(12, host.State["fromScriptHost"])
            assert(true, host.State.Remove("fromScriptHost"))
            assert(nil, host.State["fromScriptHost"])
            """);
        Failure(session, "host.State[\"fromHost\"] = 43");
        Success(session, """
            host.State["scriptOnly"] = 1
            host.State.Clear()
            assert(nil, host.State["scriptOnly"])
            assert(42, host.State["fromHost"])
            """);

        session.Reset();
        Assert(!session.Environment.State.Contains("score"), "state reset");
    }

    internal static void Signals()
    {
        using var session = new SpellkitHost()
            .AddCapabilities("state.*", "player.*")
            .AddSignal(
                "player.hit",
                listenCapability: "player.listen",
                emitCapability: "player.emit")
            .CreateInstance();

        var hostDeliveries = new List<long>();
        var hostSubscription = session.Environment.Signals.Subscribe(
            "player.hit",
            signal =>
            {
                Assert(signal.TryGetPayload<long>(out var payload), "typed signal payload");
                AssertEqual(payload, signal.GetPayload<long>(), "required signal payload");
                Assert(!signal.TryGetPayload<DateTime>(out _), "invalid signal payload conversion");
                AssertThrows<InvalidCastException>(
                    () => signal.GetPayload<DateTime>(),
                    "required invalid signal payload conversion");
                hostDeliveries.Add(payload);
            });

        Success(session, """
            func receive(value) {
                host.State["last"] = value
                host.State["count"] = host.State["count"] + 1
            }

            func receiveOnce(value) {
                host.State["once"] = value
            }

            func canceled(value) {
                host.State["canceled"] = value
            }

            host.State["count"] = 0
            host.Signals.On("player.hit", receive)
            host.Signals.Once("player.hit", receiveOnce)
            let canceledSubscription = host.Signals.On("player.hit", canceled)
            assert(true, host.Signals.Off(canceledSubscription))
            """);
        Success(session, $"assert(false, host.Signals.Off({hostSubscription}))");
        Failure(session, """
            host.Signals.On("player.hit", receive)
            throw Exception<Error>("rollback subscription")
            """);

        session.Environment.Signals.Emit("player.hit", 5);
        var first = session.DispatchSignals();
        Assert(first.Success && first.Delivered == 1,
            "first signal delivery: " + string.Join("; ", first.Failures.Select(error => error.Message)));
        Success(session, """
            assert(5, host.State["last"])
            assert(5, host.State["once"])
            assert(1, host.State["count"])
            assert(nil, host.State["canceled"])
            """);

        Success(session, "host.Signals.Emit(\"player.hit\", 8)");
        var second = session.DispatchSignals();
        Assert(second.Success && second.Delivered == 1, "second signal delivery");
        Success(session, """
            assert(8, host.State["last"])
            assert(5, host.State["once"])
            assert(2, host.State["count"])
            """);
        Assert(hostDeliveries.SequenceEqual(new long[] { 5, 8 }), "host signal subscribers");

        session.Reset();
        session.Environment.Signals.Emit("player.hit", 9);
        Assert(session.DispatchSignals().Success, "signal delivery after reset");
        AssertEqual(3, hostDeliveries.Count, "host subscriptions survive reset");
        Assert(!session.Environment.State.Contains("last"), "script subscriptions are cleared by reset");
    }

    internal static void ConfigurationAndOwnership()
    {
        var context = new DisposableProbe();
        var initialLogs = new List<SpellkitLogEntry>();
        var host = new SpellkitHost(new()
        {
            Log = initialLogs.Add
        });
        var first = host.CreateInstance(context);

        host.Module("late", module => module.Command("Value", _ => 42));

        Success(first, "host.Log.Info(\"first\")");
        AssertEqual(1, initialLogs.Count, "snapshotted initial handler");
        Failure(first, "import late");

        using (var second = host.CreateInstance())
        {
            Success(second, "import late\nassert(42, late.Value())");
            Success(second, "host.Log.Info(\"second\")");
        }

        AssertEqual(2, initialLogs.Count, "shared initial handler");

        var state = first.Environment.State;
        first.Dispose();
        AssertThrows<ObjectDisposedException>(() => state.Contains("key"),
            "session-owned state disposal");
        Assert(!context.Disposed, "borrowed host context ownership");
    }

    internal static void StateCapabilities()
    {
        using var session = new SpellkitHost()
            .AddCapabilities("state.read")
            .CreateInstance();

        session.Environment.State.Set("value", 3);
        Success(session, "assert(3, host.State[\"value\"])");
        Failure(session, "host.State[\"value\"] = 4");
    }

    internal static void Logs()
    {
        var logs = new List<SpellkitLogEntry>();
        var handledLogs = new List<SpellkitLogEntry>();
        using var session = new SpellkitHost(new()
        {
            Log = entry =>
            {
                logs.Add(entry);
                handledLogs.Add(entry);
            }
        })
            .AddCapabilities("log.write", "signal.listen")
            .AddSignal("tick", listenCapability: "signal.listen")
            .Module("work", module => module.Command("Run", context =>
            {
                context.Log(
                    SpellkitLogLevel.Warning,
                    "host command",
                    new Dictionary<string, object?> { ["step"] = 2 });
                return context.ExecutionId.ToString();
            }))
            .CreateInstance();

        var execution = session.Execute("""
            import work
            host.Log.Debug("debug")
            host.Log.Info("script", (source: "spellkit", count: 2))
            host.Log.Error("error")
            work.Run()
            """);
        Assert(execution.Success, "telemetry execution");
        Assert(execution.ExecutionId != Guid.Empty, "execution correlation ID");
        AssertEqual(4, logs.Count, "log count");
        AssertEqual(logs.Count, handledLogs.Count, "multiple log handlers");
        Assert(logs.All(log => log.ExecutionId == execution.ExecutionId), "log correlation IDs");
        AssertEqual("spellkit", (string)logs[1].Properties["source"]!,
            "structured log property");
        AssertEqual("Run", logs.Single(log => log.Message == "host command").Command!,
            "command log name");

        Success(session, """
            func onTick(value) {
                host.Log.Info("signal", (value: value))
            }
            host.Signals.On("tick", onTick)
            """);
        session.Environment.Signals.Emit("tick", 3);
        var dispatch = session.DispatchSignals();
        Assert(dispatch.Success, "telemetry signal dispatch");
        var signalLog = logs.Single(log => log.Message == "signal");
        AssertEqual(dispatch.ExecutionId, signalLog.ExecutionId, "signal correlation ID");
        Assert(signalLog.Command is null, "signal log command name");

        using var denied = new SpellkitHost(new()
        {
            Log = logs.Add
        })
            .AddCapabilities("other")
            .CreateInstance();
        Failure(denied, "host.Log.Info(\"denied\")");
    }

    internal static void TelemetryExecutionContextIsolation()
    {
        var logs = new ConcurrentQueue<SpellkitLogEntry>();
        using var commandStarted = new ManualResetEventSlim();
        using var releaseCommand = new ManualResetEventSlim();
        using var session = new SpellkitHost(new()
        {
            Log = logs.Enqueue
        })
            .Module("telemetry", module => module.Command("Wait", context =>
            {
                commandStarted.Set();
                releaseCommand.Wait();
                context.Log(SpellkitLogLevel.Info, "command");
                return null;
            }))
            .CreateInstance();

        var execution = Task.Run(() =>
            session.Execute("import telemetry\ntelemetry.Wait()"));
        Assert(commandStarted.Wait(TimeSpan.FromSeconds(5)), "telemetry command entered");

        session.Environment.Telemetry.Write(SpellkitLogLevel.Info, "external");
        releaseCommand.Set();
        var result = execution.GetAwaiter().GetResult();

        Assert(result.Success, "telemetry isolation execution");
        var external = logs.Single(entry => entry.Message == "external");
        var command = logs.Single(entry => entry.Message == "command");
        AssertEqual(Guid.Empty, external.ExecutionId, "external log execution ID");
        Assert(external.Command is null, "external log command");
        AssertEqual(result.ExecutionId, command.ExecutionId, "command log execution ID");
        AssertEqual("Wait", command.Command!, "command log scope");
    }

    internal static void ExecutionLimits()
    {
        using (var session = new SpellkitHost(new()
        {
            Limits = new() { MaxInstructions = 100 }
        })
            .CreateInstance())
        {
            var result = FailureResult(session, """
                mut value = 0
                while true {
                    value += 1
                }
                """);
            AssertLimit(result, SpellkitExecutionLimitKind.Instructions);
            AssertEqual(100L, result.Metrics.Instructions, "instruction metrics");
        }

        using (var session = new SpellkitHost(new()
        {
            Limits = new() { MaxHostCommands = 1 }
        })
            .Module("limit", module => module.Command("Ping", _ => null))
            .CreateInstance())
        {
            var result = FailureResult(session, """
                import limit
                limit.Ping()
                limit.Ping()
                """);
            AssertLimit(result, SpellkitExecutionLimitKind.HostCommands);
        }

        using (var session = new SpellkitHost(new()
        {
            Limits = new() { MaxCallDepth = 5 }
        })
            .CreateInstance())
        {
            var result = FailureResult(session, """
                func recurse(value) => value == 0 ? 0 : 1 + recurse(value - 1)
                recurse(20)
                """);
            AssertLimit(result, SpellkitExecutionLimitKind.CallDepth);
        }

        var timeProvider = new ManualTimeProvider();
        using (var commandStarted = new ManualResetEventSlim())
        using (var session = new SpellkitHost(new()
        {
            Limits = new()
            {
                MaxExecutionTime = TimeSpan.FromMilliseconds(20),
                TimeProvider = timeProvider
            }
        })
            .Module("limit", module => module.Command("WaitForCancellation", context =>
            {
                commandStarted.Set();
                context.CancellationToken.WaitHandle.WaitOne();
                context.CancellationToken.ThrowIfCancellationRequested();
                return null;
            }))
            .CreateInstance())
        {
            var execution = Task.Run(() =>
                session.Execute("import limit\nlimit.WaitForCancellation()"));
            Assert(commandStarted.Wait(TimeSpan.FromSeconds(5)), "host command entered");
            timeProvider.Advance(TimeSpan.FromMilliseconds(21));
            var result = execution.GetAwaiter().GetResult();
            AssertLimit(result, SpellkitExecutionLimitKind.Time);
        }

        using (var cancellation = new CancellationTokenSource())
        using (var session = new SpellkitHost().CreateInstance())
        {
            cancellation.Cancel();
            var result = FailureResult(session, "1", cancellation.Token);
            Assert(result.Failure is
                { Kind: SpellkitFailureKind.Cancelled, Exception: OperationCanceledException },
                "execution cancellation");
        }

        using (var cancellation = new CancellationTokenSource())
        using (var session = new SpellkitHost()
            .Module("limit", module => module.Command(
                "HasToken",
                context => context.CancellationToken.CanBeCanceled))
            .CreateInstance())
        {
            var result = session.Execute(
                "import limit\nassert(true, limit.HasToken())",
                cancellation.Token);
            Assert(result.Success, "host command cancellation token");
        }

        using (var session = new SpellkitHost(new()
        {
            Limits = new() { MaxSignals = 1 }
        })
            .AddSignal("tick")
            .CreateInstance())
        {
            session.Environment.Signals.Emit("tick", 1);
            session.Environment.Signals.Emit("tick", 2);
            var first = session.DispatchSignals();
            AssertEqual(1, first.Delivered, "limited signal delivery");
            Assert(first.Failures.Single() is
                { Kind: SpellkitFailureKind.Limit, Limit: SpellkitExecutionLimitKind.Signals },
                "signal limit error");
            AssertEqual(1, first.Metrics.Signals, "signal metrics");
            AssertEqual(1, session.DispatchSignals().Delivered, "remaining signal delivery");
        }

        using (var removedEval = new SpellkitHost().CreateInstance())
        {
            Failure(removedEval, "eval(\"1 + 1\")");
        }

    }

    internal static void ResultContracts()
    {
        using var session = new SpellkitHost()
            .Module("asynccommand", module => module.AsyncCommand(
                "Value",
                async _ =>
                {
                    await Task.Yield();
                    return 42;
                }))
            .CreateInstance();

        var asynchronous = session.ExecuteAsync(
                "import asynccommand\nasynccommand.Value()")
            .GetAwaiter()
            .GetResult();
        Assert(asynchronous.Success && asynchronous.GetValue<long>() == 42,
            "asynchronous execution and host command");

        var value = session.Execute("[1, 2, 3]");
        Assert(value.Success, "typed execution result");
        Assert(
            value.TryGetValue<int[]>(out var items)
            && items is not null
            && items.SequenceEqual(new[] { 1, 2, 3 }),
            "optional typed execution result");
        Assert(value.GetValue<int[]>()!.SequenceEqual(new[] { 1, 2, 3 }),
            "required typed execution result");
        Assert(!value.TryGetValue<DateTime>(out _), "invalid execution result conversion");
        AssertThrows<InvalidCastException>(
            () => value.GetValue<DateTime>(),
            "required invalid execution result conversion");

        var nil = session.Execute("nil");
        Assert(nil.TryGetValue<int>(out var nilValue) && nilValue == 0,
            "nil typed execution result");

        var compilation = FailureResult(session, "let =");
        Assert(!compilation.TryGetValue<int>(out _), "failed execution has no typed result");
        AssertThrows<InvalidOperationException>(
            () => compilation.GetValue<int>(),
            "failed execution required result");
        Assert(compilation.Failure?.Kind == SpellkitFailureKind.Compilation,
            "compilation failure kind");
        Assert(compilation.Diagnostics.Any(diagnostic =>
            diagnostic.Severity == SpellkitDiagnosticSeverity.Error),
            "compilation diagnostics");

        var runtime = FailureResult(session, "throw Exception<Error>(\"failure\")");
        Assert(runtime.Failure?.Kind == SpellkitFailureKind.Runtime, "runtime failure kind");

        var logs = new List<SpellkitLogEntry>();
        using var commandFailureSession = new SpellkitHost(new()
        {
            Log = logs.Add
        })
            .Module("brokencommand", module => module.Command(
                "Fail",
                _ => throw new InvalidOperationException("sensitive host detail")))
            .CreateInstance();
        var commandFailure = FailureResult(
            commandFailureSession,
            "import brokencommand\nbrokencommand.Fail()");
        Assert(!Describe(commandFailure).Contains("sensitive host detail", StringComparison.Ordinal),
            "host command failure hides exception details from scripts");
        Assert(logs.Any(log => log.Level == SpellkitLogLevel.Error
            && Equals(log.Properties["exceptionMessage"], "sensitive host detail")),
            "host command failure logs exception details for the host");

        using var hostFailureSession = new SpellkitHost()
            .Module("broken", module => module.Unit(
                () => throw new InvalidOperationException("module factory failure")))
            .CreateInstance();
        var host = FailureResult(hostFailureSession, "import broken");
        Assert(host.Failure?.Kind == SpellkitFailureKind.Host, "host failure kind");
        Assert(host.Failure?.Exception is InvalidOperationException,
            $"host failure preserves exception (actual: "
            + $"{host.Failure?.Exception?.GetType().FullName ?? "<null>"})");
        Success(hostFailureSession, "1 + 1");

        using var signalSession = new SpellkitHost().AddSignal("failed").CreateInstance();
        var subscription = signalSession.Environment.Signals.Subscribe(
            "failed",
            _ => throw new InvalidOperationException("host signal failure"));
        signalSession.Environment.Signals.Emit("failed", 1);
        var dispatch = signalSession.DispatchSignals();
        Assert(dispatch.Failures.Single().Kind == SpellkitFailureKind.Host,
            "host signal failure kind");
        Assert(signalSession.Environment.Signals.Unsubscribe(subscription),
            "host signal subscription cleanup");
    }

    internal static void Tracing()
    {
        var traces = new List<SpellkitTraceEvent>();
        var handledTraces = new List<SpellkitTraceEvent>();
        using var session = new SpellkitHost(new()
        {
            Trace = trace =>
            {
                traces.Add(trace);
                handledTraces.Add(trace);
            }
        })
            .AddCapabilities("use")
            .AddResourceType<TracingCounterResource>()
            .AddSignal("tick")
            .Module("observe", module =>
            {
                module.Command("Create", context =>
                    context.Resource(new TracingCounterResource(1)));
                module.Command("Secret", null, "secret", _ => null);
            })
            .CreateInstance();

        var execution = session.Execute("""
            import observe
            let counter = observe.Create()
            counter.Value()
            counter.Release()
            """);
        Assert(execution.Success, "traced execution");
        Assert(execution.Metrics.Instructions > 0, "traced instruction metrics");
        Assert(execution.Metrics.HostCommands >= 2, "traced host command metrics");
        AssertEqual(traces.Count, handledTraces.Count, "multiple trace handlers");
        Assert(traces.Any(trace => trace.Kind == SpellkitTraceKind.ExecutionStarted),
            "execution started trace");
        Assert(traces.Any(trace => trace.Kind == SpellkitTraceKind.ExecutionCompleted),
            "execution completed trace");
        Assert(traces.Any(trace => trace.Kind == SpellkitTraceKind.Compilation), "compilation trace");
        Assert(traces.Any(trace => trace.Kind == SpellkitTraceKind.VmExecution), "VM trace");
        Assert(traces.Any(trace => trace.Kind == SpellkitTraceKind.HostCommand
            && trace.Name == "Create" && trace.Duration is not null), "host command trace");
        Assert(traces.Any(trace => trace.Kind == SpellkitTraceKind.ResourceCreated),
            "resource creation trace");
        Assert(traces.Any(trace => trace.Kind == SpellkitTraceKind.ResourceReleased),
            "resource release trace");

        Failure(session, "observe.Secret()");
        Assert(traces.Any(trace => trace.Kind == SpellkitTraceKind.CapabilityDenied
            && trace.Name == "secret"), "capability denial trace");

        session.Environment.Signals.Emit("tick", 1);
        var dispatch = session.DispatchSignals();
        Assert(dispatch.Success, "traced signal dispatch");
        Assert(traces.Any(trace => trace.Kind == SpellkitTraceKind.SignalEmitted
            && trace.Name == "tick"), "signal emitted trace");
        Assert(traces.Any(trace => trace.Kind == SpellkitTraceKind.SignalDelivered
            && trace.ExecutionId == dispatch.ExecutionId), "signal delivered trace");
        var dispatchCompleted = traces.Single(trace =>
            trace.Kind == SpellkitTraceKind.ExecutionCompleted
            && trace.ExecutionId == dispatch.ExecutionId);
        Assert(Equals(dispatchCompleted.Data["success"], true),
            "signal dispatch completion success");
        AssertEqual(dispatch.Delivered, (int)dispatchCompleted.Data["delivered"]!,
            "signal dispatch completion delivered count");

        var failedSubscription = session.Environment.Signals.Subscribe(
            "tick",
            _ => throw new InvalidOperationException("traced host signal failure"));
        session.Environment.Signals.Emit("tick", 2);
        var failedDispatch = session.DispatchSignals();
        Assert(!failedDispatch.Success, "failed traced signal dispatch");
        var failedDispatchCompleted = traces.Single(trace =>
            trace.Kind == SpellkitTraceKind.ExecutionCompleted
            && trace.ExecutionId == failedDispatch.ExecutionId);
        Assert(Equals(failedDispatchCompleted.Data["success"], false),
            "failed signal dispatch completion success");
        Assert(session.Environment.Signals.Unsubscribe(failedSubscription),
            "failed traced signal subscription cleanup");

        var tracesAfterFailure = new List<SpellkitTraceEvent>();
        Action<SpellkitTraceEvent> traceHandlers =
            _ => throw new InvalidOperationException("ignored trace failure");
        traceHandlers += tracesAfterFailure.Add;
        using var ignoredTraceFailure = new SpellkitHost(new()
        {
            Trace = traceHandlers
        })
            .CreateInstance();
        Success(ignoredTraceFailure, "1 + 1");
        Assert(tracesAfterFailure.Count > 0, "trace handler continues after failure");
    }

    private static void Success(SpellkitInstance session, string source)
    {
        var result = session.Execute(source);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Hosting API test failed: " + Describe(result));
        }
    }

    private static void Failure(SpellkitInstance session, string source)
    {
        var result = FailureResult(session, source);
        if (result.Success)
        {
            throw new InvalidOperationException("Hosting API test expected execution to fail.");
        }
    }

    private static SpellkitExecutionResult FailureResult(
        SpellkitInstance session,
        string source,
        CancellationToken cancellationToken = default)
    {
        var result = session.Execute(source, cancellationToken);
        if (result.Success)
        {
            throw new InvalidOperationException(
                "Hosting API test expected execution to fail: " + source.Replace('\n', ' '));
        }

        return result;
    }

    private static void AssertLimit(SpellkitExecutionResult result, SpellkitExecutionLimitKind expected)
    {
        if (result.Failure is not
            { Kind: SpellkitFailureKind.Limit, Limit: var actual } || actual != expected)
        {
            throw new InvalidOperationException(
                $"Expected {expected} execution limit, got {result.Failure?.Kind}: "
                + result.Failure?.Message);
        }
    }

    private static string Describe(SpellkitExecutionResult result) =>
        result.Failure?.Message
        ?? string.Join("; ", result.Diagnostics.Select(message => message.Message));

    private static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Hosting API assertion failed: {name}.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string name) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Hosting API assertion failed for {name}: expected {expected}, got {actual}.");
        }
    }

    private static void AssertThrows<T>(Action action, string name) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Hosting API assertion failed for {name}: expected {typeof(T).Name}.");
    }

    private static void AssertHasMethod<T>(string name, string purpose)
    {
        if (typeof(T).GetMethods().All(method => method.Name != name))
        {
            throw new InvalidOperationException(
                $"Hosting API assertion failed for {purpose}: missing method {name}.");
        }
    }

    private static void AssertNoMethod<T>(string name, string purpose)
    {
        if (typeof(T).GetMethods().Any(method => method.Name == name))
        {
            throw new InvalidOperationException(
                $"Hosting API assertion failed for {purpose}: unexpected method {name}.");
        }
    }

    private static void AssertHasProperty<T>(string name, string purpose)
    {
        if (typeof(T).GetProperty(name) is null)
        {
            throw new InvalidOperationException(
                $"Hosting API assertion failed for {purpose}: missing property {name}.");
        }
    }

    private static void AssertNoProperty<T>(string name, string purpose)
    {
        if (typeof(T).GetProperty(name) is not null)
        {
            throw new InvalidOperationException(
                $"Hosting API assertion failed for {purpose}: unexpected property {name}.");
        }
    }

    private static void AssertEditorBrowsableNever<T>(string name)
    {
        var methods = typeof(T).GetMethods().Where(method => method.Name == name).ToArray();
        Assert(methods.Length > 0, $"editor-hidden API exists: {typeof(T).Name}.{name}");
        Assert(methods.All(method =>
                method.GetCustomAttributes(
                        typeof(System.ComponentModel.EditorBrowsableAttribute),
                        inherit: false)
                    .SingleOrDefault()
                is System.ComponentModel.EditorBrowsableAttribute
                {
                    State: System.ComponentModel.EditorBrowsableState.Never
                }),
            $"editor-hidden API annotation: {typeof(T).Name}.{name}");
    }

    private sealed class Counter
    {
        public Counter(int value) => Value = value;

        public int Value { get; set; }
    }

    [SpellkitResource("Counter")]
    private sealed class CounterResource(int value) : SpellkitResource
    {
        private int current = value;

        [SpellkitCommand(Description = "Reads the current value.", Capability = "counter.read")]
        public int Value() => current;

        [SpellkitCommand(Description = "Reads the current value asynchronously.", Capability = "counter.read")]
        public async ValueTask<int> AsyncValue()
        {
            await Task.Yield();
            return current;
        }

        [SpellkitCommand(Description = "Adds to the current value.", Capability = "counter.write")]
        public int Add(int amount) => current += amount;

        public string Hidden() => "not exposed";
    }

    private sealed class UnattributedResource : SpellkitResource { }

    [SpellkitResource("Duplicate")]
    private sealed class DuplicateCommandResource : SpellkitResource
    {
        [SpellkitCommand("Same")]
        public void First() { }

        [SpellkitCommand("Same")]
        public void Second() { }
    }

    [SpellkitResource("Counter", Lifetime = SpellkitResourceLifetime.Transient)]
    private sealed class TransientCounterResource(int value) : SpellkitResource
    {
        private int current = value;

        [SpellkitCommand]
        public int Value() => current;

        [SpellkitCommand]
        public int Add(int amount) => current += amount;
    }

    [SpellkitResource("Counter", Lifetime = SpellkitResourceLifetime.Transient)]
    private sealed class TracingCounterResource(int value) : SpellkitResource
    {
        [SpellkitCommand]
        public int Value() => value;
    }

    [SpellkitResource("ReleaseProbe", Lifetime = SpellkitResourceLifetime.Transient)]
    private sealed class TransientReleaseResource(
        string name,
        ICollection<string> released) : SpellkitResource
    {
        [SpellkitCommand]
        public string Name() => name;

        protected override void OnRelease() => released.Add(name);
    }

    [SpellkitResource("FailingProbe", Lifetime = SpellkitResourceLifetime.Transient)]
    private sealed class FailingReleaseResource(
        string name,
        ICollection<string> attempted) : SpellkitResource
    {
        protected override void OnRelease()
        {
            attempted.Add(name);
            if (name == "first")
            {
                throw new InvalidOperationException("release failed");
            }
        }
    }

    [SpellkitResource("SharedProbe")]
    private sealed class SharedReleaseResource(
        string name,
        Action released) : SpellkitResource
    {
        [SpellkitCommand]
        public string Name() => name;

        protected override void OnRelease() => released();
    }

    private sealed class FirstInteropValue
    {
        public string Name() => "first";

        public static string StaticSecret() => "static-secret";
    }

    private sealed class SecondInteropValue
    {
        public string Name() => "second";
    }

    private interface IVisibleInteropValue
    {
        string Name();

        IVisibleInteropValue Child();

        IReadOnlyList<IVisibleInteropValue> Children();

        IReadOnlyDictionary<string, IVisibleInteropValue> ChildrenByName();
    }

    private sealed class VisibleInteropValue : IVisibleInteropValue
    {
        public string Name() => "visible";

        public IVisibleInteropValue Child() => new VisibleInteropValue();

        public IReadOnlyList<IVisibleInteropValue> Children() =>
            new[] { new VisibleInteropValue() };

        public IReadOnlyDictionary<string, IVisibleInteropValue> ChildrenByName() =>
            new Dictionary<string, IVisibleInteropValue>
            {
                ["first"] = new VisibleInteropValue()
            };

        public string Secret() => "secret";
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class EmptyUnit : ForeignUnit { }
}
