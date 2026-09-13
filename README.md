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

Use `--help` or `--version` for command-line information; enter `#help` in the REPL for
interactive commands.

## Interactive selects

`select` declares a host-driven interaction such as a menu, dialogue, or GUI screen. Select-local
values keep the interaction state, `prop` publishes read-only values, and `case` declares the
choices currently available to the host:

```swift
select Player {
    desc ["title": "Player"]

    mut player = loadPlayer()
    prop state => player.state

    case "play" when player.state == "stopped" => {
        player.state = "playing"
        player.play()
    }

    case "stop" when player.state == "playing" => {
        player.state = "stopped"
        player.stop()
    }

    case "close" => exit player
}
```

Save the declarations as `player.nami`, then start the select from the console:

```powershell
.\bin\minamo.exe .\player.nami --do Player
```

Inside the REPL, use the console command `do Player`. This command and `--do` drive the C# hosting
API for testing; scripts have no select invocation syntax. An ordinary case action updates the
state and republishes the select, while `exit` completes the interaction and may return a value.

See the [select navigation example](Examples/Language/12-select-navigation.nami) for `goto` and
`return`. The [interactive-select guide](Docs/Developers/InteractiveSelect.md) covers the complete
language and host integration model.

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

See [Compatibility](Docs/Developers/Compatibility.md) for the supported framework contract and
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

The Hosting guide covers host setup, commands, resources, the instance registry, execution limits,
observability, and security defaults.

### Tools and operations

- [Compatibility](Docs/Developers/Compatibility.md)

Compatibility documents current target frameworks and the repository validation suites.

### For developers

- [Public API layers](Docs/Developers/PublicApiLayers.md)

This guide distinguishes the application-facing Hosting API from tooling and runtime extension
surfaces.

## License

Minamo is available under the [MIT License](LICENSE).
