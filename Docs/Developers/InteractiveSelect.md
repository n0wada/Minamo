# Interactive selects

`select` is Spellkit's host-driven interaction primitive. It is suitable for menus, dialogue,
shops, quests, and similar flows: Script defines states and available actions, while the host owns
the UI, input events, and game data.

The feature supports named and anonymous factories, select-local declarations, VM suspension at
`do`, nested selects, guards, and host-driven choice selection. Dynamic choices, state entry/exit
hooks, and persistence of suspended executions remain deferred.

## Mental model

A `select` expression produces a reusable factory. Starting that factory creates a new interaction
instance.

```text
SelectFactory
├─ static state and choice metadata
├─ captured outer values
└─ initializer template
   └─ Create()
      └─ SelectInstance
         ├─ current state and completion status
         ├─ select-local cells
         └─ choice / guard closures bound to those cells
```

The factory owns its captured outer values. Each `OpenSelect` or `do` creates a fresh instance,
including its current state, select-local cells, and choice / guard closures. Select locals must
appear before the state declarations.

```kit
func createShop(items) {
    mut visits = 0

    select {
        mut cartCount = 0

        initial state "open" {
            choose "leave" => exit
        }
    }
}

let shop = createShop(weapons)
```

Here `visits` belongs to `shop`'s factory closure, so multiple instances created from `shop` share
it. `cartCount` belongs to each select instance, so every `OpenSelect` or `do shop` starts it at
zero.

## Script syntax

Use a named select at module scope when C# should open it by name. The named form binds a global
factory; the anonymous form is an expression.

```kit
select player {
    mut selectedTrack = nil

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
        choose "pause" => goto "paused"
        choose "stop" => goto "stopped"
        choose "exit" => exit
    }

    state "paused" {
        choose "resume" => goto "playing"
        choose "stop" => goto "stopped"
        choose "exit" => exit
    }
}
```

```kit
let player = select {
    initial state "stopped" {
        choose "exit" => exit
    }
}
```

The relevant grammar is:

```text
select-expression
    ::= "select" [ identifier ] "{" state-declaration+ "}"

state-declaration
    ::= [ "initial" ] "state" string "{" choice-declaration* "}"

choice-declaration
    ::= "choose" string parameters?
        [ "label" string ]
        [ "description" string ]
        [ "when" expression ]
        "=>" choice-body

parameters
    ::= "(" identifier { "," identifier } ")"

choice-body
    ::= block | "goto" string | "exit" [ expression ]

select-invocation
    ::= "do" expression
```

Exactly one state must be `initial`. State names are private implementation details. Choice IDs are
the stable values sent by the host and must be unique within one state. `goto` targets must name a
state in the same select.

`goto "name"` changes the next waiting state. If a choice does not execute `goto`, the instance
remains in its current state. `exit` completes the instance and optionally supplies its result.
Entering a state with no choices completes immediately; `do` then continues without suspending.

```kit
choose "guard" label "Talk to the guard" => goto "guard"
choose "leave" label "Leave" => exit
```

## Choices and guards

`label` and `description` are host-facing presentation data. When omitted, a choice's label is its
ID and its description is absent.

```kit
choose "accept"
    label "Accept the courier quest"
    description "Add the search to your active quests"
    when game.CanAcceptCourierQuest() => {
    game.AcceptCourierQuest()
    goto "square"
}
```

`when` controls availability. A false guard removes the choice from `Choices`; selecting it is
also rejected if it became unavailable after rendering. Guards run whenever the host reads
`Choices` and immediately before a choice executes, so they should be free of side effects.

Choice arguments have one canonical shape:

```csharp
player.Select("play");
player.Select("select-track", trackId);
player.Select("set-volume", (trackId, 80));
```

```kit
choose "play" => { }
choose "select-track" (trackId) => { }
choose "set-volume" (trackId, value) => { }
```

With zero parameters, no payload is accepted. With one parameter, the payload is that value. With
two or more parameters, the payload must be one tuple whose elements bind to the parameters.

## C#-initiated selects

Use `OpenSelect` when C# owns the interaction's entry point. It resolves a named factory or alias,
then creates a new instance for this call.

```kit
select town {
    initial state "square" {
        choose "leave" => exit "goodbye"
    }
}

alias(town, "game.town")
```

```csharp
var initialization = instance.ExecuteFile("Scripts/town.kit");
if (!initialization.Success)
    throw new InvalidOperationException(initialization.Failure?.Message);

using var town = instance.OpenSelect("game.town");
ui.Show(town.Choices);

var result = town.Select("leave");
if (result.IsCompleted)
    Console.WriteLine(result.GetValue<string>());
```

The session API is:

```csharp
public sealed class SpellkitSelectSession : IDisposable
{
    public string Name { get; }
    public string State { get; }
    public IReadOnlyList<SpellkitChoice> Choices { get; }
    public bool IsCompleted { get; }

    public SpellkitSelectResult Select(string choiceId);
    public SpellkitSelectResult Select(string choiceId, object? argument);
    public void Cancel();
}
```

`State` is the current state name. `SpellkitChoice` exposes `Id`, `Label`, `Description`, and
`ParameterCount`. `Select` returns the next available choices or an `IsCompleted` result. It reports an error for unavailable IDs,
invalid argument shapes, or a completed session. Calls on one session are serialized; do not issue
concurrent `Select` calls.

## Script-initiated selects and VM continuations

`do expression` evaluates a factory once, creates an instance, and suspends the Script VM until
that instance exits. The continuation preserves the Script instruction position, locals, evaluation
stack, function call path, and exception state. After `exit`, execution resumes immediately after
the `do` statement.

```kit
func visitTown() {
    print("You arrive at the town square.")
    do town
    print("The town console closes.")
}

visitTown()
```

Event-driven hosts use `Start` and keep the returned run session until it completes or is disposed.

```csharp
using var run = instance.Start("""
    let shop = createShop(items)
    do shop
    showSummary()
    """);

while (!run.IsCompleted)
{
    ui.Show(run.Choices);
    var choice = ui.Pick(run.Choices);
    run.Select(choice.Id);
}
```

`SpellkitRunSession.Select` either exposes the next choices or resumes the suspended VM after the
select exits. Disposing the run cancels its active select and releases the continuation.

For compatibility, a dotted external name remains valid after `do`:

```kit
do music.player
```

This resolves an alias at runtime. When a property expression itself is a factory, parenthesize it
to distinguish it from this shorthand:

```kit
do (ui.currentShop)
```

When `do` is immediately followed by `{`, it is parsed as the existing do-while loop rather than
a select invocation. A do-while loop must use the full `do { ... } while condition` form.

```kit
do {
    update()
} while running
```

## Nested selects

A choice can invoke another factory. While the inner select waits, its choices are the only choices
exposed to the host. When it exits, the parent choice resumes at the statement following `do`.

```kit
choose "shop" => {
    do town.shop
    goto "square"
}
```

Cancelling or disposing an outer session/run cancels its active inner select as well. The initial
version does not pass an inner select's exit value back to the parent choice; `do` is a statement.

## Synchronous adapter and console

`SpellkitEnvironment.UseSelect` remains available for synchronous hosts. The callback owns the UI
loop and must complete or cancel the supplied session before it returns.

```csharp
var environment = new SpellkitEnvironment()
    .UseSelect(select =>
    {
        while (!select.IsCompleted)
        {
            var choice = ui.Pick(select.Choices);
            select.Select(choice.Id);
        }
    });
```

The `spk` console provides its own runner for direct startup and REPL use. The
[Quest Console example](../../Examples/QuestConsole/README.md) demonstrates the event-driven
`Start` / `SpellkitRunSession` form.

## Host boundary and external names

Script controls state transitions and available actions. The C# host controls rendering, input,
event timing, and game facts such as inventory or active quests. A host may also send domain events
through a choice ID, for example `town.Select("day-ended")`.

`alias(factory, "dotted.name")` registers an external name during Script initialization. Alias names
must be dotted Spellkit names and duplicates are rejected.

## Deferred features

The current implementation deliberately excludes:

- dynamic choice generation;
- public `yield` / generic `resume` syntax;
- state entry and exit hooks;
- serializing suspended VM continuations or select instances;
- concurrent `Select` calls and multiple suspended runs in one host instance.

These limitations keep the ownership boundary clear: the host provides UI and input, while Script
defines the structured interaction flow.
