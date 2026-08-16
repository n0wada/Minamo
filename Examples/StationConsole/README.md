# Station Console example

This example models a tiny space-station control console. C# owns the station state and exposes
only selected operations to Spellkit. The Spellkit script installs an emergency signal handler,
receives a instance-scoped reactor and door resource, and coordinates a response.

It demonstrates:

- an attribute-defined `StationCommands` module registered with `host.AddModule(...)`;
- a read-only `SpellkitProperty` for the station's live oxygen level;
- self-describing resource wrappers, reference-stable handles, release callbacks, and cataloged
  operations;
- capabilities and restricted file imports;
- live module commands, queued signals, logs, tracing, metrics, and execution limits;
- explicit script-file execution and common operation-result reporting;
- failure reporting without exposing arbitrary CLR members to the script.

Run it from the repository root:

```powershell
dotnet run --project .\Examples\StationConsole\StationConsole.csproj
```

The host first loads `Scripts/emergency.kit`, then simulates an oxygen incident in engineering.
The signal is delivered explicitly with `DispatchSignalsAsync()`. The script locks the engineering
door and raises reactor output, while the other CLR members remain unavailable.
