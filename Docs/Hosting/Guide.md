# Hosting API guide

The Hosting API exposes application commands to Spellkit through ordinary C# APIs and attributes.
It is part of `Spellkit.dll` and uses the `Spellkit.Hosting` namespace.

## API boundary

`Spellkit.Hosting` is the primary application-facing API. The runtime object and foreign-type APIs
under `Spellkit.Runtime` and `Spellkit.Linker.ForeignUnit` form the advanced extension API used by
custom Spellkit types. Parser, compiler, linker, debugger, bytecode, and VM implementation details
are not part of the stable Hosting contract even where a low-level type remains public for legacy
integration.

## Reading guide

The common path is:

1. Create a `SpellkitHost`.
2. Configure runtime policy and register modules, resources, signals, and capabilities.
3. Create a `SpellkitInstance`.
4. Execute scripts with `Execute(...)`.
5. Deliver queued signals at safe points with `DispatchSignals(...)`.
6. Dispose the instance when the console or host scope ends.

The later sections follow that order: setup and instances first, then host features, then limits,
tracing, security defaults, and generated bindings.

## Concept map

The Hosting API uses a small set of names consistently on the C# side and the Spellkit side:

| Concept | C# setup or access | Spellkit access | Purpose |
| --- | --- | --- | --- |
| Module commands | `host.Module(...)` | `import module` | Named command groups exposed by the host |
| Resources | `host.AddResourceType<T>()`, `context.Resource(...)` | Returned handles | Instance-scoped opaque CLR objects |
| State | `Environment.State.Set/SetScript` | `host.State` | Instance memory with host-owned and script-owned keys |
| Signals | `host.AddSignal(...)`, `Environment.Signals` | `host.Signals` | Queued events delivered by `DispatchSignals()` |
| Input and output | `SpellkitEnvironment.UseInput/UseOutput` | `readLine` (console library), `print` | Instance-local text I/O selected by the host |
| Capabilities | `host.AddCapabilities(...)`, `Environment.Capabilities` | None | Host-owned allow-list for protected features |
| Logging | `SpellkitHostOptions.Log` | `host.Log` | User-facing structured log events |
| Tracing | `SpellkitHostOptions.Trace` | None | Observational diagnostics for the embedding host |
| Limits | `SpellkitHostOptions.Limits` | None | Per-operation execution guards |

Use module commands for live host operations, resources for objects with identity and lifetime,
and `State` for instance facts or script working memory.
`Signals` are queued and explicit: `DispatchSignals()` dispatches the pending
queue at a host-chosen safe point rather than immediately re-entering a running VM.
Input and output can be selected per hosted instance. When a delegate is not supplied, the builtins
retain their normal process Console behavior for compatibility.

## Host Setup

```csharp
using Spellkit.Hosting;

var host = new SpellkitHost(new SpellkitHostOptions
{
    Limits = new() { MaxInstructions = 100_000 },
    Log = entry => Console.WriteLine(entry.Message)
});
```

`SpellkitHostOptions` contains execution policy and observability settings. Create one
`SpellkitHost`, add the modules and host features needed by the application, and then create
instances from that configured host. The examples below continue configuring this same `host`
variable rather than constructing a new host for every feature. They demonstrate alternative
features and are not intended to be concatenated verbatim; apply the relevant registrations before
creating an instance.

`CapabilityMode` controls how the capability allow-list is activated:

- `Automatic` is the compatibility default: no registered capabilities means unrestricted, while
  registering one or more names with `AddCapabilities(...)` activates the allow-list.
- `Restricted` always activates the allow-list, including an empty list that denies every protected
  operation.
- `Unrestricted` explicitly allows every protected operation. Any registered capability names
  remain visible through the host environment but do not restrict access.

```csharp
host.Module("game", module => module.Command(
    "spawn",
    "Creates an entity from a prefab.",
    context => context.Host<Game>().Spawn(
        context.Argument<string>("prefab")),
    SpellkitCommandParameter.Required<string>("prefab")));
```

The instance host object is supplied separately. This allows the same command definitions to be
used with different game instances or test doubles.

```csharp
var instance = host.CreateInstance(game);
var result = instance.Execute("import game\ngame.spawn(\"boss\")");

if (!result.Success)
    Console.WriteLine(result.Failure?.Message
        ?? string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
```

`CreateInstance` snapshots the current host configuration. Registrations added afterward are
available only to instances created after those registrations. Configure a `SpellkitHost` before sharing
it between threads; concurrent configuration and instance creation are not supported.

`SpellkitHost` borrows the instance host context and telemetry
handlers. Disposing an instance does not dispose those host-owned objects. The instance
owns its `SpellkitHostEnvironment`, state, signal subscriptions, and resource handles. Releasing a handle
or disposing an instance invalidates the handle but does not dispose the CLR object behind it.

Always dispose `SpellkitInstance`. `Reset` keeps the snapshotted host registrations but clears script
state, script signal subscriptions, incremental compilation state, and non-service handles.

## Results and failures

`Execute` and `ExecuteFile` return a `SpellkitExecutionResult` instead of throwing for script
compilation errors, runtime errors, input failures, cancellation, and execution limits. Inspect
`Failure.Kind` to distinguish `Compilation`, `Runtime`, `Input`, `Cancelled`, and `Limit`;
`Failure.Limit` identifies the exceeded limit. `Diagnostics`
contains structured compiler messages with severity, code, source location, and text. The optional
`Failure.Exception` preserves the originating CLR exception for logging and detailed diagnostics.
Exceptions thrown by registered host commands are deliberately sanitized before they cross into the
script. Their CLR type and original message are written to host telemetry at `Error` level, while the
script receives only a generic host-command failure.

`SpellkitExecutionResult` and `SpellkitSignalDispatchResult` both implement
`ISpellkitOperationResult`. Generic host reporting can use its `Success`, `Failures`,
`ExecutionId`, and `Metrics` members while operation-specific code can still inspect diagnostics,
the returned value, or the delivered signal count.

Use `GetValue<T>()` to convert a successful execution value to a CLR type, or
`TryGetValue<T>()` when conversion may not be available. A Spellkit `nil` converts to
`default(T)`. `TryGetValue<T>()` returns `false` when an operation has no value or the value cannot
be converted; `GetValue<T>()` throws in those cases. The raw `Value` remains available for advanced
runtime integrations.

Invalid Hosting API usage, such as a duplicate registration or an invalid argument, still throws
a normal C# exception immediately.

Commands can return either CLR values supported by `TypeConverter` or an existing `SpellkitObject`.
Parameters are converted to their declared CLR types before the handler uses them.

Commands can also accept Spellkit functions as callbacks through the command context:

```csharp
module.Command("Apply", context =>
{
    var callback = context.Callback<long, long>("callback");
    return callback(context.Argument<long>("value"));
},
SpellkitCommandParameter.Required<long>("value"),
SpellkitCommandParameter.Required<object>("callback"));
```

```swift
import callbacks

callbacks.Apply(5, value => value + 2)
```

Use `context.Callback("name").Invoke<T>(...)` when the arity is dynamic, or
`CallbackAction<T>(...)` for callbacks whose return value is ignored. For three or more typed
arguments, use a `ValueTuple` and let `CallbackTuple<TArgs, TResult>(...)` expand it into Spellkit
function arguments:

```csharp
var sum = context.CallbackTuple<(long A, long B, long C, long D), long>("callback");
return sum((1, 2, 3, 4));
```

```swift
callbacks.Sum((a, b, c, d) => a + b + c + d)
```

Callback arguments and return values use the same CLR conversion rules as host command arguments.
Callbacks are valid only while the host command that received them is running. For an asynchronous
command, that lifetime extends until its returned task completes. Do not retain a callback for later
use or invoke it from detached background work after the command has completed.

For other CLR objects, Spellkit exposes members from the command's declared return type. Use a
typed command when the implementation object has additional public members that must remain hidden:

```csharp
module.Command<IPlayerView>("Player", context => context.Host<Game>().Player);
```

The returned object's runtime type is not used to discover additional members. Generated bindings
follow the same rule: their declared C# return type is the boundary. Expose a concrete declared type
or a resource wrapper when more operations are required.

Generated module and resource commands may return `Task`, `Task<T>`, `ValueTask`, or
`ValueTask<T>`. Manual module registration provides `AsyncCommand(...)` for the same purpose.
Spellkit does not add language-level `async` or `await` syntax. Calling one of these commands
suspends the VM at the ordinary call expression; the hosting API waits for the CLR awaitable and
then resumes the same VM continuation.

Host module, service, signal, and resource type names use dotted Spellkit identifier segments
such as `scene`, `scene.player`, or `audio.volume`. Command, static host type, and command
parameter names use a single Spellkit identifier. Capabilities use the same dotted form, plus
hierarchical wildcards such as `scene.*` and the global `*`. Invalid names are rejected when the
host is configured, before an instance is created.

Commands that belong to a static host type can be grouped with `Type`:

```csharp
host.Module("math", module => module.Type("Math", type => type.Command(
    "Abs",
    context => Math.Abs(context.Argument<long>("value")),
    SpellkitCommandParameter.Required<long>("value"))));
```

Spellkit can then use `Math.Abs(...)` after `import * from math`.

## Instances

`SpellkitInstance` is incremental. Definitions created by one successful submission remain available to
later submissions. Failed builds and runtime failures are rolled back.

```csharp
instance.Execute("let boss = game.spawn(\"boss\")");
instance.Execute("game.teleport(boss, 100, 20)");
```

Call `Reset()` to discard compiled definitions and runtime state.

For repeated execution of the same code across multiple actors or players, compile once into a
`SpellkitProgram` and create separate instances from it:

```csharp
var compiled = host.Compile("""
    let current = if host.State["runs"] is nil { 0 } else { host.State["runs"] }
    host.State["runs"] = current + 1
    host.State["runs"]
    """);

var program = compiled.GetValueOrThrow();

using var first = host.CreateInstance(program);
using var second = host.CreateInstance(program);

var firstRun = first.Execute();
var secondRun = second.Execute();
```

`SpellkitProgram` contains compiled code and diagnostics and can be shared. Each
`SpellkitInstance` combines a program, a `SpellkitEnvironment`, and mutable execution state such as
runtime variables, state, signals, and resource handles. Each `Execute` or `DispatchSignals` call
creates a `SpellkitExecution` with its own correlation ID and metrics.

A program is bound to the `SpellkitHost` that compiled it because its compiled module references
and host policy come from that host. It can be shared by instances created from the same host, but
passing it to a different host is rejected.

`SpellkitEnvironment.Expose(...)` makes C# values visible as bare names for that instance. This is
useful for actor-style scripts where the host chooses what `self`, `world`, or `target` means:

```csharp
var program = host.Compile("self + world").GetValueOrThrow();

using var instance = host.CreateInstance(
    program,
    new SpellkitEnvironment(game)
        .Expose("self", 2)
        .Expose("world", 3));
```

```swift
self + world
```

Name resolution checks script locals, outer scopes, imports, and built-in types before consulting
the environment. A missing exposed name is a runtime error. Assignment to the same bare name creates
or updates a script binding; it does not write back into the `SpellkitEnvironment`.

### Instance input and output

Configure text I/O on the `SpellkitEnvironment` when an instance needs isolated input or output:

```csharp
var output = new StringBuilder();
var environment = new SpellkitEnvironment(game)
    .UseInput(cancellationToken => commandQueue.Read(cancellationToken))
    .UseOutput(text => output.Append(text));

using var instance = host.CreateInstance(environment);
var result = instance.Execute("print(\"ready\", terminator: nil)");
```

The input delegate receives the current operation's cancellation token. Returning `null` represents
end of input and produces an empty Spellkit string. The optional console library exposes that input
as `readLine` after `import * from console`; the output delegate receives the text chunks that
`print` writes: values, separators, and terminators. Without delegates, the console library and
`print` retain their process Console behavior.

Use `ExecuteFile` when the host has explicitly selected an entry script:

```csharp
var result = instance.ExecuteFile("Scripts/startup.kit");
```

The full file path is preserved in compiler diagnostics. This loads only the selected entry file;
its imports remain governed by the instance's `FileLookup` or `DisableFileImports()` configuration.
Missing or unreadable entry files produce an `Input` failure with the original I/O exception in
`Failure.Exception`.

`SpellkitInstance` implements `IDisposable`. Disposing an instance invalidates its resource handles and
prevents further execution.

`ExecuteAsync`, `ExecuteFileAsync`, and `DispatchSignalsAsync` provide non-blocking host-call
surfaces. A pending host `Task` or `ValueTask` does not occupy a worker thread while the VM is
suspended. Instance operations remain serialized; concurrent calls wait for the active operation.
Use the synchronous methods when the caller deliberately needs to block, and prefer the asynchronous
methods when commands perform asynchronous work.

Program-backed instances also provide `ExecuteAsync(CancellationToken)`. Interactive execution has
matching asynchronous surfaces:

```csharp
using var run = await instance.StartAsync(source);
var step = await run.SelectAsync("continue");
var eventResult = await run.SendAsync("loaded", payload);

using var select = await instance.OpenSelectAsync("dialog");
await select.SelectAsync("confirm");
```

`SelectAsync` and `SendAsync` await asynchronous work in `choose` and `on` actions, including work
performed after a nested `do`. Their synchronous counterparts remain available and wait
synchronously for the same VM continuations.

For a script that executes `do` through `ExecuteAsync`, configure an asynchronous select runner
with `SpellkitEnvironment.UseSelectAsync(...)`. `UseSelect(...)` remains the synchronous adapter;
the last configured runner replaces the previous one.

## Host environment

A hosted instance provides the global `host` object without an import. Its state belongs to that
instance and is available from C# through `instance.Environment`.

```swift
host.Commands.List()
```

Outside a hosted instance, accessing members of `host` produces a runtime error. Hosted execution
always goes through `SpellkitInstance`; parser and compiler-only tooling may still use
`BuilderOptions` directly.

Set `SpellkitHostOptions.ExposeHostObject` to `false` when scripts should see only names supplied
through `SpellkitEnvironment.Expose(...)`:

```csharp
var host = new SpellkitHost(new()
{
    ExposeHostObject = false
});

var program = host.Compile("self.MoveTo(10, 20)").GetValueOrThrow();
var env = new SpellkitEnvironment(game).Expose("self", player);
using var instance = host.CreateInstance(program, env);
```

In this mode `host` is not a script-visible name. Accessing `host.State` or `host.Signals`
therefore produces the normal undeclared-variable diagnostic for `host`.

## Capabilities and command catalog

Calling `AddCapabilities` creates an allow-list in the default `Automatic` mode. Exact names and
hierarchical wildcards are supported. If `AddCapabilities` is not called, all explicitly registered
host features are available. Use `CapabilityMode = SpellkitCapabilityMode.Restricted` to start with
an empty, deny-all allow-list.

```csharp
host.AddCapabilities("scene.read", "audio.*")
    .Module("scene", module => module.Command(
        "Delete",
        "Deletes an entity.",
        "scene.write",
        context => context.Host<Game>().Delete(context.Argument<long>("id")),
        SpellkitCommandParameter.Required<long>("id")));
```

Commands whose capability is unavailable cannot be invoked and are omitted from the catalog.
Capability policy is available to C# through `instance.Environment.Capabilities`; scripts see
only the commands and signals that the policy makes available.

```swift
host.Commands.Find("scene")
host.Commands.Describe("scene.Delete")
```

Generated commands accept the same metadata:

```csharp
[SpellkitCommand(Description = "Deletes an entity.", Capability = "scene.write")]
public void Delete(long id) { /* ... */ }
```

### Built-in capabilities

Capability names are not declared in a central registry. `AddCapabilities` builds the allow-list,
and each protected operation demands the name documented below when it runs. The following names
are reserved by Spellkit's built-in host APIs:

| Capability | Protected script operations |
| --- | --- |
| `state.read` | `host.State[key]`, `host.State.Keys()`, `Has()`, and `Owner()` |
| `state.write` | Assignment to `host.State[key]`, `Remove()`, and `Clear()` |
| `log.write` | `host.Log.Debug()`, `Info()`, `Warning()`, and `Error()` |

Signal capabilities are chosen when calling `AddSignal` through `listenCapability` and
`emitCapability`. Module commands, generated commands and properties, and resource commands use
the capability supplied by their registration or attribute. Those application-defined names are
not built-in and may follow the host application's own namespace, such as `station.read` or
`station.control`.

In `Automatic` mode, registering at least one capability switches the host to an explicit
allow-list. In `Restricted` mode the allow-list is always active, even when empty. Every protected
built-in operation and application-defined operation must then match an exact entry, `*`, or a
hierarchical wildcard such as `state.*`. `Unrestricted` mode bypasses the allow-list. An unavailable
command is hidden from `host.Commands`; attempting another protected operation produces a runtime
error and a `CapabilityDenied` trace event when tracing is enabled.

## Resource handles

Resources use explicit wrapper classes. The wrapped domain object remains private, and only methods
marked with `[SpellkitCommand]` are exposed to scripts.

Derive a wrapper from `SpellkitResource` and give it a script-visible name:

```csharp
[SpellkitResource("Player")]
public sealed class PlayerResource : SpellkitResource
{
    private readonly Player player;

    public PlayerResource(Player player) => this.player = player;

    [SpellkitCommand]
    public string Name() => player.Name;

    [SpellkitCommand(Capability = "scene.write")]
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
without `[SpellkitCommand]` are not exposed.

Registered resource wrappers are shared by default. Repeatedly exposing the same wrapper instance
returns the same instance handle. Shared handles do not expose `Release`, survive `Reset()`, and are
invalidated when the instance is disposed. `OnRelease` runs once at instance disposal.

Mark wrapper types that the script should explicitly own and release as transient:

```csharp
[SpellkitResource("TemporaryFile", Lifetime = SpellkitResourceLifetime.Transient)]
public sealed class TemporaryFileResource : SpellkitResource
{
    private readonly TemporaryFile file;

    public TemporaryFileResource(TemporaryFile file) => this.file = file;

    [SpellkitCommand]
    public string Read() => file.Read();

    protected override void OnRelease() => file.Dispose();
}
```

Transient resources receive a new handle on every exposure. `OnRelease` runs once when the handle
is released explicitly, cleared by `Reset()`, or invalidated by instance disposal.

In every lifetime, the handle is invalidated before `OnRelease` runs. Failures from bulk cleanup
are collected so every callback is attempted, then reported as an `AggregateException`. Service
instances remain borrowed host objects and do not participate in resource release.

```swift
let player = scene.Find("player")
player.Type
player.IsValid()
player.MoveTo(10, 20)
player.Release()
```

Transient handles are invalidated by `Release()`, `Reset()`, or instance disposal. Shared and
service handles remain valid through `Reset()` and cannot be released by the script. No handle can
be transferred between instances.

## Shared state

`State` is an instance-scoped string-keyed store shared by C# and Spellkit. Each key is owned either
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
Spellkit `nil` is present and returns `true` with `default(T)`. Values are converted with the same
CLR conversion rules used by host command arguments.

`Set` creates or updates host-owned state. Spellkit can read host-owned keys but cannot overwrite or
remove them. `SetScript` creates or updates script-owned state; Spellkit and C# can both edit those
keys. Spellkit assignment creates script-owned state for new keys. `Remove` returns `false` and does
nothing for host-owned keys, and `Clear` removes only script-owned keys from Spellkit. C# `Remove`
and `Clear` still manage the whole store.

Spellkit reads require `state.read`; writes, removals, and script clearing require `state.write`.
These checks are inactive when the host has no explicit allow-list. `Reset()` clears instance state.

## Signals

Signals must be declared by the host. Listen and emit capabilities can be controlled separately.

```csharp
host.AddCapabilities("player.*")
    .AddSignal(
        "player.hit",
        listenCapability: "player.listen",
        emitCapability: "player.emit");
```

Spellkit subscriptions return an ID used by `Off`. `Once` removes its subscription before the first
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

Both C# and Spellkit emission enqueue a signal. Delivery is explicit and never re-enters a running
VM execution:

```csharp
instance.Environment.Signals.Emit("player.hit", 10);
var dispatch = instance.DispatchSignals();

if (!dispatch.Success)
    foreach (var failure in dispatch.Failures)
        Console.Error.WriteLine(failure.Message);
```

C# can observe dispatched signals as well:

```csharp
var subscription = instance.Environment.Signals.Subscribe(
    "player.hit",
    signal => Console.WriteLine(signal.GetPayload<int>()));

instance.Environment.Signals.Unsubscribe(subscription);
```

Pending queues are unbounded by default for compatibility. Set `Signals.MaxPending` on the host
options when producers can outpace dispatch:

```csharp
var host = new SpellkitHost(new()
{
    Signals = new() { MaxPending = 1024 }
});
```

The limit is copied into every instance created by that host. `TryEmit` returns `false` when the
queue is full; `Emit` throws `InvalidOperationException` on the C# side and produces a runtime
failure when called by a script. Invalid signal names and disposed dispatchers still throw from
both methods. `PendingCount` reports the current queue length.

```csharp
if (!instance.Environment.Signals.TryEmit("player.hit", 10))
    droppedSignals.Increment();
```

Spellkit can select the same non-throwing behavior:

```swift
if !host.Signals.TryEmit("player.hit", 10) {
    host.Log.Warning("signal queue is full")
}
```

Use `GetPayload<T>()` or `TryGetPayload<T>()` to consume signal payloads without depending on
runtime object types. The raw `Payload` remains available for advanced integrations.

`DispatchSignals()` processes only signals that were queued when dispatch began. Signals emitted
by a callback remain queued until the next call. `Reset()` removes Spellkit subscriptions and queued
signals while preserving C# subscriptions. Instance disposal removes all subscriptions.

## Structured logs

Logging is synchronous and uses a host-provided delegate. No task or scheduler is created by this
API.

```csharp
var host = new SpellkitHost(new()
{
    Log = entry =>
        Console.WriteLine($"[{entry.Level}] {entry.Message}")
});
host.AddCapabilities("log.write");
```

Stateful handlers can assign an instance method such as `recorder.Handle`. Combine multiple
handlers with a multicast delegate before constructing the host.

Spellkit exposes four log levels and optional structured properties. A tuple or dictionary is
converted to a case-insensitive property map.

```swift
host.Log.Debug("loading started")
host.Log.Info("selected player", (id: "player-1", source: "console"))
host.Log.Warning("health is low", ["health": 5])
host.Log.Error("command failed")
```

Logs require `log.write`.

Host commands use the same sink through `SpellkitCommandContext`:

```csharp
module.Command("Load", context =>
{
    context.Log(
        SpellkitLogLevel.Info,
        "loading scene",
        new Dictionary<string, object?> { ["scene"] = "town" });
    return null;
});
```

Every `Execute` and `DispatchSignals` call receives a new correlation ID. It is available through
`SpellkitExecutionResult.ExecutionId`, `SpellkitSignalDispatchResult.ExecutionId`, `SpellkitCommandContext.ExecutionId`,
and `SpellkitLogEntry.ExecutionId`. Entries produced by a host command also contain its unqualified
command name in `Command`; script and signal-level entries leave it empty.

### Log payload

The `Log` delegate in `SpellkitHostOptions` receives an immutable record object:

| `SpellkitLogEntry` member | Meaning |
| --- | --- |
| `Timestamp` | UTC time at which `SpellkitTelemetry.Write` created the entry |
| `Level` | `Debug`, `Info`, `Warning`, or `Error` |
| `Message` | Log message supplied by the script or host command |
| `Properties` | Case-insensitive, read-only structured property map; empty when omitted |
| `ExecutionId` | Correlation ID of the current `Execute` or `DispatchSignals` operation |
| `Command` | Unqualified host-command name while that command is executing; otherwise `null` |

`ExecutionId` can be used to group interleaved diagnostic output by operation. `Command` identifies
which host boundary produced an entry, but it is intentionally `null` for direct script calls such
as `host.Log.Info(...)`. C# code may also call `instance.Environment.Telemetry.Write()`; outside an
active execution context its execution ID is `Guid.Empty`. Correlation state flows through
asynchronous continuations started by an operation, but it is not shared with unrelated threads
that write telemetry while that operation is running.

```csharp
var host = new SpellkitHost(new()
{
    Log = entry => logger.Write(
        entry.Timestamp,
        entry.Level,
        entry.ExecutionId,
        entry.Command,
        entry.Message,
        entry.Properties)
});
```

Handler exceptions are synchronous failures. A Spellkit log call reports them as a runtime error,
and a failure raised by a Handler inside a host command is reported as a host command failure.

## Execution limits

Limits are configured once on `SpellkitHost` and applied independently to every `Execute` and
`DispatchSignals` operation.

```csharp
var host = new SpellkitHost(new()
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

An exceeded limit returns a `SpellkitFailure` whose `Kind` is `Limit`. Its `Limit` identifies
`Instructions`, `Time`, `HostCommands`, `Signals`, or `CallDepth`. Instruction,
command, and Signal counters contain completed work; an operation rejected by a limit is not added
to the corresponding counter.

Cancellation is supplied per operation:

```csharp
var result = instance.Execute(source, cancellationToken);
var dispatch = instance.DispatchSignals(cancellationToken);
```

Host commands receive a combined token through `SpellkitCommandContext.CancellationToken`. It is
cancelled by either the operation token or `MaxExecutionTime`, so a command that performs long-running
C# work should observe it itself. The VM checks cancellation and time periodically while executing
bytecode and again when a host command returns. .NET does not provide a safe way to forcibly stop a
synchronous handler that ignores cancellation.

`SpellkitExecutionResult.Metrics` and `SpellkitSignalDispatchResult.Metrics` contain total, compilation, and VM
durations plus instruction, host-command, and Signal counts. Instruction counting is enabled when
limits, tracing, or a cancellable token are active; otherwise it remains zero to avoid adding work
to unrestricted instances.

## Execution tracing

Tracing is opt-in and independent from user-facing logs. It records execution phases and host
boundaries without changing script behavior.

```csharp
var traces = new List<SpellkitTraceEvent>();
var host = new SpellkitHost(new() { Trace = traces.Add });
```

`SpellkitTraceKind` includes:

- `ExecutionStarted` and `ExecutionCompleted`
- `Compilation` and `VmExecution`
- `HostCommand`
- `CapabilityDenied`
- `SignalEmitted` and `SignalDelivered`
- `ResourceCreated` and `ResourceReleased`

`SpellkitTraceEvent` contains:

| Member | Meaning |
| --- | --- |
| `Timestamp` | UTC time at which the event was created |
| `Kind` | Event category from `SpellkitTraceKind` |
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

Events emitted by C# outside `Execute` or `DispatchSignals` use `Guid.Empty` for `ExecutionId`.
`Trace` accepts an `Action<SpellkitTraceEvent>`. Unlike log handlers, trace handler exceptions are
ignored because tracing is observational and must not alter script results.

```csharp
Trace = trace =>
{
    if (trace.Kind == SpellkitTraceKind.CapabilityDenied)
        securityLog.Write(trace.ExecutionId, trace.Name);
    else if (trace.Duration is { } elapsed)
        timings.Record(trace.Kind, trace.Name, elapsed);
}
```

## Game console example

The following setup exposes a deliberately small surface to an in-game console. The engine remains
responsible for scene work; Spellkit only combines registered operations.

```csharp
var host = new SpellkitHost(new()
{
    Limits = new()
    {
        MaxInstructions = 50_000,
        MaxExecutionTime = TimeSpan.FromMilliseconds(25),
        MaxHostCommands = 50,
        MaxSignals = 16,
        MaxCallDepth = 32
    },
    Log = entry => console.AddLine(entry.Level, entry.Message),
    Trace = trace => diagnostics.Record(trace)
});

host.AddResourceType<EntityResource>();

host.AddCapabilities(
        "scene.read", "scene.write",
        "state.*", "log.write",
        "player.listen")
    .AddSignal(
        "player.selected",
        listenCapability: "player.listen");

host.Module("scene", module => module.Command(
    "Find",
    "Finds an entity by name.",
    "scene.read",
    command => command.Resource(
        new EntityResource(scene.Find(command.Argument<string>("name")))),
    SpellkitCommandParameter.Required<string>("name")));

using var instance = host.CreateInstance(game);
```

`EntityResource` follows the wrapper pattern from the resource section and exposes only its
attributed commands.

The console can then execute a small orchestration script:

```swift
import scene
let player = scene.Find("player")

host.Log.Info("moving player", (name: player.Name()))
player.MoveTo(10, 20)
host.State["lastCommand"] = "move player"

func selected(name) {
    let entity = scene.Find(name)
    host.Log.Info("selected", (entity: entity.Name()))
}

host.Signals.On("player.selected", selected)
```

The game loop does not run Spellkit asynchronously. It explicitly delivers queued engine events at
a safe point:

```csharp
instance.Environment.Signals.Emit("player.selected", selectedEntity.Name);
var dispatch = instance.DispatchSignals(frameCancellationToken);
```

## Security default

When no `FileLookup` is supplied, a host instance does not search the current directory, system
directories, or additional library paths. Only registered host modules and the built-in `lang`
module are available. Call `DisableFileImports()` when a host should stay restricted even if a
lookup was configured earlier. Supply an explicit `FileLookup` when script file imports are
intended.

```csharp
using Spellkit.Compiler;
using Spellkit.Hosting;
using Spellkit.Linker;

var options = BuilderOptions.Default();
var lookup = FileLookup.Restricted(options)
    .AddStartupPath(Path.Combine(AppContext.BaseDirectory, "scripts"))
    .AddPath(Path.Combine(AppContext.BaseDirectory, "mods"))
    .Build();

var host = new SpellkitHost(new() { BuilderOptions = options })
    .UseFileLookup(lookup);
using var instance = host.CreateInstance(game);
```

`FileLookup.Restricted(options)` searches only paths added explicitly with `AddStartupPath`,
`AddPath`, or `AddPaths`. `FileLookup.Standard(options)` also searches relative to the importing
file and paths from `SPELLKIT_LIBS`. Spellkit never searches beside its executable implicitly.

Use the same host configuration but disable file imports explicitly for a restricted console:

```csharp
var restrictedHost = new SpellkitHost(new() { BuilderOptions = options })
    .UseFileLookup(lookup)
    .DisableFileImports();
```

The command-line host registers its standard modules, including `io`, through this same C# API.

## Generated command bindings

`Spellkit.Generators` can generate the `SpellkitHost` registration code from ordinary C# methods.

```csharp
using Spellkit.Hosting;

[SpellkitModule("game")]
public sealed class GameCommands
{
    [SpellkitProperty(Description = "Current scene name.", Capability = "scene.read")]
    public string SceneName => game.SceneName;

    [SpellkitProperty(Capability = "audio.write")]
    public double Volume
    {
        get => game.Volume;
        set => game.Volume = value;
    }

    [SpellkitCommand("spawn", Description = "Creates an entity from a prefab.")]
    public GameObject Spawn(string prefab, bool active = true) { /* ... */ }

    [SpellkitCommand]
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

A `SpellkitCommandContext` parameter is injected rather than exposed to Spellkit. C# optional
parameter values and the `Description` property are copied into command metadata.

`SpellkitProperty` exposes an ordinary C# property as a module property:

```swift
print(game.SceneName)
game.Volume = 0.5
```

A getter is required. Omitting the C# setter makes the Spellkit property read-only. The declared
capability protects both reads and writes, and the property appears once in the command catalog;
its generated setter is an internal implementation detail. Use properties for live values and
lightweight settings. Keep operations with substantial side effects as `SpellkitCommand` methods.

When using project references, add the generator as an analyzer:

```xml
<ProjectReference Include="..\Spellkit.Generators\Spellkit.Generators.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Unsupported generic methods, `ref`/`out` parameters, `params` arrays, inaccessible methods, and
duplicate command names are reported as compiler diagnostics.

Set `Type` when commands should appear under a static host type rather than directly on the module:

```csharp
[SpellkitModule("math")]
public static class MathCommands
{
    [SpellkitCommand(Type = "Math")]
    public static long Abs(long value) => Math.Abs(value);
}
```

Custom Spellkit foreign types can be registered by the generated module initializer:

```csharp
[SpellkitModule("game")]
[SpellkitForeignType(typeof(EntityTypeInfo))]
public static class GameTypes { }
```

The generator validates that foreign types derive from `SpellkitForeignTypeInfo` and have an accessible
parameterless constructor. Generator diagnostics currently use `SpellkitH001` through `SpellkitH007`.

A specialized module can derive from `ForeignUnit` directly. Applying `SpellkitModule` makes the
generator register that unit through `module.Unit(...)`:

```csharp
[SpellkitModule("types")]
public sealed class TypesModule : ForeignUnit
{
    public TypesModule() { /* register related foreign types */ }
}
```

This form is useful when several foreign types share a strongly typed declaring unit. It cannot be
combined with generated `SpellkitCommand` or `SpellkitForeignType` declarations on the same module class.
The imperative API follows the same rule: a module configured with `Unit(...)` cannot also add
generated command, static type, or foreign type registrations.

Foreign type members use the related type-binding attributes:

```csharp
[SpellkitType]
public sealed partial class EntityTypeInfo : SpellkitForeignTypeInfo
{
    [SpellkitMethod]
    internal static string Name(ExecutionContext context, Entity self) => self.Name;

    [SpellkitProperty]
    internal static long Id(ExecutionContext context, Entity self) => self.Id;

    [SpellkitStaticMethod]
    internal static Entity Find(ExecutionContext context, long id) { /* ... */ }

    [SpellkitStaticProperty]
    internal static Entity None(ExecutionContext context) { /* ... */ }
}
```

`SpellkitCommand` exposes ordinary host commands on a module or static host type. `SpellkitType` and its member
attributes bind instance and static members on a `SpellkitForeignTypeInfo`. Operators and conversions stay
as explicit `SpellkitForeignTypeInfo` overrides because they participate in the runtime type protocol.

The optional library modules can be enabled explicitly with their generated extensions:

```csharp
host.AddBinaryModule()
    .AddCollectionsModule()
    .AddHttpModule()
    .AddTextModule()
    .AddTimeModule()
    .AddUuidModule()
    .AddIoModule();
```

The console library's `http` module follows a requests-style shape while keeping Spellkit keyword
rules intact:

```kit
import * from http

let res = Get("https://api.example.test/users",
    params: [active: true],
    headers: ["Accept": "application/json"],
    timeout: 5)

if res.ok {
    print(res.json()["items"])
}
```

Use `Session` when several requests share a base URL, headers, auth, or timeout:

```kit
let api = Session(
    baseUrl: "https://api.example.test",
    headers: ["Accept": "application/json"],
    auth: [bearer: token],
    timeout: 10)

let created = api.Post("orders", json: [id: 42, customer: "Ada"]).raiseForStatus()
```

The `collections` module adds ordered maps backed by .NET's sorted dictionary behavior:

```kit
import * from collections

let scores = SortedDictionary()
scores.Add("ben", 82)
scores.Add("ada", 98)

print(scores.First().key) // ada
```
