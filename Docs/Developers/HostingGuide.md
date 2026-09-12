# Hosting API guide

The Hosting API exposes application commands to Minamo through ordinary C# APIs and attributes.
It is part of `Minamo.dll` and uses the `Minamo.Hosting` namespace.

## API boundary

`Minamo.Hosting` is the application-facing API. Parser, compiler, linker, and runtime APIs are
separate tooling and extension surfaces; see [Public API layers](PublicApiLayers.md).

## Reading guide

The common hosting flow is:

1. Create a `MinamoHost`.
2. Configure runtime policy and register modules, resources, signals, and capabilities.
3. Create a `MinamoInstance`.
4. Execute scripts and dispatch queued signals at host-chosen safe points.
5. Dispose the instance when its host scope ends.

## Concept map

The Hosting API uses a small set of names consistently on the C# side and the Minamo side:

| Concept | C# setup or access | Minamo access | Purpose |
| --- | --- | --- | --- |
| Module commands | `host.Module(...)` | `import module` | Named command groups exposed by the host |
| Resources | `host.AddResourceType<T>()`, `context.Resource(...)` | Returned handles | Instance-scoped opaque CLR objects |
| State | `Environment.State.Set/SetScript` | `host.State` | Instance memory with host-owned and script-owned keys |
| Signals | `host.AddSignal(...)`, `Environment.Signals` | `host.Signals` | Queued events delivered by `DispatchSignalsAsync()` |
| Input and output | `MinamoEnvironment.UseInputAsync/UseOutput` | `readLine` (`readline` library), `print` | Instance-local text I/O selected by the host |
| Capabilities | `host.AddCapabilities(...)`, `Environment.Capabilities` | None | Host-owned allow-list for protected features |
| Logging | `MinamoHostOptions.Log` | `host.Log` | User-facing structured log events |
| Tracing | `MinamoHostOptions.Trace` | None | Observational diagnostics for the embedding host |
| Limits | `MinamoHostOptions.Limits` | None | Per-operation execution guards |

Module commands expose operations, resources preserve object identity and lifetime, state stores
instance data, and signals defer events until `DispatchSignalsAsync()`.

## Host Setup

```csharp
using Minamo.Hosting;

var host = new MinamoHost(new MinamoHostOptions
{
    Limits = new() { MaxInstructions = 100_000 },
    Log = entry => Console.WriteLine(entry.Message)
});
```

Configure the host before creating instances. The examples below add individual features to this
same `host`; they are not intended to be concatenated verbatim.

```csharp
host.Module("game", module => module.Command(
    "spawn",
    "Creates an entity from a prefab.",
    context => context.Host<Game>().Spawn(
        context.Argument<string>("prefab")),
    MinamoCommandParameter.Required<string>("prefab")));
```

The instance host object is supplied separately. This allows the same command definitions to be
used with different game instances or test doubles.

```csharp
var instance = host.CreateInstance(game);
var result = await instance.ExecuteAsync("import game\ngame.spawn(\"boss\")");

if (!result.Success)
    Console.WriteLine(result.Failure?.Message
        ?? string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
```

`CreateInstance` snapshots the current host configuration. Registrations added afterward are
available only to instances created after those registrations. Configure a `MinamoHost` before sharing
it between threads; concurrent configuration and instance creation are not supported.

## Results and failures

`ExecuteAsync` and `ExecuteFileAsync` return a `MinamoExecutionResult` instead of throwing for script
compilation errors, runtime errors, input failures, cancellation, and execution limits. Inspect
`Failure.Kind` to distinguish `Compilation`, `Runtime`, `Host`, `Input`, `Cancelled`, and `Limit`;
`Failure.Limit` identifies the exceeded limit. `Diagnostics`
contains structured compiler messages with severity, code, source location, and text. The optional
`Failure.Exception` preserves the exception reported at the operation boundary for logging and
detailed diagnostics. Exceptions thrown by registered host commands are deliberately sanitized
before they cross into the script. Their CLR type and original message are written to host telemetry
at `Error` level, while the script and `MinamoExecutionResult` receive only the sanitized runtime
failure.

`MinamoExecutionResult` and `MinamoSignalDispatchResult` both implement
`IMinamoOperationResult`. Generic host reporting can use its `Success`, `Failures`,
`ExecutionId`, and `Metrics` members while operation-specific code can still inspect diagnostics,
the returned value, or the delivered signal count.

Use `GetValue<T>()` to convert a successful execution value to a CLR type, or
`TryGetValue<T>()` when conversion may not be available. A Minamo `nil` converts to
`default(T)`. `TryGetValue<T>()` returns `false` when an operation has no value or the value cannot
be converted; `GetValue<T>()` throws in those cases. Hosting results deliberately do not expose the
raw runtime value.

Invalid Hosting API usage, such as a duplicate registration or an invalid argument, still throws
a normal C# exception immediately.

Commands can return either CLR values supported by `TypeConverter` or an existing `MinamoObject`.
Parameters are converted to their declared CLR types before the handler uses them.

Commands may accept Minamo callbacks through `MinamoCommandContext.Callback(...)`,
`CallbackAction(...)`, or `CallbackTuple(...)`. Arguments and results use the normal CLR conversion
rules. A callback is valid only for the lifetime of the host command, including the awaited lifetime
of an asynchronous command, and must not be retained for detached work.

For returned CLR objects, the declared C# return type is the exposure boundary; runtime types do not
reveal additional members. Use a resource wrapper when the script needs a deliberately broader API.
Generated commands support `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>`; manual registration
provides `AsyncCommand(...)`. Minamo suspends the VM while the CLR awaitable completes without adding
language-level `async` or `await` syntax.

Module, signal, resource, and capability names use dotted identifier segments. Commands,
static host types, and parameters use single identifiers. Invalid names are rejected during host
configuration. Group static host commands with `module.Type(...)`.

## Instances

`MinamoInstance` is incremental. Definitions created by one successful submission remain available to
later submissions. A failed submission does not commit its compiled definitions or script signal
subscriptions. Host-command side effects, emitted signals, and writes to shared host state are not
transactional and are not rolled back.

```csharp
await instance.ExecuteAsync("let boss = game.spawn(\"boss\")");
await instance.ExecuteAsync("game.teleport(boss, 100, 20)");
```

`Reset()` discards compiled definitions, state, script subscriptions, queued signals, and transient
resource handles while preserving snapshotted host registrations, C# signal subscriptions, and
shared resource handles. Dispose every instance when its host scope ends; disposal invalidates its
resource handles but does not dispose the borrowed host context.

For repeated execution of the same code across multiple actors or players, compile once into a
`MinamoProgram` and create separate instances from it:

```csharp
var compiled = host.Compile("""
    let current = if host.State["runs"] is nil { 0 } else { host.State["runs"] }
    host.State["runs"] = current + 1
    host.State["runs"]
    """);

var program = compiled.GetValueOrThrow();

using var first = host.CreateInstance(program);
using var second = host.CreateInstance(program);

var firstRun = await first.ExecuteAsync();
var secondRun = await second.ExecuteAsync();
```

`MinamoProgram` contains compiled code and diagnostics and can be shared. Each
`MinamoInstance` combines a program, a `MinamoEnvironment`, and mutable execution state such as
runtime variables, state, signals, and resource handles. Each `ExecuteAsync` or `DispatchSignalsAsync` call
creates a `MinamoExecution` with its own correlation ID and metrics.

A program is bound to the `MinamoHost` that compiled it because its compiled module references
and host policy come from that host. It can be shared by instances created from the same host, but
passing it to a different host is rejected.

`MinamoEnvironment.Expose(...)` makes C# values visible as bare names for that instance. This is
useful for actor-style scripts where the host chooses what `self`, `world`, or `target` means:

```csharp
var program = host.Compile("self + world").GetValueOrThrow();

using var instance = host.CreateInstance(
    program,
    new MinamoEnvironment(game)
        .Expose("self", 2)
        .Expose("world", 3));
```

Name resolution checks script locals, outer scopes, imports, and built-in types before consulting
the environment. A missing exposed name is a runtime error. Assignment to the same bare name creates
or updates a script binding; it does not write back into the `MinamoEnvironment`.

### Instance input and output

Configure text I/O on the `MinamoEnvironment` when an instance needs isolated input or output:

```csharp
var output = new StringBuilder();
var environment = new MinamoEnvironment(game)
    .UseInputAsync(async cancellationToken => await commandQueue.ReadAsync(cancellationToken))
    .UseOutput(text => output.Append(text));

using var instance = host.CreateInstance(environment);
var result = await instance.ExecuteAsync("print(\"ready\", terminator: nil)");
```

The input delegate receives the operation cancellation token and returns `ValueTask<string?>`;
`null` is exposed to the script as an empty string. The output delegate receives the chunks written
by `print`. When the optional `readline` library is registered, its `readLine` command consumes this
input delegate. Without configured delegates, `readLine` and `print` use the process console.

Use `ExecuteFileAsync("Scripts/startup.nami")` for a host-selected entry script. Imports still obey
the configured `FileLookup`; file I/O failures return an `Input` failure.

Instance operations are asynchronous and serialized. Pending host awaitables suspend the VM without
occupying a worker thread, and concurrent calls wait for the active operation. Interactive selects
follow the same model; see [Interactive selects](InteractiveSelect.md).

## Host environment

A hosted instance provides the global `host` object without an import. Its state belongs to that
instance and is available from C# through `instance.Environment`.

```swift
host.Commands.List()
```

Outside a hosted instance, accessing members of `host` produces a runtime error. Hosted execution
always goes through `MinamoInstance`; parser and compiler-only tooling may still use
`BuilderOptions` directly.

Set `MinamoHostOptions.ExposeHostObject` to `false` when scripts should see only names supplied
through `MinamoEnvironment.Expose(...)`:

```csharp
var host = new MinamoHost(new()
{
    ExposeHostObject = false
});

var program = host.Compile("self.MoveTo(10, 20)").GetValueOrThrow();
var env = new MinamoEnvironment(game).Expose("self", player);
using var instance = host.CreateInstance(program, env);
```

In this mode `host` is not a script-visible name. Accessing `host.State` or `host.Signals`
therefore produces the normal undeclared-variable diagnostic for `host`.

## Capabilities and command catalog

`CapabilityMode` determines when the allow-list is active:

- `Automatic` activates it when `AddCapabilities(...)` registers at least one name.
- `Restricted` activates it even when empty, producing a deny-all starting point.
- `Unrestricted` bypasses it.

Entries may be exact names, `*`, or hierarchical wildcards such as `scene.*`.

```csharp
host.AddCapabilities("scene.read", "audio.*")
    .Module("scene", module => module.Command(
        "Delete",
        "Deletes an entity.",
        "scene.write",
        context => context.Host<Game>().Delete(context.Argument<long>("id")),
        MinamoCommandParameter.Required<long>("id")));
```

Commands whose capability is unavailable cannot be invoked and are omitted from the catalog.
Capability policy is available to C# through `instance.Environment.Capabilities`; scripts see
only the commands and signals that the policy makes available.

```swift
host.Commands.Find("scene")
host.Commands.Describe("scene.Delete")
```

### Built-in capabilities

The following names are reserved by Minamo's built-in host APIs:

| Capability | Protected script operations |
| --- | --- |
| `state.read` | `host.State[key]`, `host.State.Keys()`, `Has()`, and `Owner()` |
| `state.write` | Assignment to `host.State[key]`, `Remove()`, and `Clear()` |
| `log.write` | `host.Log.Debug()`, `Info()`, `Warning()`, and `Error()` |

Signal, module, generated, and resource capabilities use names supplied by the host application.
An unavailable command is hidden from `host.Commands`; other denied operations produce a runtime
error and, when tracing is enabled, a `CapabilityDenied` event.

## Resource handles

Resources use explicit wrapper classes. The wrapped domain object remains private, and only methods
marked with `[MinamoCommand]` are exposed to scripts.

Derive a wrapper from `MinamoResource` and give it a script-visible name:

```csharp
[MinamoResource("Player")]
public sealed class PlayerResource : MinamoResource
{
    private readonly Player player;

    public PlayerResource(Player player) => this.player = player;

    [MinamoCommand]
    public string Name() => player.Name;

    [MinamoCommand(Capability = "scene.write")]
    public void MoveTo(double x, double y) => player.MoveTo(x, y);

    protected override void OnRelease() => player.CloseSessionView();
}
```

Register only the wrapper type when configuring the host:

```csharp
host.AddResourceType<PlayerResource>();
```

Commands can then create handles:

```csharp
return context.Resource(new PlayerResource(player));
```

Registered operations appear in the command catalog under names such as
`resource.Player.Name` and `resource.Player.MoveTo`. Catalog visibility respects each operation's
capability. A wrapper type can have one registered resource definition per host. Public methods
without `[MinamoCommand]` are not exposed.

Registered resource wrappers are shared by default. Repeatedly exposing the same wrapper instance
returns the same instance handle. Shared handles do not expose `Release`, survive `Reset()`, and are
invalidated when the instance is disposed. `OnRelease` runs once at instance disposal.

Use `[MinamoResource("TemporaryFile", Lifetime = MinamoResourceLifetime.Transient)]` when the
script should own and explicitly release a resource. Transient resources receive a new handle on
every exposure; `OnRelease` runs once when the handle is released, reset, or disposed.

In every lifetime, the handle is invalidated before `OnRelease` runs. Failures from bulk cleanup
are collected so every callback is attempted, then reported as an `AggregateException`. The
instance host context remains a borrowed object and does not participate in resource release.

Transient handles are invalidated by `Release()`, `Reset()`, or instance disposal. Shared handles
remain valid through `Reset()` and cannot be released by the script. No handle can be transferred
between instances.

## Shared state

`State` is an instance-scoped string-keyed store shared by C# and Minamo. Each key is owned either
by the host or by the script. Missing keys return `nil`.

```swift
host.State["selectedPlayer"] = "player-1"
print(host.State["selectedPlayer"])
host.State.Has("selectedPlayer")
host.State.Owner("selectedPlayer")
host.State.Keys()
host.State.Remove("selectedPlayer")
```

```csharp
instance.Environment.State.Set("session.name", "Debug Console");
instance.Environment.State.SetScript("selectedPlayer", "player-1");
var selected = instance.Environment.State.Get<string>("selectedPlayer");
if (instance.Environment.State.TryGet<string>("selectedPlayer", out var current))
    Console.WriteLine(current);
```

`TryGet<T>()` returns `false` when the key is absent or its value cannot be converted. A stored
Minamo `nil` is present and returns `true` with `default(T)`. Values are converted with the same
CLR conversion rules used by host command arguments.

`Set` creates or updates host-owned state. Minamo can read host-owned keys but cannot overwrite or
remove them. `SetScript` creates or updates script-owned state; Minamo and C# can both edit those
keys. Minamo assignment creates script-owned state for new keys. `Remove` returns `false` and does
nothing for host-owned keys, and `Clear` removes only script-owned keys from Minamo. C# `Remove`
and `Clear` still manage the whole store.

Minamo reads require `state.read`; writes, removals, and script clearing require `state.write`.
In `Automatic` mode these checks are inactive when the host has no explicit allow-list;
`Restricted` mode enforces them even when the list is empty, and `Unrestricted` mode bypasses them.
`Reset()` clears instance state.

## Signals

Signals must be declared by the host. Listen and emit capabilities can be controlled separately.

```csharp
host.AddCapabilities("player.*")
    .AddSignal(
        "player.hit",
        listenCapability: "player.listen",
        emitCapability: "player.emit");
```

Minamo subscriptions return an ID used by `Off`. `Once` removes its subscription before the first
callback is invoked.

```swift
func onHit(damage) {
    host.State["lastDamage"] = damage
}

let subscription = host.Signals.On("player.hit", onHit)
host.Signals.Once("player.hit", damage => print(damage))
host.Signals.Off(subscription)
host.Signals.Emit("player.hit", 10)
```

Both C# and Minamo emission enqueue a signal. Delivery is explicit and never re-enters a running
VM execution:

```csharp
instance.Environment.Signals.Emit("player.hit", 10);
var dispatch = await instance.DispatchSignalsAsync();

if (!dispatch.Success)
    foreach (var failure in dispatch.Failures)
        Console.Error.WriteLine(failure.Message);
```

C# observers use `Subscribe` and `Unsubscribe`; payloads can be read with `GetPayload<T>()` or
`TryGetPayload<T>()`.

Pending queues are unbounded by default. Configure `Signals.MaxPending` when producers can outpace
dispatch. `TryEmit` returns `false` when the queue is full, while `Emit` throws on the C# side and
produces a runtime failure in Minamo. `PendingCount` reports the current queue length.

`DispatchSignalsAsync()` processes only signals that were queued when dispatch began. Signals emitted
by a callback remain queued until the next call. `Reset()` removes Minamo subscriptions and queued
signals while preserving C# subscriptions. Instance disposal removes all subscriptions.

## Structured logs

Logging is synchronous and uses a host-provided delegate. No task or scheduler is created by this
API.

```csharp
var host = new MinamoHost(new()
{
    Log = entry =>
        Console.WriteLine($"[{entry.Level}] {entry.Message}")
});
host.AddCapabilities("log.write");
```

Minamo exposes four log levels and optional structured properties. A tuple or dictionary is
converted to a case-insensitive property map.

```swift
host.Log.Debug("loading started")
host.Log.Info("selected player", (id: "player-1", source: "console"))
host.Log.Warning("health is low", ["health": 5])
host.Log.Error("command failed")
```

Logs require `log.write`.

Host commands write to the same sink through `MinamoCommandContext.Log(...)`.

### Log payload

The `Log` delegate in `MinamoHostOptions` receives an immutable record object:

| `MinamoLogEntry` member | Meaning |
| --- | --- |
| `Timestamp` | UTC time at which `MinamoTelemetry.Write` created the entry |
| `Level` | `Debug`, `Info`, `Warning`, or `Error` |
| `Message` | Log message supplied by the script or host command |
| `Properties` | Case-insensitive, read-only structured property map; empty when omitted |
| `ExecutionId` | Correlation ID of the current `ExecuteAsync` or `DispatchSignalsAsync` operation |
| `Command` | Unqualified host-command name while that command is executing; otherwise `null` |

Each execution or signal dispatch receives a correlation ID. Outside an active operation,
`instance.Environment.Telemetry.Write(...)` uses `Guid.Empty`. Log-handler exceptions become
runtime or host-command failures.

## Execution limits

Limits are configured once on `MinamoHost` and applied independently to every `ExecuteAsync` and
`DispatchSignalsAsync` operation.

```csharp
var host = new MinamoHost(new()
{
    Limits = new()
    {
        MaxInstructions = 100_000,
        MaxExecutionTime = TimeSpan.FromMilliseconds(50),
        MaxHostCommands = 100,
        MaxSignals = 32,
        MaxCallDepth = 64
    }
});
```

Every limit is optional. Leave a property as `null` to make that dimension unlimited. For example,
omit `MaxExecutionTime` to allow a long-running operation, or omit `MaxHostCommands` to allow any
number of host command calls while still limiting instructions or call depth.

An exceeded limit returns a `MinamoFailure` whose `Kind` is `Limit`. Its `Limit` identifies
`Instructions`, `Time`, `HostCommands`, `Signals`, or `CallDepth`. Instruction,
command, and Signal counters contain completed work; an operation rejected by a limit is not added
to the corresponding counter.

Cancellation is supplied per operation:

```csharp
var result = await instance.ExecuteAsync(source, cancellationToken);
var dispatch = await instance.DispatchSignalsAsync(cancellationToken);
```

Host commands receive a combined token through `MinamoCommandContext.CancellationToken`. It is
cancelled by either the operation token or `MaxExecutionTime`, so a command that performs long-running
C# work should observe it itself. The VM checks cancellation and time periodically while executing
bytecode and again when a host command returns. .NET does not provide a safe way to forcibly stop a
synchronous handler that ignores cancellation.

`MinamoExecutionResult.Metrics` and `MinamoSignalDispatchResult.Metrics` contain total, compilation, and VM
durations plus instruction, host-command, and Signal counts. Instruction counting is enabled when
limits, tracing, or a cancellable token are active; otherwise it remains zero to avoid adding work
to unrestricted instances.

## Execution tracing

Tracing is opt-in and independent from user-facing logs. It records execution phases and host
boundaries without changing script behavior.

```csharp
var traces = new List<MinamoTraceEvent>();
var host = new MinamoHost(new() { Trace = traces.Add });
```

`MinamoTraceEvent` contains:

| Member | Meaning |
| --- | --- |
| `Timestamp` | UTC time at which the event was created |
| `Kind` | Event category from `MinamoTraceKind` |
| `ExecutionId` | Correlation ID of the current operation |
| `Name` | Operation, command, capability, signal, or resource type associated with the event |
| `Duration` | Elapsed time for completed execution phases and host commands; otherwise `null` |
| `Data` | Case-insensitive, read-only structured details; empty when the event has no details |

The contents of the optional fields depend on `Kind`:

| Kind | `Name` | `Duration` | `Data` |
| --- | --- | --- | --- |
| `ExecutionStarted` | Operation name | — | — |
| `ExecutionCompleted` | Operation name | Total operation time | `success`: whether execution completed successfully; signal dispatch also includes `delivered` |
| `Compilation` | — | Compilation time | — |
| `VmExecution` | — | VM execution time | — |
| `HostCommand` | Unqualified command name | Command execution time | — |
| `CapabilityDenied` | Denied capability name | — | — |
| `SignalEmitted` | Signal name | — | — |
| `SignalDelivered` | Signal name | — | — |
| `ResourceCreated` | Resource type name | — | `id`: resource handle ID |
| `ResourceReleased` | Resource type name | — | `id`: resource handle ID |

Events emitted by C# outside `ExecuteAsync` or `DispatchSignalsAsync` use `Guid.Empty` for `ExecutionId`.
`Trace` accepts an `Action<MinamoTraceEvent>`. Unlike log handlers, trace handler exceptions are
ignored because tracing is observational and must not alter script results.

## Complete example

The [Station Console example](../../Examples/StationConsole/README.md) combines generated commands,
resources, capabilities, state, signals, limits, and logging in a runnable host. Signal delivery is
explicitly driven by that host at a safe point through `DispatchSignalsAsync(...)`.

## Security default

When no `FileLookup` is supplied, a host instance does not search the current directory, system
directories, or additional library paths. Only registered host modules and the built-in `lang`
module are available. Call `DisableFileImports()` when a host should stay restricted even if a
lookup was configured earlier. Supply an explicit `FileLookup` when script file imports are
intended.

```csharp
using Minamo.Compiler;
using Minamo.Hosting;
using Minamo.Linker;

var options = BuilderOptions.Default();
var lookup = FileLookup.Restricted(options)
    .AddStartupPath(Path.Combine(AppContext.BaseDirectory, "scripts"))
    .AddPath(Path.Combine(AppContext.BaseDirectory, "mods"))
    .Build();

var host = new MinamoHost(new() { BuilderOptions = options })
    .UseFileLookup(lookup);
using var instance = host.CreateInstance(game);
```

`FileLookup.Restricted(options)` searches only paths added explicitly with `AddStartupPath`,
`AddPath`, or `AddPaths`. `FileLookup.Standard(options)` also searches relative to the importing
file and paths from `MINAMO_LIBS`. Minamo never searches beside its executable implicitly.
Each configured path is searched exactly as registered; a `lib` child directory is not added
implicitly. Register it with `AddPath` when it is intended to be importable.

Call `DisableFileImports()` after `UseFileLookup(...)` when a restricted console must explicitly
disable file imports.

The command-line host registers its standard modules, including `io`, through this same C# API.

## Generated command bindings

`Minamo.Generators` can generate the `MinamoHost` registration code from ordinary C# methods.

```csharp
using Minamo.Hosting;

[MinamoModule("game")]
public sealed class GameCommands
{
    [MinamoProperty(Description = "Current scene name.", Capability = "scene.read")]
    public string SceneName => game.SceneName;

    [MinamoProperty(Capability = "audio.write")]
    public double Volume
    {
        get => game.Volume;
        set => game.Volume = value;
    }

    [MinamoCommand("spawn", Description = "Creates an entity from a prefab.")]
    public GameObject Spawn(string prefab, bool active = true) { /* ... */ }

    [MinamoCommand]
    public static string Version() => "1.0";
}
```

For an instance module, the generator creates a typed `AddModule` extension method:

```csharp
var commands = new GameCommands();
host.AddModule(commands);
var instance = host.CreateInstance();
```

The module instance is captured by its generated registration, so it does not occupy the instance's
general-purpose host context. This is the preferred form for cohesive application commands with
dependencies. Static modules generate a parameterless registration method such as
`AddGameModule()`.

A `MinamoCommandContext` parameter is injected rather than exposed to Minamo. C# optional
parameter values and the `Description` property are copied into command metadata.

`MinamoProperty` exposes an ordinary C# property as a module property:

```swift
print(game.SceneName)
game.Volume = 0.5
```

A getter is required. Omitting the C# setter makes the Minamo property read-only. The declared
capability protects both reads and writes, and the property appears once in the command catalog;
its generated setter is an internal implementation detail. Use properties for live values and
lightweight settings. Keep operations with substantial side effects as `MinamoCommand` methods.

When using project references, add the generator as an analyzer:

```xml
<ProjectReference Include="..\Minamo.Generators\Minamo.Generators.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Unsupported generic methods, `ref`/`out` parameters, `params` arrays, inaccessible methods, and
duplicate command names are reported as compiler diagnostics.

Set `MinamoCommand.Type` to group commands under a static host type. Advanced extensions can register
`MinamoForeignTypeInfo` implementations with `MinamoForeignType`, or register a specialized
`ForeignUnit` by applying `MinamoModule` to it. A module backed by `ForeignUnit` cannot also contain
generated commands or foreign-type declarations. Foreign-type members use `MinamoType`,
`MinamoMethod`, `MinamoProperty`, `MinamoStaticMethod`, and `MinamoStaticProperty`; operators and
conversions remain explicit runtime overrides. See [Public API layers](PublicApiLayers.md) for the
boundary between hosting and advanced extension APIs.

## External extension libraries

The `minamo` executable can load optional extension assemblies listed in its
`minamo.json` file. This is a command-line distribution feature; embedding
applications choose their own module registrations and do not inherit those
extensions automatically.

An extension assembly exposes one or more public `[MinamoModule]` types. The
Minamo source generator produces the registration code used by `minamo`:

```csharp
using Minamo.Hosting;

namespace MyExtension;

[MinamoModule("example")]
public static class ExampleModule
{
    [MinamoCommand("hello")]
    public static string Hello() => "Hello from an extension."
}
```

`minamo.json` contains an `extensions` array of assembly paths. Each path may
be absolute or relative to the directory containing the configuration file:

```json
{
  "extensions": [
    "MyExtension.dll"
  ]
}
```

The default `minamo.json` has an empty `extensions` array. Add paths to extension
assemblies to this array as needed.

`minamo` loads each assembly and registers its generated modules automatically.
Extension module types must be public and either static, derive from
`ForeignUnit`, or have a public parameterless constructor. An extension should
reference `Minamo.dll`, not `minamo.exe`; no executable-specific interface is
required.
