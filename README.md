# Minamo

<p align="center">
  <img src="minamo_logo.png" alt="Minamo logo">
</p>

Minamo is a lightweight dynamic language and embeddable scripting runtime for .NET.
It gives applications a real programming language without giving scripts unrestricted
access to the host.

Minamo includes a handwritten parser, bytecode compiler, virtual machine, interactive
console, standard-library modules, and a C# Hosting API.

## Why Minamo?

- **Embeddable by design** — create isolated instances and execute scripts from C#.
- **Explicit host boundaries** — scripts see registered commands and resources, not
  arbitrary CLR members.
- **Controlled execution** — hosts can apply capabilities, cancellation, and execution
  limits.
- **An expressive language** — functions, iterators, pattern matching, modules,
  user-defined types, traits, exceptions, and extension methods.

Minamo began as a fork of [Dyalect](https://github.com/vorov2/dyalect) and is being
reshaped around small, controllable embedded runtimes.

## Getting started

Minamo source files use the `.nami` extension.

```swift
func fibonacci(n) =>
    n < 2 ? n : fibonacci(n - 1) + fibonacci(n - 2)

for n in 1..10 {
    print(fmt("{0}: {1}", n, fibonacci(n)))
}
```

See the [language recipes](Docs/Language/Recipes.md) for small, runnable examples.

## Build and run

You need the .NET 10 SDK. From PowerShell at the repository root:

```powershell
.\scripts\build-local.ps1 -Configuration Release
```

Start the interactive console:

```powershell
.\bin\minamo.exe
```

Or execute a source file:

```powershell
.\bin\minamo.exe .\hello.nami
```

Check source syntax, imports, and compilation without executing it:

```powershell
.\bin\minamo.exe .\hello.nami --check
```

Start an interactive select declared by the file:

```powershell
.\bin\minamo.exe .\Player.nami --do music.player
```

Inside the REPL, use the console command `do music.player`. This command and `--do` drive the C#
hosting API for testing; scripts have no select invocation syntax. The console displays the currently available choices;
enter their number.

Use `--help` or `--version` for command-line information; enter `#help` in the REPL for
interactive commands.

The Windows `minamo.exe` distribution is framework-dependent and requires the .NET 10
Runtime to be installed.

## Embed Minamo in C#

Embedding requires references to `Minamo.dll` (the Hosting API and runtime) and
`Minamo.Generators.dll` (the source generator). Reference the generator as an analyzer
at build time; the running application requires `Minamo.dll`.

The application-facing API lives in `Minamo.Hosting`. Source generation turns attributed
C# methods into commands:

```csharp
using Minamo.Hosting;

[MinamoModule("app")]
public sealed class AppCommands
{
    [MinamoCommand("greet")]
    public string Greet(string name) => $"Hello, {name}!";
}
```

Register the generated bindings and execute a script in an isolated instance:

```csharp
var host = new MinamoHost();
host.AddModule(new AppCommands());

using var instance = host.CreateInstance();
var result = instance.ExecuteFile("hello.nami");

if (!result.Success)
    Console.Error.WriteLine(result.Failure?.Message);
```

`hello.nami` contains the script code:

```swift
import app
app.greet("Minamo")
```

Hosts can expose selected commands and resources, supply capabilities and limits, and keep
CLR objects behind opaque handles. For the complete lifecycle and API contract, see the
[Hosting guide](Docs/Developers/HostingGuide.md). The [Public API layers](Docs/Developers/PublicApiLayers.md) guide explains
the application, tooling, and runtime-extension surfaces.

## Examples

- [Station Console](Examples/StationConsole/README.md) combines a C# space-station
  simulation with a Minamo emergency script:

  ```powershell
  dotnet run --project .\Examples\StationConsole\StationConsole.csproj
  ```

- [Order Workflow](Examples/OrderWorkflow/README.md) is a script-first order pipeline:

  ```powershell
  dotnet run --project .\Examples\OrderWorkflow\OrderWorkflow.csproj
  ```

- [Quest Console](Examples/QuestConsole/README.md) is a game-style interactive select where C#
  owns quest data and Minamo owns dialogue states and choices:

  ```powershell
  dotnet run --project .\Examples\QuestConsole\QuestConsole.csproj
  ```

## Repository layout

| Path | Purpose |
| --- | --- |
| `Minamo` | Parser, compiler, linker, VM, runtime types, and Hosting API |
| `Minamo.Console` | `minamo` command-line runner, REPL, and standard modules |
| `Minamo.Generators` | Source generators for C# host bindings |
| `Minamo.UnitTests` | xUnit tests and the optional `.nami` language report runner |
| `Examples` | Runnable C# hosts and Minamo scripts |
| `Docs` | Language, hosting, compatibility, and test documentation |

## Validate the checkout

Run the full local validation suite:

```powershell
.\scripts\test-local.ps1
```

For focused work, the xUnit project can be run directly:

```powershell
dotnet test .\Minamo.UnitTests\Minamo.UnitTests.csproj
```

See [Compatibility](Docs/Operations/Compatibility.md) for the supported framework contract and
validation levels.

## Documentation

The rest of the documentation is organized by the task you are trying to complete.

### Language guide

Use the overview to learn the language by concept, then use recipes for complete, runnable
programs.

- [Language overview](Docs/Language/Overview.md)
- [Syntax](Docs/Language/Syntax.md)
- [Built-in types and functions](Docs/Language/Builtins.md)
- [Operators](Docs/Language/Operators.md)
- [Program structure](Docs/Language/ProgramStructure.md)
- [Interactive selects](Docs/Developers/InteractiveSelect.md)
- [Types and traits](Docs/Language/TypesAndTraits.md)
- [Functions and closures](Docs/Language/FunctionsAndClosures.md)
- [Semantics](Docs/Language/Semantics.md)
- [Language recipes](Docs/Language/Recipes.md)

### Language reference

- [Grammar reference](Docs/Reference/Grammar.md)

The grammar reference describes the syntax accepted by the current parser. The parser and the
language test corpus remain authoritative for current behavior.

### Host integration

- [Hosting API guide](Docs/Developers/HostingGuide.md)

The Hosting guide covers host setup, commands, resources, state, signals, execution limits,
observability, and security defaults.

### Tools and operations

- [Compatibility](Docs/Operations/Compatibility.md)

Compatibility documents current target frameworks and the repository validation suites.

### For developers

- [Public API layers](Docs/Developers/PublicApiLayers.md)
- [Advanced interactive select design](Docs/Developers/InteractiveSelectAdvanced.md)

These guides distinguish the application-facing Hosting API from tooling and runtime extension
surfaces, and record planned language and host integration designs.

## License

Minamo is available under the [MIT License](LICENSE).
