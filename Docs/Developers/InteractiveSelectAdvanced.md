# Advanced interactive selects

This page covers the parts of `select` intended for richer UI integrations and script composition.
Start with [Interactive selects](InteractiveSelect.md) for the ordinary `OpenSelectAsync`
loop.

## Advanced sessions and snapshots

`SpellkitInstance.OpenSelectSessionAsync` returns `SpellkitSelectSession`. Use it when the host needs a stable
UI frame, stale-input rejection, invalidation after host-owned changes, or select actions that
await host work. Session operations that can run Spellkit code are asynchronous.

`Snapshot` is not required for ordinary local UI loops. It is one coherent description of a render:
the interactive select name, state and state view, visible choices and choice views, completion
state, and a Revision number. When a nested select is active, the snapshot follows the nested
interaction because that is the one that can receive input.

```csharp
using var town = await instance.OpenSelectSessionAsync("game.town");

var snapshot = town.Snapshot;
ui.Render(snapshot.State.Id, snapshot.Choices);

try
{
    var result = await town.SelectAtRevisionAsync("accept", snapshot.Revision);
    ui.Render(result.Snapshot.State.Id, result.Snapshot.Choices);
}
catch (SpellkitSelectRevisionMismatchException stale)
{
    ui.Render(stale.Snapshot.State.Id, stale.Snapshot.Choices);
}
```

The revision starts at zero and advances after a successful action, cancellation, or invalidation.
Pass the revision that produced a UI event to `SelectAtRevision` or `SendAtRevision`; a mismatch
leaves the session unchanged and provides a current snapshot in
`SpellkitSelectRevisionMismatchException`.

### Refreshing host-owned data

`RefreshAsync` reevaluates a snapshot without changing its revision. Use it when a UI needs an
ordinary resync. `InvalidateAsync` reevaluates the snapshot and advances its revision, making
input from older renders stale.

```csharp
inventory.Changed += async (_, _) =>
{
    var snapshot = await town.InvalidateAsync();
    ui.Render(snapshot.State.Id, snapshot.Choices);
};
```

These operations serialize with select actions without blocking the caller. Update host-owned data
and invalidate it through the host's own dispatcher. Do not await an invalidation from a callback
already executing one of the session's actions; queue that notification instead.

### Display views

`view` puts rendering data next to the state or choice it describes. Views are evaluated when a
snapshot is created and must be side-effect free.

```kit
select courierQuest {
    mut questId = ""

    initial state offer {
        view => ["template": "quest.offer", "title": "Courier needed"]

        choose "accept"
            label "Accept"
            view => ["style": "primary", "icon": "check"]
            => {
                questId = "courier"
                goto active
            }
    }

    state active {
        view => ["template": "quest.active", "questId": questId]
        choose "leave" => exit
    }
}
```

`SpellkitSelectView.GetValue<T>()` and `TryGetValue<T>()` use standard host conversion. For
example, a dictionary view can be read as `Dictionary<string, object?>`.

## Stateful select declarations

A select expression is a reusable factory. Each `OpenSelectAsync`, `OpenSelectSessionAsync`, or `do`
creates a new interaction instance, including its state and select-local values. Captured outer
variables belong to the factory and are shared by its instances.

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
```

In this example, `visits` belongs to the factory closure while `cartCount` starts again for each
select instance. Select-local declarations must precede the state declarations.

### Lifecycle hooks

Use select-local values when state actions need to share per-session data. `enter` and `leave`
run when a state is entered or left.

```kit
select counter {
    mut total = 0

    initial state start {
        choose "begin" => {
            total = 0
            goto count
        }
    }

    state count {
        enter => {
            print("Entered count")
        }
        choose "add" (amount: Integer) => {
            total += amount
            goto count
        }
        choose "finish" => exit total
        leave => {
            print("Leaving count")
        }
    }
}
```

`enter` runs after the initial state is created and after `goto`; `leave` runs before a `goto` or
`exit` leaves a state. Both are side-effect hooks: they cannot themselves `goto`, `exit`, or start
a nested select.

### Fallback actions

`otherwise` runs once after entering a state when no choice is available and the state has no host
event. It can use `goto` or `exit` to recover from an empty state.

```kit
initial state gate {
    choose "continue" when account.ready => goto next
    otherwise => exit "Account is not ready"
}
```

A state with no choices, events, or fallback action completes immediately. An event-only state
stays active with an empty choice list.

### Dynamic choices

Use a state-local `for` group to generate choices from a Spellkit collection.

```kit
initial state browse {
    choose "leave" => exit

    for item in shop.Stock {
        choose item.id
            label item.name
            when item.available
            view => ["price": item.price]
            => {
            cart.Add(item)
            goto browse
        }
    }
}
```

The source is reevaluated whenever the host reads choices or a snapshot and again immediately
before selection. Generated IDs must be nonempty and unique among all choices in the state. Dynamic
choices currently have no host-supplied parameters: the loop item is their action input.

## Script-initiated and nested selects

`do expression` starts a select factory and suspends the Spellkit VM until it exits. The expression
evaluates to the value supplied by `exit`.

```kit
func visitTown() {
    let result = do town
    print("Town closed: ", result)
}
```

A choice can start another select. While it is active, the host sees only the inner select's choices
or snapshot. When it exits, the parent choice resumes after `do`.

```kit
choose "shop" => {
    let purchase = do "town.shop"
    recordPurchase(purchase)
    goto square
}
```

`do "dotted.name"` resolves a Script alias registered with `alias(factory, "dotted.name")`.
Disposing or cancelling an outer session also cancels its active nested select.

## Asynchronous and event-driven hosts

Use `SelectAsync`, `SendAsync`, `SelectAtRevisionAsync`, and `SendAtRevisionAsync` to drive a
session. Calls on one session are serialized; do not issue concurrent actions.

```csharp
using var select = await instance.OpenSelectSessionAsync("dialog");
var result = await select.SelectAsync("confirm");
```

When a script itself executes `do`, start it with `StartAsync`. The resulting
`SpellkitRunSession` exposes the active choices and resumes the suspended VM as selections arrive.

```csharp
using var run = await instance.StartAsync(source);
while (!run.IsCompleted)
{
    var choice = await ui.PickAsync(run.Choices);
    await run.SelectAsync(choice.Id);
}
```

`SpellkitEnvironment.UseSelectAsync` installs a host-wide select runner for scripts that execute
`do`. The `spell` console supplies its own runner.

## Host boundary and limits

Script owns state transitions and available actions. The host owns rendering, event timing, and
external facts such as inventory or network state. A guarded or generated choice can change after
it was displayed; selection is checked again before its action runs.

Current limits are deliberate:

- one active suspended run per `SpellkitInstance`;
- serialized select actions rather than concurrent actions;
- no public serialization of suspended VM continuations or select instances.

For exact syntax and all validation rules, see the [grammar reference](../Reference/Grammar.md).
