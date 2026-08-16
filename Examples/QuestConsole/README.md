# Quest Console example

This example is a small game-style console. The Script keeps its quest flags in select-local
storage and defines the conversation states, available actions, conditions, and transitions. C#
renders the terminal UI and drives the interaction.

It demonstrates:

- `select`, `initial state`, `choose`, `goto`, and `exit`;
- `label` for host-facing presentation;
- `when` guards that read Script-owned, per-session quest flags;
- a `questGame` function that returns a select factory with select-local state;
- `alias(questGame(), "quest.town")` to expose that factory to the host;
- a small C# terminal adapter that opens `quest.town`, displays `SpellkitSelect.Choices`,
  and calls `SelectAsync`.

Run it from the repository root:

```powershell
dotnet run --project .\Examples\QuestConsole\QuestConsole.csproj
```

Talk to the guard, ask about the courier, accept the quest, return to the square, and leave. The
`accept` choice is hidden until the Script has recorded that the courier quest is known.

During initialization, `alias(questGame(), "quest.town")` calls `questGame` once and registers its
factory. Each `await instance.OpenSelectAsync("quest.town")` creates fresh cells for the two select-local
quest flags, then binds the choice and guard closures to those cells. `Program.cs` drives the
resulting `SpellkitSelect` by calling `SelectAsync`. This example intentionally shows the
C#-initiated API rather than `do` and a suspended Script continuation.
