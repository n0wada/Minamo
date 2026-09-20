# Quest Console example

This example is a small game-style console. The Script keeps its quest flags in select-local
storage and defines the available actions and conditions. C# renders the terminal UI and drives
the interaction.

It demonstrates:

- `select`, `action`, and `exit`;
- free-form action metadata for host-facing presentation;
- `when` guards that read Script-owned, per-session quest flags;
- a `questGame` function that returns a select factory with select-local values;
- `alias(questGame(), "quest.town")` to expose that factory to the host;
- a small C# terminal adapter that submits `open quest.town`, displays `MinamoSelect.Actions`,
  and calls `SelectAsync`.

Run it from the repository root:

```powershell
dotnet run --project .\Examples\QuestConsole\QuestConsole.csproj
```

Talk to the guard, ask about the courier, accept the quest, return to the square, and leave. Action
guards use a select-local `screen` value to determine which actions are currently visible.

During initialization, `alias(questGame(), "quest.town")` calls `questGame` once and registers its
factory. `await instance.ExecuteAsync("open quest.town")` creates fresh cells for the select-local
quest values, binds the action and guard closures to those cells, and returns the resulting
`MinamoSelect` in `MinamoExecutionResult.Select`. `Program.cs` drives that Select by calling
`SelectAsync`, the same API used to test selects from C#.
