# Quest Console example

This example is a small game-style console. C# owns the quest facts and renders the terminal UI;
Spellkit owns the conversation states, available actions, conditions, and transitions.

It demonstrates:

- `select`, `initial state`, `choose`, `goto`, and `exit`;
- `label` and `description` for host-facing presentation;
- `when` guards that react to host-owned game state;
- an external select name through `alias(town, "game.town")`;
- host-driven startup with `instance.OpenSelect("game.town")`;
- a small C# terminal adapter that displays `SpellkitSelectSession.Choices` and calls `Choose`.

Run it from the repository root:

```powershell
dotnet run --project .\Examples\QuestConsole\QuestConsole.csproj
```

Talk to the guard, ask about the courier, accept the quest, return to the square, and leave. The
`accept` choice is hidden until the host reports that the quest is available.

`Program.cs` contains the minimal terminal runner. A game can replace it with its own UI while
keeping the same `SpellkitSelectSession` protocol. The host initializes the Script definitions,
then opens the alias explicitly with `instance.OpenSelect("game.town")`.
