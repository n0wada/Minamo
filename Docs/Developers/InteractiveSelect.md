# Interactive selects

> Status: implemented initial feature. Dynamic choice generation, nested selects, asynchronous
> waiting, state entry and exit hooks, and save/load remain deferred.

`select` defines a long-lived, host-driven interaction. It is intended for game consoles,
dialogue, shops, quests, and similar flows where the host owns the user interface while the script
owns the available actions and state transitions.

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
    ::= block | "exit" expression?
```

Select declarations are permitted only at global (module) scope. Exactly one state is marked
`initial`. State names and choice identifiers are string literals.
State names are private implementation details; choice identifiers are the values the host sends
to the session.

`goto "name"` changes the state in which the session next waits for input. If a choice body does
not execute `goto`, the session remains in its current state. `exit` completes the session. A
state with no choices also completes the session.

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

The host renders choices and obtains input. The script never reads from a console or owns a UI.
The session API should therefore expose the current choices and accept the selected choice with
one optional argument.

```csharp
using var player = instance.OpenSelect("music.player");

foreach (var choice in player.Choices)
{
    console.AddChoice(choice);
}

player.Choose("play");
player.Choose("set-volume", 80);
player.Choose("move", (12, 34));
```

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

Host events use the same protocol as user input. For example, when a song ends, C# can call
`player.Choose("track-ended")`. This keeps user actions and game events inside the same script
state machine.

## Invoking a select

`do qualified.name` invokes a select from Script. It is a statement, not a function call. The
host runs the select through its configured select runner, and Script resumes after the runner
completes or cancels the session.

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

An embedding host configures the runner when it wants Script to invoke selects. The runner owns
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

Hosts that start a select themselves can continue to call `instance.OpenSelect("music.player")`
directly. This is useful when a host UI, rather than a Script statement, owns the entry point.

The `spk` console supplies its own runner. It supports both direct startup and a REPL entry:

```text
spk Player.kit --do music.player

kit> do music.player
```

The console shows labels and descriptions, accepts either a displayed number or a choice ID, and
accepts one string argument as `choice-id value`. `cancel` and `quit` cancel the active session.

## Music selection boundary

Song libraries, track pickers, playback queues, and the active track belong to the C# host. A
select script controls operations such as play, pause, and stop; it does not need to enumerate
tracks or read input.

```csharp
var selected = trackPicker.Show(musicLibrary.GetTracks());
if (selected is not null)
{
    musicPlayer.SetTrack(selected.Id);
}

player.Choose("play");
```

The script's `music.Play()` command then uses the track selected by the host. Passing a track ID
through `Choose("play", trackId)` remains available when a script genuinely needs that value, but
is not required for the normal picker workflow.

## External names

The script name (`player` in the examples) is not necessarily the name exposed to C#. The planned
configuration function is:

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
