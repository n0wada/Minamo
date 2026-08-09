# Interactive selects

`select` lets a Spellkit script describe a menu, dialogue, shop, or similar interaction. The
script decides which actions are available and where they lead; the C# host displays those actions
and sends the selected ID back to the script.

This page covers the normal synchronous host integration. It uses `OpenSelect` and its
small `SpellkitSelect` API. For web or asynchronous UIs, dynamic choices, nested selects, and the
Snapshot/Revision protocol, see [Advanced interactive selects](InteractiveSelectAdvanced.md).

## Quick start

Declare a named select at module scope. It has one initial state and one or more visible choices.

```kit
select town {
    initial state square {
        choose "leave" => exit "goodbye"
    }
}
```

Execute the script, then open a new interaction and drive it with the currently available choices.

```csharp
var initialization = instance.Execute("""
    select town {
        initial state square {
            choose "leave" => exit "goodbye"
        }
    }
    """);
if (!initialization.Success)
    throw new InvalidOperationException(initialization.Failure?.Message);

using var town = instance.OpenSelect("town");
while (!town.IsCompleted)
{
    var choice = ui.Pick(town.Choices);
    var result = town.Select(choice.Id);

    if (result.IsCompleted)
        Console.WriteLine(result.GetValue<string>());
}
```

`SpellkitSelect` exposes only the operations needed for this loop:

| Member | Purpose |
| --- | --- |
| `State` | Current script state name. |
| `Choices` | The currently visible choices. |
| `IsCompleted` | Whether the select has exited or was cancelled. |
| `Select(id, argument?)` | Runs a visible choice. |
| `Send(id, argument?)` | Delivers a hidden host event declared with `on`. |
| `Cancel()` / `Dispose()` | Ends the interaction without an exit result. |

Each call to `OpenSelect` creates a new interaction with its own current state and
select-local values.

## States and choices

A state contains the choices available at that point. `choose` IDs are the stable values sent by
the host. `label` and `description` are display text; when omitted, the label defaults to the ID.
`goto` enters another state and `exit` completes the interaction, optionally with a value.

```kit
select player {
    initial state stopped {
        choose "play" label "Play" => goto playing
        choose "exit" label "Close player" => exit
    }

    state playing {
        choose "pause" => goto paused
        choose "stop" => goto stopped
        choose "exit" => exit
    }

    state paused {
        choose "resume" => goto playing
        choose "stop" => goto stopped
        choose "exit" => exit
    }
}
```

Exactly one state must be marked `initial`. State names are internal to the script; use choice IDs
for host input. If a choice body does not run `goto` or `exit`, the interaction remains in its
current state.

## Passing values to choices

Choice parameters determine the C# payload shape:

```kit
choose "play" => { }
choose "select-track" (trackId) => { }
choose "set-volume" (trackId, value) => { }
```

```csharp
player.Select("play");
player.Select("select-track", trackId);
player.Select("set-volume", (trackId, 80));
```

No parameter means that no payload is accepted. One parameter receives the supplied value. Two or
more parameters receive one C# tuple with the same number of elements.

## Conditional choices

Use `when` to hide a choice until it is available.

```kit
choose "accept"
    label "Accept the courier quest"
    when game.CanAcceptCourierQuest() => {
    game.AcceptCourierQuest()
    goto square
}
```

A false guard removes the choice from `Choices`. Guards run when choices are read and again before
the selected action runs, so guard expressions should be free of side effects.

## Host events

`on` declares an event that is not displayed in `Choices`. Use it for host-owned domain events
such as a timer, a completed download, or an inventory update.

```kit
select download {
    initial state waiting {
        on "completed" (fileName) => exit fileName
        choose "cancel" => exit nil
    }
}
```

```csharp
var result = download.Send("completed", fileName);
```

`Send` uses the same argument rules as `Select`. An event is accepted only in a state that declares
it.

## When to use the advanced API

Use this basic API for a CLI, a single-threaded desktop UI, or any host that renders choices and
handles the next input immediately. Move to [Advanced interactive selects](InteractiveSelectAdvanced.md)
when an old screen or client request must be rejected, host data can change independently of the
select, or select actions perform asynchronous work.

For the complete grammar, see the [grammar reference](../Reference/Grammar.md). The advanced guide
also covers select-local values, state parameters, lifecycle hooks, fallback actions, dynamic
choices, display views, script-initiated selects, and nested selects.
