# Hosting API guide

The Hosting API exposes application commands to Minamo through ordinary C# APIs and attributes.
It is part of `Minamo.dll` and uses the `Minamo.Hosting` namespace.

## API boundary

`Minamo.Hosting` is the application-facing API. Parser, compiler, linker, and runtime APIs are
separate tooling and extension surfaces; see [Public API layers](PublicApiLayers.md).

## Reading guide

The common hosting flow is:

1. Create a `MinamoHost`.
2. Configure runtime policy and register modules, resources, and capabilities.
3. Create a `MinamoInstance`.
4. Execute scripts at host-chosen points.
5. Dispose the instance when its host scope ends.

## Concept map

The Hosting API uses a small set of names consistently on the C# side and the Minamo side:

| Concept | C# setup or access | Minamo access | Purpose |
| --- | --- | --- | --- |
| Module commands | `host.Module(...)` | `import module` | Named command groups exposed by the host |
| Resources | `host.AddResourceType<T>()`, `context.Resource(...)` | Returned handles | Instance-scoped opaque CLR objects |
| Registry | `Environment.Registry.Set` | `host.Registry` | Host-written, instance-scoped named values |
| Input | `MinamoEnvironment.UseInputAsync` | `host.Input()` | Arbitrary values supplied explicitly by the host |
| Capabilities | `host.AddCapabilities(...)`, `Environment.Capabilities` | None | Host-owned allow-list for protected features |
| Logging | `MinamoHostOptions.Log` | `host.Log` | User-facing structured log events |

Module commands expose operations, resources preserve object identity and lifetime, and the
registry lets host commands coordinate through instance-local values.

## Host Setup

```csharp
using Minamo.Hosting;

var host = new MinamoHost(new MinamoHostOptions
{
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

`MinamoExecutionResult` implements `IMinamoOperationResult`. Generic host reporting can use its
`Success`, `Failures`, `ExecutionId`, and `Metrics` members while operation-specific code can still
inspect diagnostics or the returned value.

`Metrics` contains total, compilation, and VM durations plus instruction and host-command counts.
The counters are collected when execution controls are active; otherwise they remain zero.

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

Module, resource, and capability names use dotted identifier segments. Commands,
static host types, and parameters use single identifiers. Invalid names are rejected during host
configuration. Group static host commands with `module.Type(...)`.

## Instances

`MinamoInstance` is incremental. Definitions created by one successful submission remain available to
later submissions. A failed submission does not commit its compiled definitions. Host-command side
effects and registry writes are not transactional and are not rolled back.

```csharp
await instance.ExecuteAsync("let boss = game.spawn(\"boss\")");
await instance.ExecuteAsync("game.teleport(boss, 100, 20)");
```

`Reset()` discards compiled definitions, registry values, and transient resource handles while
preserving snapshotted host registrations and shared resource handles. Dispose every instance when
its host scope ends; disposal invalidates its resource handles but does not dispose the borrowed
host context.

For repeated execution of the same code across multiple actors or players, compile once into a
`MinamoProgram` and create separate instances from it:

```csharp
host.Module("counter", module => module.Command("Increment", context =>
{
    var next = context.Environment.Registry.Get<long>("runs") + 1;
    context.Environment.Registry.Set("runs", next);
    return next;
}));

var compiled = host.Compile("import counter\ncounter.Increment()");

var program = compiled.GetValueOrThrow();

using var first = host.CreateInstance(program);
using var second = host.CreateInstance(program);

var firstRun = await first.ExecuteAsync();
var secondRun = await second.ExecuteAsync();
```

`MinamoProgram` contains compiled code and diagnostics and can be shared. Each
`MinamoInstance` combines a program, a `MinamoEnvironment`, and mutable execution data such as
runtime variables, registry values, and resource handles. Each `ExecuteAsync` call creates a
`MinamoExecution` with its own correlation ID and metrics.

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

### Host input

Configure an asynchronous input source when the host needs to supply values to a running script.
Input values use the normal CLR-to-Minamo conversion and are not limited to strings:

```csharp
var environment = new MinamoEnvironment(game)
    .UseInputAsync(async cancellationToken => await commandQueue.ReadAsync(cancellationToken));

using var instance = host.CreateInstance(environment);
var result = await instance.ExecuteAsync("""
    let message = host.Input()
    message["kind"]
    """);
```

`UseInputAsync<T>` receives the operation cancellation token and returns `ValueTask<T>`. Each call
to `host.Input()` obtains one value from that source; `null` becomes Minamo `nil`. Calling it without
a configured source is a runtime error. Hosting input never reads `Console.In`.

As an auxiliary setting, `UseOutput(Action<string>)` redirects text chunks written by `print`;
without it, `print` uses the process console. The `readline` module belongs to Minamo.Console and
reads `Console.In` directly; it is independent of Hosting input.

Use `ExecuteFileAsync("Scripts/startup.nami")` for a host-selected entry script. Imports still obey
the configured `FileLookup`; file I/O failures return an `Input` failure.

Instance operations are asynchronous and serialized. Pending host awaitables suspend the VM without
occupying a worker thread, and concurrent calls wait for the active operation. Interactive selects
follow the same model; see [Interactive selects](InteractiveSelect.md).

Pass a cancellation token to `ExecuteAsync` when the caller needs to stop an operation:

```csharp
var result = await instance.ExecuteAsync(source, cancellationToken);
```

Host commands receive the effective token through `MinamoCommandContext.CancellationToken`. The VM
observes cancellation while executing bytecode and after a host command returns; a long-running host
command must observe the token itself.

## Host environment

A hosted instance provides the global `host` object without an import. It exposes host-provided
input, the instance registry, and structured logging. The command catalog is available only to C#
through `instance.Environment.Commands`.

```csharp
var commands = instance.Environment.Commands.List();
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

In this mode `host` is not a script-visible name. Accessing `host.Registry` therefore produces the
normal undeclared-variable diagnostic for `host`.

## Execution policy

Execution policy combines access control with optional resource bounds. Capabilities determine
which host operations a script may use.

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
Capability policy and the filtered command catalog are available to C# through
`instance.Environment`. Scripts access registered modules directly and cannot inspect the catalog.

```csharp
var matches = instance.Environment.Commands.Find("scene");
var delete = instance.Environment.Commands.Describe("scene.Delete");
```

### Built-in capabilities

The following names are reserved by Minamo's built-in host APIs:

| Capability | Protected script operations |
| --- | --- |
| `registry.read` | `host.Registry[key]`, `host.Registry.Keys()`, and `Has()` |
| `log.write` | `host.Log.Debug()`, `Info()`, `Warning()`, and `Error()` |

Module, generated, and resource capabilities use names supplied by the host application.
An unavailable command is hidden from `Environment.Commands`; other denied operations produce a
runtime error.

For untrusted scripts, `MinamoHostOptions.Limits` can additionally apply per-operation execution
bounds. All bounds are optional and disabled by default; exceeding one returns a `Limit` failure.

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

## Instance registry

`Registry` is an instance-scoped, string-keyed store written by C# and read by Minamo. Host
commands can use it to coordinate through values that belong to one instance. Missing keys return
`nil`.

```swift
print(host.Registry["selectedPlayer"])
host.Registry.Has("selectedPlayer")
host.Registry.Keys()
```

```csharp
instance.Environment.Registry.Set("session.name", "Debug Console");
instance.Environment.Registry.Set("selectedPlayer", "player-1");
var selected = instance.Environment.Registry.Get<string>("selectedPlayer");
if (instance.Environment.Registry.TryGet<string>("selectedPlayer", out var current))
    Console.WriteLine(current);
```

`TryGet<T>()` returns `false` when the key is absent or its value cannot be converted. A stored
Minamo `nil` is present and returns `true` with `default(T)`. Values are converted with the same
CLR conversion rules used by host command arguments.

Only C# can call `Set`, `Remove`, and `Clear`; assignment through `host.Registry` is rejected.
Registry operations are individually thread-safe, but a sequence of reads and writes is not a
transaction. An external registry write does not republish an interactive select; deliver an `on`
event with `SendAsync` when the select must react to the change.

Minamo reads require `registry.read`.
In `Automatic` mode these checks are inactive when the host has no explicit allow-list;
`Restricted` mode enforces them even when the list is empty, and `Unrestricted` mode bypasses them.
`Reset()` clears the instance registry.

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
| `ExecutionId` | Correlation ID of the current `ExecuteAsync` operation |
| `Command` | Unqualified host-command name while that command is executing; otherwise `null` |

Each execution receives a correlation ID. Outside an active operation,
`instance.Environment.Telemetry.Write(...)` uses `Guid.Empty`. Log-handler exceptions become
runtime or host-command failures.

## Complete example

The [Station Console example](../../Examples/StationConsole/README.md) combines generated commands,
resources, execution policy, and logging in a runnable host.

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
