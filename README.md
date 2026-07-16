# Spellkit

Spellkit is a lightweight dynamic language and embeddable scripting runtime for .NET.
It is designed for applications that need a real programming language without handing scripts
unrestricted access to the host.

Spellkit includes a handwritten parser, bytecode compiler, virtual machine, interactive console,
optional standard-library modules, and a C# Hosting API. A host can expose a deliberately small
command surface, attach capabilities and execution limits, and keep CLR objects behind opaque
handles.

> Spellkit is under active development. The current runtime targets .NET 10.0, and its public
> distribution story is not finalized yet.

## Why Spellkit?

- **Embeddable by design** — create isolated instances and execute scripts from C#.
- **Explicit host boundaries** — scripts see registered commands and resources, not arbitrary CLR
  members.
- **Controlled execution** — optional capability allow-lists, cancellation, and limits for
  instructions, time, host calls, signals, pending signal queues, and call depth.
- **A compact but expressive language** — functions, iterators, pattern matching, modules,
  user-defined types, traits, exceptions, and extension methods.
- **Useful diagnostics** — structured compilation failures, runtime failures, metrics, and
  opt-in tracing are available to the host.

Spellkit began as a fork of [Dyalect](https://github.com/vorov2/dyalect) and is being reshaped around
small, controllable embedded runtimes. The language, assemblies, CLI, file formats, and Hosting API
now use the Spellkit identity.

## A taste of the language

Spellkit source files use the `.kit` extension.

```swift
func fibonacci(n) =>
    n < 2 ? n : fibonacci(n - 1) + fibonacci(n - 2)

for n in 1..10 {
    print(fmt("{0}: {1}", n, fibonacci(n)))
}
```

Functions are first-class values, and iterator functions use `yield`:

```swift
func messages() {
    yield "hello"
    yield "from Spellkit"
}

for message in messages() {
    print(message)
}
```

See the [recipes](Docs/Recipes.md) for small, runnable language examples.

## Build and run

You need the .NET 10 SDK. From PowerShell at the repository root:

```powershell
.\scripts\build-local.ps1 -Configuration Release
```

The Release build writes the distributable runtime and console files directly to `bin`. Start the
interactive console:

```powershell
dotnet .\bin\spk.dll
```

Or execute a source file:

```powershell
dotnet .\bin\spk.dll .\hello.kit
```

The console can also print VM bytecode:

```powershell
dotnet .\bin\spk.dll -il .\hello.kit
```

Inside the REPL, enter `#help` to list the available commands and switches.

Standard command-line help and version information are also available without entering the REPL:

```powershell
dotnet .\bin\spk.dll --help
dotnet .\bin\spk.dll --version
```

Development-only outputs are kept out of the Release folder:

| Output | Location |
| --- | --- |
| Release runtime and console | `bin\` |
| Debug runtime and console | `bin\debug\` |
| Unit test runner and dependencies | `bin\tests\unit\<Configuration>\` |
| Language report runner | `bin\tests\language\<Configuration>\` |
| Source generator | `bin\tools\generators\<Configuration>\` |
| Station Console example | `bin\examples\StationConsole\<Configuration>\` |

`build-local.ps1` still defaults to Debug for development builds. Use
`.\scripts\build-local.ps1 -Configuration Release` when refreshing the files in the `bin` root.

## Embed Spellkit in C#

The application-facing API lives in `Spellkit.Hosting`. The source generator turns ordinary C#
methods into commands, including argument conversion and command metadata:

```csharp
using Spellkit.Hosting;

[SpellkitModule("app")]
public sealed class AppCommands
{
    [SpellkitCommand("greet", Description = "Greets a user.")]
    public string Greet(string name)
        => $"Hello, {name}!";

    [SpellkitCommand("sum", Description = "Adds two integers.")]
    public long Sum(long left, long right)
        => left + right;
}
```

The host registers the generated bindings and supplies the command object to the instance:

```csharp
using Spellkit.Hosting;

var commands = new AppCommands();
var host = new SpellkitHost();

host.AddAppCommands();

using var instance = host.CreateInstance(commands);
var result = instance.Execute("""
    import app
    (app.greet("Spellkit"), app.sum(20, 22))
    """);

if (!result.Success)
    Console.Error.WriteLine(result.Failure?.Message);
```

`AddAppCommands()` is generated at build time. Method parameters become named Spellkit parameters,
supported values are converted to their declared C# types, and return values are converted back to
Spellkit values. A larger host can split commands across several module classes without maintaining
parallel `context.Argument<T>(...)` declarations.

Commands that need execution services can declare one `SpellkitCommandContext` parameter. It is injected
by the generated binding and is not visible as a Spellkit argument:

```csharp
[SpellkitCommand]
public void Save(SpellkitCommandContext context, string path)
{
    context.CancellationToken.ThrowIfCancellationRequested();
    context.Log(SpellkitLogLevel.Info, $"Saving {path}");
}
```

Instances are incremental: definitions from a successful execution remain available to the next
one. Failed compilation and execution are rolled back, and `Reset()` clears script state while
preserving the instance's host configuration.

When the same code should run for many actors, compile once and pass an instance environment:

```csharp
var program = host.Compile("self + world").GetValueOrThrow();
var env = new SpellkitEnvironment()
    .Expose("self", 2)
    .Expose("world", 3);

using var instance = host.CreateInstance(program, env);
var value = instance.Execute();
```

For actor-style sandboxes, `SpellkitHostOptions.ExposeHostObject = false` hides the global `host`
name so scripts can see only the names supplied by `SpellkitEnvironment`.

The Hosting API also supports:

- named modules and generated command bindings;
- module commands and instance-scoped resource handles;
- host-owned and script-owned instance state;
- queued signals delivered at host-selected safe points;
- instance-local input and output selected by the embedding host;
- capability-gated commands and host features;
- logging, execution metrics, and tracing;
- per-operation cancellation and execution limits.

File imports are unavailable to a hosted instance unless the host supplies an explicit lookup.
Likewise, CLR resources expose only their attributed commands; Spellkit does
not reflect over their public members.

For the complete API and lifecycle contract, read the [Hosting guide](Docs/Hosting.md).

## Public API layers

The public surface is separated by intended use:

- **Application API** — `Spellkit.Hosting`; this is the normal entry point for executing source
  strings and files.
- **Tooling API** — parser, syntax model, compiler, linker, bytecode, and debugger namespaces.
- **Runtime extension API** — runtime values, contexts, and interop for integrations that
  deliberately participate in VM semantics.
- **Internal implementation** — VM stacks, dispatch state, compiler working state, registries,
  and process helpers are non-public.

See [Public API layers](Docs/ApiLayers.md) for the namespace map and guidance.

Tooling can parse or compile source without assembling the lower-level pipeline:

```csharp
var syntax = SpkParser.Parse(source);
var compiled = SpellkitCompiler.Compile(source);
var compiledFile = SpellkitCompiler.CompileFile("script.kit");
```

The compiler facade uses restricted file lookup by default. Supply an explicit `FileLookup` when
imports should be resolved.

## Repository layout

| Path | Purpose |
| --- | --- |
| `Spellkit` | Parser, compiler, linker, VM, runtime types, and Hosting API |
| `Spellkit.Console` | `spk` command-line runner, REPL, and standard modules |
| `Spellkit.Generators` | Source generators for C# host bindings |
| `Spellkit.UnitTests` | xUnit tests and the optional `.kit` language report runner |
| `Examples` | Runnable C# hosts and Spellkit scripts |
| `Docs` | Language, hosting, compatibility, and test documentation |

## Examples

The [Station Console](Examples/StationConsole/README.md) example combines a C# space-station
simulation with a Spellkit emergency script. It demonstrates command discovery, opaque resource
handles, capabilities, queued signals, telemetry, tracing, metrics, and execution limits:

```powershell
dotnet run --project .\Examples\StationConsole\StationConsole.csproj
```

The [Order Workflow](Examples/OrderWorkflow/README.md) example is a script-first order pipeline.
Its five `.kit` files separate the domain model, validation, shipment selection, notifications,
and signal handlers; the small C# host only exposes the order-system boundary:

```powershell
dotnet run --project .\Examples\OrderWorkflow\OrderWorkflow.csproj
```

## Validate the checkout

Run the complete local validation suite:

```powershell
.\scripts\test-local.ps1
```

Individual contract layers can be selected when working on a specific part of the runtime:

```powershell
.\scripts\test-local.ps1 -Suite Pipeline
.\scripts\test-local.ps1 -Suite Hosting
.\scripts\test-local.ps1 -Suite Generator
.\scripts\test-local.ps1 -Suite Language
.\scripts\test-local.ps1 -Suite Security
```

The C# contract tests and each `.kit` language test file are exposed through xUnit and can also be
discovered by IDE test explorers or run directly:

```powershell
dotnet test .\Spellkit.UnitTests\Spellkit.UnitTests.csproj
```

Select an individual language file by its display name:

```powershell
dotnet test .\Spellkit.UnitTests\Spellkit.UnitTests.csproj --filter "DisplayName~array.kit"
```

The same `Spellkit.UnitTests` project can also be built with `LanguageRunner=true` to produce the
standalone Markdown report runner. The local test script does this after the xUnit language tests
for the `All` and `Language` suites.

Run a single region with an explicit per-test timeout when reproducing a failure:

```powershell
.\scripts\test-local.ps1 -Suite Language -TestPath "Spellkit.UnitTests\Tests\array.kit" -Region "Methods: Add, AddRange, Remove, RemoveAt" -TimeoutSeconds 10
```

Standalone failure reports include the file and region, elapsed time, expected and actual assertion
values when available, a stack trace, and a copyable reproduction command.

The xUnit project groups tests by subsystem under `Parser`, `Compiler`, `Runtime`, `Interop`,
`Hosting`, `Generators`, `Language`, and `Security`. Suite traits keep the existing
command-line filters stable, while `Category=Security` selects the focused security regression
tests.

Collect line and branch coverage without enforcing a threshold:

```powershell
.\scripts\coverage-local.ps1
```

The generated summary under `artifacts/coverage` highlights Parser, Compiler, Runtime/Interop,
Hosting, and Linker/FileLookup separately.

Validate the Release CLI, Station Console example, and the packed NuGet consumer independently:

```powershell
.\scripts\test-release-smoke.ps1
.\scripts\test-package-local.ps1
```

The package smoke test verifies the runtime, source generator, icon, and README entries, then builds
and runs a separate application using only the generated local package.

Generator output is protected by approved snapshots under
`Spellkit.UnitTests/Generators/Snapshots`. After intentionally changing generated code, refresh
them with `SPELLKIT_UPDATE_SNAPSHOTS=1` while running the Generator suite, then review the diff.

See [Compatibility](Docs/Compatibility.md) for the current framework contract and validation
levels.

## Documentation

- [Language overview](Docs/Overview.md)
- [Grammar reference](Docs/Grammar.md)
- [Language recipes](Docs/Recipes.md)
- [Hosting API](Docs/Hosting.md)
- [Public API layers](Docs/ApiLayers.md)
- [Compatibility](Docs/Compatibility.md)

## License

Spellkit is available under the [MIT License](LICENSE).
