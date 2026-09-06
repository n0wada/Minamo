using Minamo.Compiler;
using Minamo.Hosting;
using Minamo.Linker;
using Minamo.Runtime;
using System.Collections.Concurrent;
using System.Text;

namespace Minamo.UnitTesting;

internal static class HostingScenarios
{
    internal static void HostConfigurationValidation()
    {
        AssertThrows<ArgumentException>(
            () => new MinamoHost().Module("bad-name", _ => { }),
            "invalid module name");
        AssertThrows<ArgumentException>(
            () => new MinamoHost().AddCapabilities("scene..read"),
            "invalid capability name");
        AssertThrows<ArgumentOutOfRangeException>(
            () => new MinamoHost(new()
            {
                CapabilityMode = (MinamoCapabilityMode)int.MaxValue
            }),
            "invalid capability mode");
        AssertThrows<ArgumentException>(
            () => new MinamoHost().AddSignal("player-hit"),
            "invalid signal name");
        AssertThrows<InvalidOperationException>(
            () => new MinamoHost().AddResourceType<UnattributedResource>(),
            "resource attribute is required");
        AssertThrows<InvalidOperationException>(
            () => new MinamoHost().AddResourceType<DuplicateCommandResource>(),
            "duplicate attributed resource commands");

        AssertThrows<ArgumentException>(
            () => new MinamoHost().Module("game", module => module.Command("bad-name", _ => null)),
            "invalid command name");
        AssertThrows<ArgumentException>(
            () => new MinamoHost().Module("game", module => module.Type("bad-type")),
            "invalid host type name");
        AssertThrows<ArgumentException>(
            () => new MinamoHost().Module("game", module => module.Command(
                "Move",
                _ => null,
                MinamoCommandParameter.Required<int>("bad-name"))),
            "invalid command parameter name");

        AssertThrows<InvalidOperationException>(
            () => new MinamoHost().Module(
                "custom",
                module => module.Unit(() => new EmptyUnit()).Command("Ignored", _ => null)),
            "custom unit then generated registration");
        AssertThrows<InvalidOperationException>(
            () => new MinamoHost().Module(
                "custom",
                module => module.Command("Ignored", _ => null).Unit(() => new EmptyUnit())),
            "generated registration then custom unit");

        var parameters = new[] { MinamoCommandParameter.Required<int>("value") };
        using var session = new MinamoHost()
            .Module("safe", module => module.Command(
                "Echo",
                context => context.Argument<int>("value"),
                parameters))
            .CreateInstance();
        parameters[0] = MinamoCommandParameter.Required<string>("changed");
        Success(session, "import safe\nassert(3, safe.Echo(3))");

        using var numericSession = new MinamoHost()
            .Module("numeric", module => module.Command(
                "Int32",
                context => context.Argument<int>("value"),
                MinamoCommandParameter.Required<int>("value")))
            .CreateInstance();
        var overflow = FailureResult(
            numericSession,
            "import numeric\nnumeric.Int32(4294967296)");
        Assert(overflow.Failure?.Kind == MinamoFailureKind.Runtime,
            "host numeric overflow failure");

        using var interopSession = new MinamoHost()
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

        using var boundedInteropSession = new MinamoHost()
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
        var assembly = typeof(MinamoHost).Assembly;
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
            "Minamo.CultureInfoSettings",
            "Minamo.Runtime.MinamoMachine",
            "Minamo.Runtime.EvalStack",
            "Minamo.Runtime.ExecutionResult",
            "Minamo.Runtime.TerminationReason",
            "Minamo.Compiler.MinamoCompilerEngine",
            "Minamo.Linker.MinamoIncrementalLinker",
            "Minamo.Debug.MinamoDebugger"
        })
        {
            var type = assembly.GetType(name, throwOnError: true)!;
            Assert(!type.IsPublic, $"internal API boundary for {name}");
        }

        var runtimeContext = typeof(MinamoInstance).GetProperty("RuntimeContext");
        Assert(runtimeContext is null, "session runtime context is not public");

        AssertPublicApiLayer(
            assembly,
            "application",
            type => type.Namespace is "Minamo.Hosting",
            typeof(MinamoHost));
        AssertPublicApiLayer(
            assembly,
            "tooling",
            IsToolingApi,
            typeof(Minamo.Parser.MinamoParser),
            typeof(Minamo.Compiler.Op),
            typeof(Minamo.Compiler.Unit),
            typeof(Minamo.Parser.Model.SyntaxNode),
            typeof(Minamo.Debug.DebugInfo));
        AssertPublicApiLayer(
            assembly,
            "runtime extension",
            IsRuntimeExtensionApi,
            typeof(Minamo.Runtime.RuntimeContext),
            typeof(Minamo.Runtime.Types.MinamoObject));
    }

    internal static void PublicApiNames()
    {
        AssertHasMethod<MinamoHost>("AddCapabilities", "host capability setup");
        AssertNoMethod<MinamoHost>("Allow", "old host capability setup name");
        AssertNoMethod<MinamoHost>("WithLimits", "limits moved to host options");
        AssertHasMethod<MinamoHost>("UseFileLookup", "file import lookup setup");
        AssertHasMethod<MinamoHost>("AddResourceType", "reusable resource type registration");
        AssertNoMethod<MinamoHost>("ResourceType", "old resource type registration");
        AssertHasMethod<MinamoHost>("DisableFileImports", "explicit file import restriction");
        AssertNoMethod<MinamoHost>("OnLog", "logging moved to host options");
        AssertNoMethod<MinamoHost>("OnProgress", "removed progress registration");
        AssertNoMethod<MinamoHost>("OnTrace", "tracing moved to host options");
        AssertHasProperty<MinamoHostOptions>("Limits", "host execution limits");
        AssertHasProperty<MinamoHostOptions>("Signals", "host signal queue options");
        AssertHasProperty<MinamoHostOptions>("CapabilityMode", "host capability mode");
        AssertHasProperty<MinamoHostOptions>("Log", "host log handler");
        AssertNoProperty<MinamoHostOptions>("Progress", "removed host progress handler");
        AssertHasProperty<MinamoHostOptions>("Trace", "host trace handler");
        AssertHasProperty<MinamoHostOptions>("ExposeHostObject", "host object visibility");
        AssertNoMethod<MinamoCommandContext>("ReportProgress", "removed command progress reporting");
        AssertNoMethod<MinamoTelemetry>("Report", "removed telemetry progress reporting");
        Assert(
            typeof(MinamoHost).Assembly.GetType("Minamo.Hosting.MinamoProgressUpdate") is null,
            "removed progress payload");
        foreach (var name in new[]
        {
            "Minamo.Hosting.IMinamoLogHandler",
            "Minamo.Hosting.IMinamoProgressHandler",
            "Minamo.Hosting.IMinamoTraceHandler"
        })
        {
            Assert(
                typeof(MinamoHost).Assembly.GetType(name) is null,
                $"removed handler interface {name}");
        }
        AssertHasMethod<MinamoHost>("AddSignal", "signal registration");
        AssertNoMethod<MinamoHost>("Signal", "old signal registration name");
        AssertNoMethod<MinamoHost>("ApplyTo", "removed low-level module injection");
        AssertNoMethod<MinamoHost>("LogTo", "old log registration name");
        AssertNoMethod<MinamoHost>("ProgressTo", "old progress registration name");
        AssertNoMethod<MinamoHost>("TraceTo", "old trace registration name");

        AssertHasMethod<MinamoHost>("CreateInstance", "instance creation");
        AssertNoMethod<MinamoHost>("CreateSession", "removed session creation");
        AssertHasMethod<MinamoHost>("Compile", "program compilation");
        AssertHasMethod<MinamoHost>("CompileFile", "program file compilation");
        AssertNoMethod<MinamoInstance>("Execute", "synchronous instance execution");
        AssertHasMethod<MinamoInstance>("ExecuteAsync", "asynchronous instance execution");
        AssertNoMethod<MinamoInstance>("ExecuteFile", "synchronous explicit script file execution");
        AssertHasMethod<MinamoInstance>("ExecuteFileAsync", "asynchronous file execution");
        AssertNoMethod<MinamoInstance>("Start", "synchronous suspended run creation");
        AssertNoMethod<MinamoInstance>("OpenSelect", "synchronous basic interactive select creation");
        AssertHasMethod<MinamoInstance>("OpenSelectAsync", "asynchronous basic interactive select creation");
        AssertNoMethod<MinamoInstance>("OpenSelectSession", "synchronous advanced interactive select creation");
        AssertNoMethod<MinamoInstance>("OpenSelectSessionAsync", "internal select session creation");
        AssertNoMethod<MinamoInstance>("StartAsync", "removed script-initiated select execution");
        AssertNoMethod<MinamoInstance>("DispatchSignals", "synchronous pending signal dispatch");
        AssertHasMethod<MinamoInstance>("DispatchSignalsAsync", "asynchronous signal dispatch");
        AssertNoMethod<MinamoSelect>("Select", "synchronous basic select choice");
        AssertNoMethod<MinamoSelect>("Send", "synchronous basic select host event delivery");
        AssertHasMethod<MinamoSelect>("SelectAsync", "asynchronous basic select choice");
        AssertHasMethod<MinamoSelect>("SendAsync", "asynchronous basic select host event delivery");
        AssertHasMethod<MinamoSelect>("RefreshAsync", "asynchronous select refresh");
        AssertHasMethod<MinamoSelect>("InvalidateAsync", "asynchronous select invalidation");
        AssertHasMethod<MinamoSelect>("SelectAtRevisionAsync", "revision-aware select choice");
        AssertHasMethod<MinamoSelect>("SendAtRevisionAsync", "revision-aware select host event delivery");
        AssertHasProperty<MinamoSelect>("Description", "select description");
        Assert(!typeof(MinamoSelectSession).IsPublic, "select session stays internal");
        Assert(!typeof(MinamoSelectSnapshot).IsPublic, "select snapshot stays internal");
        AssertNoProperty<MinamoSelectResult>("Snapshot", "internal select result snapshot");
        AssertNoProperty<MinamoSelectRevisionMismatchException>("Snapshot", "internal stale-select snapshot");
        AssertHasMethod<MinamoExecutionResult>("GetValue", "typed execution result");
        AssertHasMethod<MinamoExecutionResult>("TryGetValue", "optional typed execution result");
        AssertNoProperty<MinamoExecutionResult>("Value", "removed raw execution result");
        AssertHasProperty<MinamoExecutionResult>("Execution", "execution details");
        AssertHasProperty<MinamoSignalDispatchResult>("Execution", "signal dispatch execution details");
        AssertHasProperty<MinamoProgram>("Diagnostics", "compiled program diagnostics");
        AssertHasMethod<MinamoEnvironment>("Expose", "environment name exposure");
        AssertHasMethod<MinamoEnvironment>("Set", "environment bindings");
        AssertNoMethod<MinamoEnvironment>("UseInput", "synchronous instance input setup");
        AssertHasMethod<MinamoEnvironment>("UseInputAsync", "asynchronous instance input setup");
        AssertHasMethod<MinamoEnvironment>("UseOutput", "instance output setup");
        AssertNoMethod<MinamoEnvironment>("UseSelect", "synchronous select runner setup");
        AssertNoMethod<MinamoEnvironment>("UseSelectAsync", "removed script-initiated select runner");

        AssertHasProperty<MinamoExecutionLimits>("MaxExecutionTime", "operation time limit");
        AssertNoProperty<MinamoExecutionLimits>("MaxTime", "old time limit name");

        AssertHasMethod<MinamoStateStore>("Set", "host-owned state setter");
        AssertHasMethod<MinamoStateStore>("SetScript", "script-owned state setter");
        AssertHasMethod<MinamoStateStore>("TryGet", "typed state lookup");
        AssertHasMethod<MinamoStateStore>("GetOwner", "state ownership inspection");
        AssertNoMethod<MinamoStateStore>("GetRaw", "internal raw state getter");
        AssertNoMethod<MinamoStateStore>("SetRaw", "internal raw host state setter");
        AssertNoMethod<MinamoStateStore>("SetScriptRaw", "internal raw script state setter");
        AssertNoMethod<MinamoSignalDispatcher>("EmitRaw", "internal raw signal emission");
        AssertHasMethod<MinamoSignalDispatcher>("TryEmit", "bounded signal emission");
        AssertHasProperty<MinamoSignalDispatcher>("MaxPending", "pending signal limit");
        AssertHasProperty<MinamoSignalDispatcher>("PendingCount", "pending signal count");
        AssertHasMethod<MinamoSignal>("GetPayload", "typed signal payload");
        AssertHasMethod<MinamoSignal>("TryGetPayload", "optional typed signal payload");
        AssertNoProperty<MinamoSignal>("Payload", "removed raw signal payload");
        AssertNoProperty<MinamoHostEnvironment>("Resources", "internal resource registry");
        Assert(
            typeof(MinamoHost).Assembly.GetType(
                "Minamo.Hosting.MinamoResourceRegistry") is { IsPublic: false },
            "resource registry is internal");

        AssertHasMethod<MinamoCommandContext>("Resource", "host resource creation");
        AssertHasMethod<MinamoCommandContext>("Callback", "host callback argument");
        AssertHasMethod<MinamoCommandContext>("CallbackTuple", "host tuple callback argument");
        AssertHasMethod<MinamoCommandContext>("CallbackAction", "host callback action argument");
        AssertNoMethod<MinamoCallback>("InvokeRaw", "removed raw callback invocation");
        AssertNoMethod<MinamoHost>("Service", "removed service registration");
        AssertHasMethod<MinamoCommandParameter>("Required", "required command parameter factory");
        AssertHasMethod<MinamoCommandParameter>("Optional", "optional command parameter factory");
        AssertHasMethod<MinamoModuleBuilder>("AsyncCommand", "asynchronous module command");
        AssertHasMethod<MinamoTypeBuilder>("AsyncCommand", "asynchronous type command");
        AssertNoMethod<MinamoHost>("Value", "removed bound value registration");
        AssertNoMethod<MinamoModuleBuilder>(
            "RuntimeInteropCommand",
            "removed runtime interop command");
        AssertNoMethod<MinamoTypeBuilder>(
            "RuntimeInteropCommand",
            "removed runtime interop type command");
        AssertEditorBrowsableNever<MinamoModuleBuilder>("RawCommand");
        AssertEditorBrowsableNever<MinamoModuleBuilder>("RawProperty");
        AssertEditorBrowsableNever<MinamoTypeBuilder>("RawCommand");
    }

    internal static void HostCommandCallbacks()
    {
        Func<long, long>? escapedCallback = null;
        using var session = new MinamoHost()
            .Module("callbacks", module =>
            {
                module.Command("Apply", context =>
                {
                    var callback = context.Callback<long, long>("callback");
                    return callback(context.Argument<long>("value"));
                },
                MinamoCommandParameter.Required<long>("value"),
                MinamoCommandParameter.Required<object>("callback"));

                module.Command("Combine", context =>
                {
                    var callback = context.Callback<string, string, string>("callback");
                    return callback(
                        context.Argument<string>("first"),
                        context.Argument<string>("second"));
                },
                MinamoCommandParameter.Required<string>("first"),
                MinamoCommandParameter.Required<string>("second"),
                MinamoCommandParameter.Required<object>("callback"));

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
                MinamoCommandParameter.Required<long>("first"),
                MinamoCommandParameter.Required<long>("second"),
                MinamoCommandParameter.Required<long>("third"),
                MinamoCommandParameter.Required<long>("fourth"),
                MinamoCommandParameter.Required<object>("callback"));

                module.Command("Notify", context =>
                {
                    var callback = context.CallbackAction<string>("callback");
                    callback(context.Argument<string>("value"));
                    return context.Environment.State.Get<string>("seen");
                },
                MinamoCommandParameter.Required<string>("value"),
                MinamoCommandParameter.Required<object>("callback"));

                module.Command("Capture", context =>
                {
                    escapedCallback = context.Callback<long, long>("callback");
                    return null;
                },
                MinamoCommandParameter.Required<object>("callback"));
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
        var host = new MinamoHost()
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

        var otherHost = new MinamoHost(new()
        {
            ExposeHostObject = false
        });
        AssertThrows<InvalidOperationException>(
            () => otherHost.CreateInstance(program),
            "compiled program cannot cross its host boundary");
    }

    internal static void InstanceEnvironmentNames()
    {
        var host = new MinamoHost();
        var compiled = host.Compile("self + world");
        Assert(compiled.Success && compiled.Value is not null, "environment-name program compiles");
        var program = compiled.GetValueOrThrow();

        using var first = host.CreateInstance(
            program,
            new MinamoEnvironment()
                .Expose("self", 2)
                .Expose("world", 3));
        using var second = host.CreateInstance(
            program,
            new MinamoEnvironment()
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
            new MinamoEnvironment()
                .Expose("self", 1));
        var missingRun = missing.Execute();
        Assert(!missingRun.Success, "missing environment name fails");
        Assert(missingRun.Failure?.Kind == MinamoFailureKind.Runtime,
            "missing environment name fails at runtime");
        Assert(Describe(missingRun).Contains("world", StringComparison.Ordinal),
            "missing environment name identifies name");

        var assignmentEnvironment = new MinamoEnvironment()
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
        var host = new MinamoHost(new()
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
            new MinamoEnvironment()
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
        type.Namespace is "Minamo.Hosting"
        || IsToolingApi(type)
        || IsRuntimeExtensionApi(type);

    private static bool IsToolingApi(Type type) =>
        type.Namespace is "Minamo.Codegen"
        || type.Namespace is "Minamo.Compiler"
        || type.Namespace is "Minamo.Debug"
        || type.Namespace is "Minamo.Linker"
        || type.Namespace is "Minamo.Parser"
        || type.Namespace is "Minamo.Parser.Model";

    private static bool IsRuntimeExtensionApi(Type type) =>
        type.Namespace is "Minamo"
        || type.Namespace is "Minamo.Runtime"
        || type.Namespace?.StartsWith("Minamo.Runtime.Types", StringComparison.Ordinal) == true
        || type.Namespace is "Minamo.Runtime.Interop";

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
        var root = Path.Combine(Path.GetTempPath(), "minamo-hosting-" + Guid.NewGuid());
        var outside = Path.Combine(Path.GetTempPath(), "minamo-outside-" + Guid.NewGuid() + ".nami");
        var outsideDirectory = Path.Combine(
            Path.GetTempPath(),
            "minamo-outside-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideDirectory);
        try
        {
            File.WriteAllText(outside, "func value() => 99", Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(outsideDirectory, "linked.nami"),
                "func value() => 100",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(root, "hostmod.nami"),
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
                    !lookup.Find(null, Path.Combine("linked", "linked.nami"), out _),
                    "symbolic link traversal outside lookup root is rejected");
            }
            catch (Exception ex) when (ex is IOException
                or PlatformNotSupportedException
                or UnauthorizedAccessException)
            {
                // Symbolic-link creation is not available in every test environment.
            }

            using (var allowed = new MinamoHost(new() { BuilderOptions = options })
                .UseFileLookup(lookup)
                .CreateInstance())
            {
                Success(allowed, "import hostmod\nassert(42, hostmod.value())");
            }

            using (var disabled = new MinamoHost(new() { BuilderOptions = options })
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
        var root = Path.Combine(Path.GetTempPath(), "minamo-execute-file-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var validPath = Path.Combine(root, "valid.nami");
            var invalidPath = Path.Combine(root, "invalid.nami");
            File.WriteAllText(validPath, "let answer = 42", Encoding.UTF8);
            File.WriteAllText(invalidPath, "let =", Encoding.UTF8);

            using var session = new MinamoHost()
                .AddSignal("tick")
                .CreateInstance();

            IMinamoOperationResult execution = session.ExecuteFile(validPath);
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
            IMinamoOperationResult dispatch = session.DispatchSignals();
            Assert(dispatch.Success, "signal result uses common operation contract");
            AssertEqual(0, dispatch.Failures.Count, "signal operation failures");

            var missing = session.ExecuteFile(Path.Combine(root, "missing.nami"));
            Assert(missing.Failure is
                { Kind: MinamoFailureKind.Input, Exception: FileNotFoundException },
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
        using (var automatic = new MinamoHost()
            .Module("mode", module => module.Command("Protected", null, "mode.use", _ => 1))
            .CreateInstance())
        {
            Assert(automatic.Environment.Capabilities.IsUnrestricted,
                "automatic mode without allow-list is unrestricted");
            Success(automatic, "import mode\nassert(1, mode.Protected())");
        }

        using (var restricted = new MinamoHost(new()
        {
            CapabilityMode = MinamoCapabilityMode.Restricted
        })
            .Module("mode", module => module.Command("Protected", null, "mode.use", _ => 1))
            .CreateInstance())
        {
            Assert(!restricted.Environment.Capabilities.IsUnrestricted,
                "explicit restricted mode");
            Failure(restricted, "import mode\nmode.Protected()");
        }

        using (var unrestricted = new MinamoHost(new()
        {
            CapabilityMode = MinamoCapabilityMode.Unrestricted
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
        using var session = new MinamoHost()
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
        using var session = new MinamoHost()
            .AddResourceType<TransientCounterResource>()
            .Module("factory", module => module.Command("Create", context =>
                context.Resource(new TransientCounterResource(
                    context.Argument<int>("value"))),
                MinamoCommandParameter.Required<int>("value")))
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
        var host = new MinamoHost()
            .AddCapabilities("counter.read")
            .AddResourceType<CounterResource>()
            .Module("factory", module => module.Command(
                "Create",
                context => context.Resource(
                    new CounterResource(context.Argument<int>("value"))),
                MinamoCommandParameter.Required<int>("value")));

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

        using var unregistered = new MinamoHost()
            .Module("factory", module => module.Command(
                "Create",
                context => context.Resource(new CounterResource(1))))
            .CreateInstance();
        Failure(unregistered, "import factory\nfactory.Create()");
    }

    internal static void ResourceReleaseCallbacks()
    {
        var released = new List<string>();
        var host = new MinamoHost()
            .AddResourceType<TransientReleaseResource>()
            .Module("factory", module => module.Command(
                "Create",
                context => context.Resource(
                    new TransientReleaseResource(
                        context.Argument<string>("name"),
                        released)),
                MinamoCommandParameter.Required<string>("name")));

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
        var failingHost = new MinamoHost()
            .AddResourceType<FailingReleaseResource>()
            .Module("factory", module => module.Command(
                "Create",
                context => context.Resource(
                    new FailingReleaseResource(
                        context.Argument<string>("name"),
                        attempted)),
                MinamoCommandParameter.Required<string>("name")));
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
        var host = new MinamoHost()
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
        using var session = new MinamoHost()
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
        AssertEqual(MinamoStateOwner.Host, session.Environment.State.GetOwner("fromHost")!.Value,
            "host-owned state owner");
        session.Environment.State.SetScript("fromScriptHost", 11);
        AssertEqual(MinamoStateOwner.Script, session.Environment.State.GetOwner("fromScriptHost")!.Value,
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
        using var session = new MinamoHost()
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
        var initialLogs = new List<MinamoLogEntry>();
        var host = new MinamoHost(new()
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
        using var session = new MinamoHost()
            .AddCapabilities("state.read")
            .CreateInstance();

        session.Environment.State.Set("value", 3);
        Success(session, "assert(3, host.State[\"value\"])");
        Failure(session, "host.State[\"value\"] = 4");
    }

    internal static void Logs()
    {
        var logs = new List<MinamoLogEntry>();
        var handledLogs = new List<MinamoLogEntry>();
        using var session = new MinamoHost(new()
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
                    MinamoLogLevel.Warning,
                    "host command",
                    new Dictionary<string, object?> { ["step"] = 2 });
                return context.ExecutionId.ToString();
            }))
            .CreateInstance();

        var execution = session.Execute("""
            import work
            host.Log.Debug("debug")
            host.Log.Info("script", (source: "minamo", count: 2))
            host.Log.Error("error")
            work.Run()
            """);
        Assert(execution.Success, "telemetry execution");
        Assert(execution.ExecutionId != Guid.Empty, "execution correlation ID");
        AssertEqual(4, logs.Count, "log count");
        AssertEqual(logs.Count, handledLogs.Count, "multiple log handlers");
        Assert(logs.All(log => log.ExecutionId == execution.ExecutionId), "log correlation IDs");
        AssertEqual("minamo", (string)logs[1].Properties["source"]!,
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

        using var denied = new MinamoHost(new()
        {
            Log = logs.Add
        })
            .AddCapabilities("other")
            .CreateInstance();
        Failure(denied, "host.Log.Info(\"denied\")");
    }

    internal static void TelemetryExecutionContextIsolation()
    {
        var logs = new ConcurrentQueue<MinamoLogEntry>();
        using var commandStarted = new ManualResetEventSlim();
        using var releaseCommand = new ManualResetEventSlim();
        using var session = new MinamoHost(new()
        {
            Log = logs.Enqueue
        })
            .Module("telemetry", module => module.Command("Wait", context =>
            {
                commandStarted.Set();
                releaseCommand.Wait();
                context.Log(MinamoLogLevel.Info, "command");
                return null;
            }))
            .CreateInstance();

        var execution = Task.Run(() =>
            session.Execute("import telemetry\ntelemetry.Wait()"));
        Assert(commandStarted.Wait(TimeSpan.FromSeconds(5)), "telemetry command entered");

        session.Environment.Telemetry.Write(MinamoLogLevel.Info, "external");
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
        using (var session = new MinamoHost(new()
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
            AssertLimit(result, MinamoExecutionLimitKind.Instructions);
            AssertEqual(100L, result.Metrics.Instructions, "instruction metrics");
        }

        using (var session = new MinamoHost(new()
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
            AssertLimit(result, MinamoExecutionLimitKind.HostCommands);
        }

        using (var session = new MinamoHost(new()
        {
            Limits = new() { MaxCallDepth = 5 }
        })
            .CreateInstance())
        {
            var result = FailureResult(session, """
                func recurse(value) => value == 0 ? 0 : 1 + recurse(value - 1)
                recurse(20)
                """);
            AssertLimit(result, MinamoExecutionLimitKind.CallDepth);
        }

        var timeProvider = new ManualTimeProvider();
        using (var commandStarted = new ManualResetEventSlim())
        using (var session = new MinamoHost(new()
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
            AssertLimit(result, MinamoExecutionLimitKind.Time);
        }

        using (var cancellation = new CancellationTokenSource())
        using (var session = new MinamoHost().CreateInstance())
        {
            cancellation.Cancel();
            var result = FailureResult(session, "1", cancellation.Token);
            Assert(result.Failure is
                { Kind: MinamoFailureKind.Cancelled, Exception: OperationCanceledException },
                "execution cancellation");
        }

        using (var cancellation = new CancellationTokenSource())
        using (var session = new MinamoHost()
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

        using (var session = new MinamoHost(new()
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
                { Kind: MinamoFailureKind.Limit, Limit: MinamoExecutionLimitKind.Signals },
                "signal limit error");
            AssertEqual(1, first.Metrics.Signals, "signal metrics");
            AssertEqual(1, session.DispatchSignals().Delivered, "remaining signal delivery");
        }

        using (var removedEval = new MinamoHost().CreateInstance())
        {
            Failure(removedEval, "eval(\"1 + 1\")");
        }

    }

    internal static void ResultContracts()
    {
        using var session = new MinamoHost()
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
        Assert(compilation.Failure?.Kind == MinamoFailureKind.Compilation,
            "compilation failure kind");
        Assert(compilation.Diagnostics.Any(diagnostic =>
            diagnostic.Severity == MinamoDiagnosticSeverity.Error),
            "compilation diagnostics");

        var runtime = FailureResult(session, "throw Exception<Error>(\"failure\")");
        Assert(runtime.Failure?.Kind == MinamoFailureKind.Runtime, "runtime failure kind");

        var logs = new List<MinamoLogEntry>();
        using var commandFailureSession = new MinamoHost(new()
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
        Assert(logs.Any(log => log.Level == MinamoLogLevel.Error
            && Equals(log.Properties["exceptionMessage"], "sensitive host detail")),
            "host command failure logs exception details for the host");

        using var hostFailureSession = new MinamoHost()
            .Module("broken", module => module.Unit(
                () => throw new InvalidOperationException("module factory failure")))
            .CreateInstance();
        var host = FailureResult(hostFailureSession, "import broken");
        Assert(host.Failure?.Kind == MinamoFailureKind.Host, "host failure kind");
        Assert(host.Failure?.Exception is InvalidOperationException,
            $"host failure preserves exception (actual: "
            + $"{host.Failure?.Exception?.GetType().FullName ?? "<null>"})");
        Success(hostFailureSession, "1 + 1");

        using var signalSession = new MinamoHost().AddSignal("failed").CreateInstance();
        var subscription = signalSession.Environment.Signals.Subscribe(
            "failed",
            _ => throw new InvalidOperationException("host signal failure"));
        signalSession.Environment.Signals.Emit("failed", 1);
        var dispatch = signalSession.DispatchSignals();
        Assert(dispatch.Failures.Single().Kind == MinamoFailureKind.Host,
            "host signal failure kind");
        Assert(signalSession.Environment.Signals.Unsubscribe(subscription),
            "host signal subscription cleanup");
    }

    internal static void Tracing()
    {
        var traces = new List<MinamoTraceEvent>();
        var handledTraces = new List<MinamoTraceEvent>();
        using var session = new MinamoHost(new()
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
        Assert(traces.Any(trace => trace.Kind == MinamoTraceKind.ExecutionStarted),
            "execution started trace");
        Assert(traces.Any(trace => trace.Kind == MinamoTraceKind.ExecutionCompleted),
            "execution completed trace");
        Assert(traces.Any(trace => trace.Kind == MinamoTraceKind.Compilation), "compilation trace");
        Assert(traces.Any(trace => trace.Kind == MinamoTraceKind.VmExecution), "VM trace");
        Assert(traces.Any(trace => trace.Kind == MinamoTraceKind.HostCommand
            && trace.Name == "Create" && trace.Duration is not null), "host command trace");
        Assert(traces.Any(trace => trace.Kind == MinamoTraceKind.ResourceCreated),
            "resource creation trace");
        Assert(traces.Any(trace => trace.Kind == MinamoTraceKind.ResourceReleased),
            "resource release trace");

        Failure(session, "observe.Secret()");
        Assert(traces.Any(trace => trace.Kind == MinamoTraceKind.CapabilityDenied
            && trace.Name == "secret"), "capability denial trace");

        session.Environment.Signals.Emit("tick", 1);
        var dispatch = session.DispatchSignals();
        Assert(dispatch.Success, "traced signal dispatch");
        Assert(traces.Any(trace => trace.Kind == MinamoTraceKind.SignalEmitted
            && trace.Name == "tick"), "signal emitted trace");
        Assert(traces.Any(trace => trace.Kind == MinamoTraceKind.SignalDelivered
            && trace.ExecutionId == dispatch.ExecutionId), "signal delivered trace");
        var dispatchCompleted = traces.Single(trace =>
            trace.Kind == MinamoTraceKind.ExecutionCompleted
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
            trace.Kind == MinamoTraceKind.ExecutionCompleted
            && trace.ExecutionId == failedDispatch.ExecutionId);
        Assert(Equals(failedDispatchCompleted.Data["success"], false),
            "failed signal dispatch completion success");
        Assert(session.Environment.Signals.Unsubscribe(failedSubscription),
            "failed traced signal subscription cleanup");

        var tracesAfterFailure = new List<MinamoTraceEvent>();
        Action<MinamoTraceEvent> traceHandlers =
            _ => throw new InvalidOperationException("ignored trace failure");
        traceHandlers += tracesAfterFailure.Add;
        using var ignoredTraceFailure = new MinamoHost(new()
        {
            Trace = traceHandlers
        })
            .CreateInstance();
        Success(ignoredTraceFailure, "1 + 1");
        Assert(tracesAfterFailure.Count > 0, "trace handler continues after failure");
    }

    private static void Success(MinamoInstance session, string source)
    {
        var result = session.Execute(source);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Hosting API test failed: " + Describe(result));
        }
    }

    private static void Failure(MinamoInstance session, string source)
    {
        var result = FailureResult(session, source);
        if (result.Success)
        {
            throw new InvalidOperationException("Hosting API test expected execution to fail.");
        }
    }

    private static MinamoExecutionResult FailureResult(
        MinamoInstance session,
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

    private static void AssertLimit(MinamoExecutionResult result, MinamoExecutionLimitKind expected)
    {
        if (result.Failure is not
            { Kind: MinamoFailureKind.Limit, Limit: var actual } || actual != expected)
        {
            throw new InvalidOperationException(
                $"Expected {expected} execution limit, got {result.Failure?.Kind}: "
                + result.Failure?.Message);
        }
    }

    private static string Describe(MinamoExecutionResult result) =>
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

    [MinamoResource("Counter")]
    private sealed class CounterResource(int value) : MinamoResource
    {
        private int current = value;

        [MinamoCommand(Description = "Reads the current value.", Capability = "counter.read")]
        public int Value() => current;

        [MinamoCommand(Description = "Reads the current value asynchronously.", Capability = "counter.read")]
        public async ValueTask<int> AsyncValue()
        {
            await Task.Yield();
            return current;
        }

        [MinamoCommand(Description = "Adds to the current value.", Capability = "counter.write")]
        public int Add(int amount) => current += amount;

        public string Hidden() => "not exposed";
    }

    private sealed class UnattributedResource : MinamoResource { }

    [MinamoResource("Duplicate")]
    private sealed class DuplicateCommandResource : MinamoResource
    {
        [MinamoCommand("Same")]
        public void First() { }

        [MinamoCommand("Same")]
        public void Second() { }
    }

    [MinamoResource("Counter", Lifetime = MinamoResourceLifetime.Transient)]
    private sealed class TransientCounterResource(int value) : MinamoResource
    {
        private int current = value;

        [MinamoCommand]
        public int Value() => current;

        [MinamoCommand]
        public int Add(int amount) => current += amount;
    }

    [MinamoResource("Counter", Lifetime = MinamoResourceLifetime.Transient)]
    private sealed class TracingCounterResource(int value) : MinamoResource
    {
        [MinamoCommand]
        public int Value() => value;
    }

    [MinamoResource("ReleaseProbe", Lifetime = MinamoResourceLifetime.Transient)]
    private sealed class TransientReleaseResource(
        string name,
        ICollection<string> released) : MinamoResource
    {
        [MinamoCommand]
        public string Name() => name;

        protected override void OnRelease() => released.Add(name);
    }

    [MinamoResource("FailingProbe", Lifetime = MinamoResourceLifetime.Transient)]
    private sealed class FailingReleaseResource(
        string name,
        ICollection<string> attempted) : MinamoResource
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

    [MinamoResource("SharedProbe")]
    private sealed class SharedReleaseResource(
        string name,
        Action released) : MinamoResource
    {
        [MinamoCommand]
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
