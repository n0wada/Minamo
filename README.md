# Spellkit

Spellkit is a lightweight dynamic language and embeddable scripting runtime for .NET.
It gives applications a real programming language without giving scripts unrestricted
access to the host.

Spellkit includes a handwritten parser, bytecode compiler, virtual machine, interactive
console, optional standard-library modules, and a C# Hosting API.

> Spellkit is under active development. The current runtime targets .NET 10.0, and its
> public distribution story is not finalized yet.

## Why Spellkit?

- **Embeddable by design** — create isolated instances and execute scripts from C#.
- **Explicit host boundaries** — scripts see registered commands and resources, not
  arbitrary CLR members.
- **Controlled execution** — hosts can apply capabilities, cancellation, and execution
  limits.
- **An expressive language** — functions, iterators, pattern matching, modules,
  user-defined types, traits, exceptions, and extension methods.

Spellkit began as a fork of [Dyalect](https://github.com/vorov2/dyalect) and is being
reshaped around small, controllable embedded runtimes.

## A taste of the language

Spellkit source files use the `.kit` extension.

```swift
func fibonacci(n) =>
    n < 2 ? n : fibonacci(n - 1) + fibonacci(n - 2)

for n in 1..10 {
    print(fmt("{0}: {1}", n, fibonacci(n)))
}
```

See the [language recipes](Docs/Recipes.md) for small, runnable examples.

## Build and run

You need the .NET 10 SDK. From PowerShell at the repository root:

```powershell
.\scripts\build-local.ps1 -Configuration Release
```

Start the interactive console:

```powershell
dotnet .\bin\spk.dll
```

Or execute a source file:

```powershell
dotnet .\bin\spk.dll .\hello.kit
```

Use `--help` or `--version` for command-line information; enter `#help` in the REPL for
interactive commands.

## Embed Spellkit in C#

The application-facing API lives in `Spellkit.Hosting`. Source generation turns attributed
C# methods into commands:

```csharp
using Spellkit.Hosting;

[SpellkitModule("app")]
public sealed class AppCommands
{
    [SpellkitCommand("greet")]
    public string Greet(string name) => $"Hello, {name}!";
}
```

Register the generated bindings and execute a script in an isolated instance:

```csharp
var host = new SpellkitHost();
host.AddModule(new AppCommands());

using var instance = host.CreateInstance();
var result = instance.ExecuteFile("hello.kit");

if (!result.Success)
    Console.Error.WriteLine(result.Failure?.Message);
```

`hello.kit` contains the script code:

```swift
import app
app.greet("Spellkit")
```

Hosts can expose selected commands and resources, supply capabilities and limits, and keep
CLR objects behind opaque handles. For the complete lifecycle and API contract, see the
[Hosting guide](Docs/Hosting.md). The [Public API layers](Docs/ApiLayers.md) guide explains
the application, tooling, and runtime-extension surfaces.

## Examples

- [Station Console](Examples/StationConsole/README.md) combines a C# space-station
  simulation with a Spellkit emergency script:

  ```powershell
  dotnet run --project .\Examples\StationConsole\StationConsole.csproj
  ```

- [Order Workflow](Examples/OrderWorkflow/README.md) is a script-first order pipeline:

  ```powershell
  dotnet run --project .\Examples\OrderWorkflow\OrderWorkflow.csproj
  ```

## Repository layout

| Path | Purpose |
| --- | --- |
| `Spellkit` | Parser, compiler, linker, VM, runtime types, and Hosting API |
| `Spellkit.Console` | `spk` command-line runner, REPL, and standard modules |
| `Spellkit.Generators` | Source generators for C# host bindings |
| `Spellkit.UnitTests` | xUnit tests and the optional `.kit` language report runner |
| `Examples` | Runnable C# hosts and Spellkit scripts |
| `Docs` | Language, hosting, compatibility, and test documentation |

## Validate the checkout

Run the full local validation suite:

```powershell
.\scripts\test-local.ps1
```

For focused work, the xUnit project can be run directly:

```powershell
dotnet test .\Spellkit.UnitTests\Spellkit.UnitTests.csproj
```

See [Compatibility](Docs/Compatibility.md) for the supported framework contract and
validation levels.

## Documentation

- [Language overview](Docs/Overview.md)
- [Grammar reference](Docs/Grammar.md)
- [Language recipes](Docs/Recipes.md)
- [Hosting API](Docs/Hosting.md)
- [Public API layers](Docs/ApiLayers.md)
- [Compatibility](Docs/Compatibility.md)

## License

Spellkit is available under the [MIT License](LICENSE).
