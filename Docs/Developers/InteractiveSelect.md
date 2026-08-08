# Interactive selects

`select` is Spellkit's host-driven interaction primitive. It is suitable for menus, dialogue,
shops, quests, and similar flows: Script defines states and available actions, while the host owns
the UI, input events, and game data.

The feature supports named and anonymous factories, select-local declarations, VM suspension at
`do`, nested selects, guards, visible and dynamic choices, hidden host events, fallback actions,
state parameters, and state lifecycle hooks. Persistence of suspended executions remains deferred.

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
         └─ choice / event / guard closures bound to those cells
```

The factory owns its captured outer values. Each `OpenSelect` or `do` creates a fresh instance,
including its current state, select-local cells, and action / guard closures. Select locals must
appear before the state declarations.

```kit
func createShop(items) {
    mut visits = 0

    select {
        mut cartCount = 0

        initial state open {
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

    initial state stopped {
        choose "play" => {
            music.Play()
            goto playing
        }

        choose "set-volume" (value) => {
            music.SetVolume(value)
        }

        choose "exit" => exit
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

```kit
let player = select {
    initial state stopped {
        choose "exit" => exit
    }
}
```

The relevant grammar is:

```text
select-expression
    ::= "select" [ identifier ] "{" state-declaration+ "}"

state-declaration
    ::= [ "initial" ] "state" identifier parameters?
        "{" { state-hook | state-view | otherwise-declaration | choice-declaration | dynamic-choice-group | event-declaration } "}"

state-hook
    ::= ( "enter" | "leave" ) "=>" block

state-view
    ::= "view" "=>" expression

otherwise-declaration
    ::= "otherwise" "=>" choice-body

choice-declaration
    ::= "choose" string parameters?
        [ "label" string ]
        [ "description" string ]
        [ "when" expression ]
        [ "view" "=>" expression ]
        "=>" choice-body

dynamic-choice-group
    ::= "for" identifier "in" expression "{" dynamic-choice+ "}"

dynamic-choice
    ::= "choose" expression newline
        { "label" expression newline
        | "description" expression newline
        | "when" expression newline
        | "view" "=>" expression newline }
        "=>" choice-body

event-declaration
    ::= "on" string parameters? "=>" choice-body

parameters
    ::= "(" parameter { "," parameter } ")"

parameter
    ::= identifier [ ":" type-annotation ]

choice-body
    ::= block | goto-statement | exit-statement

goto-statement
    ::= "goto" identifier transition-arguments?

transition-arguments
    ::= "(" [ expression { "," expression } ] ")"

select-invocation
    ::= "do" expression
```

Exactly one state must be `initial`. State identifiers are private implementation details. Choice IDs are
the stable values sent by the host and must be unique within one state. `goto` targets must name a
state in the same select.

`goto name` changes the next waiting state. If an action does not execute `goto`, the instance
remains in its current state. `exit` completes the instance and optionally supplies its result. A
state with no choices, events, or `otherwise` handler completes immediately; an event-only state
remains active with an empty `Choices` collection. `otherwise` runs at most once per state entry
when all choices are unavailable and no host event is declared. It can `goto` or `exit`; if it
does neither, the state remains active and the fallback is not repeated until the state is entered
again.

`enter` runs after a session is created for the initial state and after a `goto` enters a state.
`leave` runs before a `goto` or `exit` leaves the current state. Both hooks must be blocks and are
side-effect hooks: they cannot `goto`, `exit`, or suspend on another select.

`view` supplies host-facing display data. A state can declare one state view, and a choice can
declare one choice view. A view is an expression terminated by a newline or semicolon; it is
therefore written before the choice action's `=>`. State views and choice views receive the current
state parameters and captured select-local values. Choice views deliberately do not receive the
host-supplied choice arguments, because the host needs them before a choice is selected. Views are
evaluated each time a snapshot is read, so they should be free of side effects and cannot start a
nested select.

State parameters carry the data for a particular state entry. A `goto` supplies the target state's
values, and those values are available as ordinary variables in the target state's `enter`, `leave`,
`otherwise`, `choose`, and `on` bodies and guards. They are prepended to the internal action
arguments, so host-supplied choice and event arguments keep their existing shape. State values are
script-side context; they are not included in `SpellkitChoice.Parameters` or exposed by the
session's `State` property. The number of expressions in a transition must match the target
state's parameter count.

```kit
select counter {
    initial state start {
        choose "begin" => goto count(0)
    }

    state count(total: Integer) {
        choose "add" (amount: Integer) => goto count(total + amount)
        choose "finish" => exit total
    }
}
```

```kit
choose "guard" label "Talk to the guard" => goto guard
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
    goto square
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

## Dynamic choices

Use a state-local `for` group to generate choices from a collection. The loop variable is available
to the generated choice ID, presentation fields, guard, view, and action body.

```kit
initial state browse {
    choose "leave" => exit

    for item in shop.Stock {
        choose item.id
            label item.name
            description item.description
            when item.available
            view => ["price": item.price, "rarity": item.rarity]
            => {
                cart.Add(item)
                goto browse
            }
    }
}
```

The source must evaluate to a Spellkit collection, such as an array, tuple, dictionary, set, or
string. It is evaluated whenever the host reads the current snapshot and again when a choice is
selected. Generated IDs must be nonempty strings and must be unique across the state's static and
dynamic choices. A missing, hidden, or changed generated choice is rejected on selection just like
a static choice whose `when` guard became false.

Dynamic choices do not currently declare host-supplied parameters: their loop item is the action's
input. Their `SpellkitChoice.Parameters` collection is therefore empty. An empty dynamic source
keeps the state active, allowing its source to become available later; `otherwise` still runs when
that source produces no visible choices and the state has no host events.

## Views and snapshots

Use `view` to keep rendering data beside the state or choice it describes. A dictionary literal is
convenient for structured UI data:

```kit
select courierQuest {
    initial state offer {
        view => ["template": "quest.offer", "title": "Courier needed"]

        choose "accept"
            label "Accept"
            view => ["style": "primary", "icon": "check"]
            => goto active("courier")
    }

    state active(questId: String) {
        view => ["template": "quest.active", "questId": questId]

        choose "leave"
            view => ["style": "secondary"]
            => exit
    }
}
```

`SpellkitSelectSession.Snapshot` returns an immutable `SpellkitSelectSnapshot` containing the
currently interactive select's name, state, visible choices, completion flag, and revision. This
also follows an active nested select, so a UI always renders the interaction that can currently
receive input. The revision starts at zero and increases after each successful select action,
cancellation, or host invalidation; use it to avoid rerendering an unchanged snapshot or to track
which UI render produced an input event.

Send the revision that produced a UI action to reject stale input atomically. A mismatch leaves
the session unchanged and throws `SpellkitSelectRevisionMismatchException`; its `Snapshot` is the
current UI state to render instead.

```csharp
try
{
    var result = town.SelectAtRevision("accept", snapshot.Revision);
    ui.Render(result.Snapshot);
}
catch (SpellkitSelectRevisionMismatchException stale)
{
    ui.Render(stale.Snapshot);
}
```

When host-owned data changes outside a select action, call `Invalidate` after updating that data.
It advances the revision and returns a newly evaluated snapshot, so actions from the previous UI
render are rejected. `Refresh` also evaluates and returns a snapshot, but deliberately preserves
the current revision; use it for an ordinary UI resync that does not make an existing render stale.

```csharp
inventory.Changed += (_, _) =>
{
    var snapshot = town.Invalidate();
    ui.Render(snapshot);
};
```

`InvalidateAsync` and `RefreshAsync` serialize with asynchronous select actions without blocking
the caller. Host-owned data must still be updated and invalidated under the host's own dispatcher
or synchronization mechanism. Do not synchronously invalidate the same session from a callback
executing one of its select actions; enqueue that notification after the action instead.

Use the three-argument overload when a choice or event has a payload. `SelectAtRevisionAsync` and
`SendAtRevisionAsync` provide the same check for asynchronous hosts.

```csharp
var snapshot = town.Snapshot;
var stateData = snapshot.State.View?
    .GetValue<Dictionary<string, object?>>();

ui.Render(snapshot.State.Id, stateData, snapshot.Choices);

foreach (var choice in snapshot.Choices)
{
    var choiceData = choice.View?.GetValue<Dictionary<string, object?>>();
    ui.AddChoice(choice.Id, choice.Label, choiceData);
}
```

The generic `GetValue<T>()` and `TryGetValue<T>()` methods use the standard host conversion rules.
For example, dictionary literals convert to `Dictionary<string, object?>`; scalar views can be
read directly as `string`, `long`, and similar host types.

## Host events

`on` declares a host event that is not included in `Choices`. Its parameters use the same payload
shape as choice parameters. The host delivers it with `Send`; state scope determines whether the
event is currently handled.

## C#-initiated selects

Use `OpenSelect` when C# owns the interaction's entry point. It resolves a named factory or alias,
then creates a new instance for this call.

```kit
select town {
    initial state square {
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
    public long Revision { get; }
    public SpellkitSelectSnapshot Snapshot { get; }
    public SpellkitSelectSnapshot Refresh();
    public Task<SpellkitSelectSnapshot> RefreshAsync();
    public SpellkitSelectSnapshot Invalidate();
    public Task<SpellkitSelectSnapshot> InvalidateAsync();
    public string State { get; }
    public IReadOnlyList<SpellkitChoice> Choices { get; }
    public bool IsCompleted { get; }

    public SpellkitSelectResult Select(string choiceId);
    public SpellkitSelectResult Select(string choiceId, object? argument);
    public SpellkitSelectResult SelectAtRevision(string choiceId, long expectedRevision);
    public SpellkitSelectResult SelectAtRevision(
        string choiceId, object? argument, long expectedRevision);
    public SpellkitSelectResult Send(string eventId);
    public SpellkitSelectResult Send(string eventId, object? argument);
    public SpellkitSelectResult SendAtRevision(string eventId, long expectedRevision);
    public SpellkitSelectResult SendAtRevision(
        string eventId, object? argument, long expectedRevision);
    public void Cancel();
}
```

`State` is the current state name. `SpellkitChoice` exposes `Id`, `Label`, `Description`, `View`,
`ParameterCount`, and `Parameters`. `SpellkitSelectSnapshot` exposes `Name`, `Revision`, `State`,
`Choices`, and `IsCompleted`; its state is a `SpellkitSelectState` with `Id` and `View`. Each
`SpellkitChoiceParameter` exposes the source parameter `Name` and its optional `TypeName`.
`Select` and `Send` return the next snapshot through `SpellkitSelectResult.Snapshot`, while the
existing `Choices` and `IsCompleted` result properties remain available for compatibility.
They report an error for unavailable IDs, invalid argument shapes, or a completed session.
`SelectAtRevision` and `SendAtRevision` additionally reject stale UI actions with
`SpellkitSelectRevisionMismatchException`. `Refresh` and `Invalidate` participate in the same
serialization as actions. Calls on one session are serialized; do not issue concurrent action
calls.

## Script-initiated selects and VM continuations

`do expression` evaluates a factory once, creates an instance, and suspends the Script VM until
that instance exits. The continuation preserves the Script instruction position, locals, evaluation
stack, function call path, and exception state. `do` is an expression whose value is supplied by
`exit`; an exit without a value produces `nil`.

```kit
func visitTown() {
    print("You arrive at the town square.")
    let result = do town
    print("The town console closes: ", result)
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

`SpellkitRunSession.Select` and `Send` either expose the next choices or resume the suspended VM
after the select exits. Disposing the run cancels its active select and releases the continuation.

Pass a dotted external name as a string:

```kit
do "music.player"
```

This resolves an alias at runtime. Every other target is an ordinary expression, so a property
that contains a factory needs no special punctuation:

```kit
do ui.currentShop
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
    let purchase = do "town.shop"
    recordPurchase(purchase)
    goto square
}
```

Cancelling or disposing an outer session/run cancels its active inner select as well. An inner
select's exit value becomes the value of the nested `do` expression.

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

Asynchronous hosts can configure `UseSelectAsync` and use the session's asynchronous actions:

```csharp
var environment = new SpellkitEnvironment()
    .UseSelectAsync(async select =>
    {
        while (!select.IsCompleted)
        {
            var choice = await ui.PickAsync(select.Choices);
            await select.SelectAsync(choice.Id);
        }
    });
```

`ExecuteAsync` awaits this runner without occupying a worker thread. Configuring either runner
replaces the previously configured runner.

The `spell` console provides its own runner for direct startup and REPL use. The
[Quest Console example](../../Examples/QuestConsole/README.md) demonstrates the event-driven
`Start` / `SpellkitRunSession` form.

## Host boundary and external names

Script controls state transitions and available actions. The C# host controls rendering, input,
event timing, and game facts such as inventory or active quests. Visible user actions use `choose`
and `Select`; hidden domain events use `on` and `Send`, for example `town.Send("day-ended")`.

`alias(factory, "dotted.name")` registers an external name during Script initialization. Alias names
must be dotted Spellkit names and duplicates are rejected.

## Deferred features

The current implementation deliberately excludes:

- public `yield` / generic `resume` syntax;
- serializing suspended VM continuations or select instances;
- concurrent action calls and multiple suspended runs in one host instance.

These limitations keep the ownership boundary clear: the host provides UI and input, while Script
defines the structured interaction flow.
