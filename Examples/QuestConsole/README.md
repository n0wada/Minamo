# Quest Console example

This example is a small game-style console. The Script keeps its quest flags in select-local
storage and defines the available actions and conditions. C# renders the terminal UI and drives
the interaction.

It demonstrates:

- `select`, `case`, and `exit`;
- free-form case metadata for host-facing presentation;
- `when` guards that read Script-owned, per-session quest flags;
- a `questGame` function that returns a select factory with select-local values;
- `alias(questGame(), "quest.town")` to expose that factory to the host;
- a small C# terminal adapter that opens `quest.town`, displays `MinamoSelect.Choices`,
  and calls `SelectAsync`.

Run it from the repository root:

```powershell
dotnet run --project .\Examples\QuestConsole\QuestConsole.csproj
```

Talk to the guard, ask about the courier, accept the quest, return to the square, and leave. Choice
guards use a select-local `screen` value to determine which actions are currently visible.

During initialization, `alias(questGame(), "quest.town")` calls `questGame` once and registers its
factory. Each `await instance.OpenSelectAsync("quest.town")` creates fresh cells for the select-local
quest values, then binds the choice and guard closures to those cells. `Program.cs` drives the
resulting `MinamoSelect` by calling `SelectAsync`, the same API used to test selects from C#.
