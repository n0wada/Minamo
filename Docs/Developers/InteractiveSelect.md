# Interactive selects

> Status: implemented initial feature. Dynamic choice generation, nested selects, asynchronous
> waiting, state entry and exit hooks, and save/load remain deferred.

`select` defines a long-lived, host-driven interaction. It is intended for game consoles,
dialogue, shops, quests, and similar flows where the host owns the user interface while the Script
owns the available actions and state transitions. The usual entry point is C#:
`instance.OpenSelect("name")`.

Unlike an ordinary function, a select session does not run to completion for every host call. It
starts once, stops while it waits for input, and resumes in the state reached by the previous
input. The current select state remains available until the session completes, is cancelled, or
fails. Choice-local bindings have ordinary function lifetime; persistent game data remains in the
host or in module-level bindings.

## Script syntax

```kit
select player {
    initial state "stopped" {
        choose "play" => {
            music.Play()
            goto "playing"
        }

        choose "set-volume" (value) => {
            music.SetVolume(value)
        }

        choose "exit" => exit
    }

    state "playing" {
        choose "pause" => {
            music.Pause()
            goto "paused"
        }

        choose "stop" => {
            music.Stop()
            goto "stopped"
        }

        choose "track-ended" => {
            music.Next()
            music.Play()
        }

        choose "exit" => {
            music.Stop()
            exit
        }
    }

    state "paused" {
        choose "resume" => {
            music.Play()
            goto "playing"
        }

        choose "stop" => {
            music.Stop()
            goto "stopped"
        }

        choose "exit" => exit
    }
}
```

The grammar is:

```text
select-declaration
    ::= "select" identifier "{" state-declaration+ "}"

state-declaration
    ::= "initial"? "state" string-literal "{" choice-declaration* "}"

choice-declaration
    ::= "choose" string-literal parameters?
        [ "label" string-literal ]
        [ "description" string-literal ]
        [ "when" expression ]
        "=>" choice-body

parameters
    ::= "(" identifier ("," identifier)* ")"

choice-body
    ::= block | "goto" string-literal | "exit" expression?
```

Select declarations are permitted only at global (module) scope. Exactly one state is marked
`initial`. State names and choice identifiers are string literals.
State names are private implementation details; choice identifiers are the values the host sends
to the session.

`goto "name"` changes the state in which the session next waits for input. If a choice body does
not execute `goto`, the session remains in its current state. `exit` completes the session. A
state with no choices also completes the session. A transition-only choice may use the short form:

```kit
choose "guard" label "Talk to the guard" => goto "guard"
choose "leave" label "Leave the town square" => exit
```

Each `goto` target must name a state declared by the same select; otherwise compilation fails.

Within one state, choice identifiers must be unique. A choice may bind zero, one, or several
values supplied by the host. The implementation must diagnose an unknown state, a duplicate
choice identifier, an unavailable choice, and an argument shape that does not match the choice.

## Choice presentation and availability

The choice identifier is the stable value sent by the host. `label` and `description` are optional
display information for a host UI. When omitted, `label` defaults to the identifier and
`description` is absent.

```kit
choose "play"
    label "Play selected track"
    description "Start playback of the selected track"
    when music.HasSelectedTrack() => {
    music.Play()
    goto "playing"
}
```

`when` controls availability. A false result removes the choice from `Choices`, and attempting to
choose it is rejected even if it was shown by an earlier query. Guards are reevaluated whenever
the host reads `Choices` and immediately before a choice executes, so they should be free of side
effects.

A state declared with no choices completes the session. A state whose choices are temporarily
hidden by guards remains active, because a later query may make choices available.

## Hosting protocol

The host renders choices and obtains input. Script never reads from a console or owns a UI. The
host first initializes the Script file, then opens the select by its declared name or alias.

```csharp
var initialization = instance.ExecuteFile("Scripts/town.kit");
if (!initialization.Success)
    throw new InvalidOperationException(initialization.Failure?.Message);

using var town = instance.OpenSelect("game.town");

ui.Show(town.Choices);

var result = town.Choose("guard");
ui.Show(result.Choices);
```

There is no required polling loop in the Hosting API. A desktop or game UI normally calls
`Choose` from its selection event, then redraws from `SpellkitSelectResult.Choices`. The
[Quest Console example](../../Examples/QuestConsole/README.md) contains a deliberately simple
terminal adapter; its loop is console UI code, not a requirement of the select protocol.

The Hosting API is:

```csharp
public sealed class SpellkitSelectSession : IDisposable
{
    public string Name { get; }
    public IReadOnlyList<SpellkitChoice> Choices { get; }
    public bool IsCompleted { get; }

    public SpellkitSelectResult Choose(string choiceId);
    public SpellkitSelectResult Choose(string choiceId, object? argument);
    public void Cancel();
}
```

`SpellkitChoice` exposes `Id`, `Label`, `Description`, and `ParameterCount`. Hosts may display the
provided label and description directly or use the stable ID to look up localized UI text.

`Choose` has one canonical payload. With no payload, the choice has no parameters. With one
value, that value binds to a single parameter. With several values, the host passes one tuple and
the choice parameters receive its elements.

```csharp
player.Choose("play");
player.Choose("select-track", trackId);
player.Choose("set-volume", (trackId, 80));
```

```kit
choose "play" => { /* no parameters */ }
choose "select-track" (trackId) => { /* one parameter */ }
choose "set-volume" (trackId, value) => { /* tuple elements */ }
```

The result must distinguish a session that is waiting for its next input, one that completed, and
one that failed. Calling `Choose` with an identifier absent from `Choices`, with an incompatible
argument, or after completion must report a defined error. Concurrent calls to one session are
not supported.

Host events use the same protocol as user input. For example, a game can call
`town.Choose("day-ended")`. This keeps user actions and game events inside the same Script state
machine.

## Invoking a select from Script

`do qualified.name` invokes a select from Script. It is a statement, not a function call. This is
useful when Script owns the entry point as well as the flow. The host runs the select through a
configured select runner, and Script resumes after the runner completes or cancels the session.

For applications whose UI opens a menu, dialogue, or console directly, prefer the C# entry point
shown above. It keeps UI ownership explicit and does not require `UseSelect`.

```kit
setupPlayer()
do music.player
showSummary()
```

`do` remains the do-while keyword when followed by a block:

```kit
do {
    update()
} while running
```

An embedding host configures a runner only when it wants Script to invoke selects. The runner owns
the UI and must complete or cancel the supplied session before it returns.

```csharp
var environment = new SpellkitEnvironment()
    .UseSelect(select =>
    {
        while (!select.IsCompleted)
        {
            var choice = ui.Pick(select.Choices);
            select.Choose(choice.Id);
        }
    });
```

The `spk` console supplies its own runner. It supports both direct startup and a REPL entry:

```text
spk Player.kit --do music.player

kit> do music.player
```

The console shows labels and descriptions, accepts either a displayed number or a choice ID, and
accepts one string argument as `choice-id value`. `cancel` and `quit` cancel the active session.

## Host data boundary

The C# host owns game facts such as inventory, active quests, or the selected track. A select
script controls the available actions and transitions; it does not enumerate host UI data or read
input. A `when` guard asks the host whether a choice is currently available.

```kit
choose "accept"
    label "Accept the courier quest"
    when game.CanAcceptCourierQuest() => {
    game.AcceptCourierQuest()
    goto "square"
}
```

## External names

The Script name (`player` in the examples) is not necessarily the name exposed to C#. Use:

```kit
alias(player, "music.player")
```

The host opens the session by this external name:

```csharp
using var player = instance.OpenSelect("music.player");
```

`alias` is deliberately a function call rather than a declaration modifier. It registers an
external name for the select during script initialization. An alias must be a dotted Spellkit name
and duplicate aliases are rejected.

## Deferred features

The first implementation intentionally excludes dynamic choice generation, nested selects,
automatic asynchronous waiting, state entry and exit hooks, and save/load of suspended sessions.
Those features require additional rules for suspension, errors, cleanup, and serialization.

The design should nevertheless preserve the host boundary: UI and external input remain in C#,
while the script remains the authority for choices and transitions.
