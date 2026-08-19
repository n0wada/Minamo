# Advanced interactive selects

This page covers the parts of `select` intended for richer UI integrations and script composition.
Start with [Interactive selects](InteractiveSelect.md) for the ordinary `OpenSelectAsync`
loop.

## Published UI state and revisions

`OpenSelectAsync` returns one live `SpellkitSelect` object. Its `State`, `Description`, `Choices`,
and `IsCompleted` properties are the most recently published UI state. Reading them does not
execute Spellkit code; opening the select, completing an action, refreshing, and invalidating
publish a new state asynchronously. A failed publication leaves the preceding published state
unchanged.

Each published choice has a `Revision`. A local UI can pass the choice object directly to
`SelectAsync`. A remote UI can send its ID and revision back to the host, which rejects input from
an earlier published state.

```csharp
using var town = await instance.OpenSelectAsync("game.town");

ui.Render(town.State, town.Choices);

try
{
    await town.SelectAtRevisionAsync(request.ChoiceId, request.Revision);
    ui.Render(town.State, town.Choices);
}
catch (SpellkitSelectRevisionMismatchException stale)
{
    ui.Render(town.State, town.Choices);
}
```

The revision starts at zero and advances after a successful action, cancellation, or invalidation.
Pass the revision that produced a UI event to `SelectAtRevisionAsync` or `SendAtRevisionAsync`.
A mismatch leaves the select unchanged; read `State` and `Choices` again to render the current
published state.

### Refreshing host-owned data

`RefreshAsync` republishes choices without changing the revision. Use it when older UI
input remains valid. `InvalidateAsync` advances the revision, then republishes, making input from
older renders stale.

```csharp
inventory.Changed += async (_, _) =>
{
    await town.InvalidateAsync();
    ui.Render(town.State, town.Choices);
};
```

These operations serialize with select actions without blocking the caller. Update host-owned data
and invalidate it through the host's own dispatcher. Do not await an invalidation from a callback
already executing one of the session's actions; queue that notification instead.

### Select descriptions

`desc` puts host-facing dictionary metadata next to a select. It appears first in the select body,
uses no `=>`, and must be a dictionary literal with string keys. It is evaluated once when the
interaction opens.

```kit
select courierQuest {
    desc ["template": "quest", "title": "Courier needed"]

    mut questId = ""

    initial state offer {
        choose "accept"
            label "Accept"
            => {
                questId = "courier"
                goto active
            }
    }

    state active {
        choose "leave" => exit
    }
}
```

`SpellkitSelectDescription.GetValue<T>()` and `TryGetValue<T>()` use standard host conversion. For
example, a dictionary description can be read as `Dictionary<string, object?>`.

## Stateful select declarations

A select expression is a reusable factory. Each `OpenSelectAsync` or `do`
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

### Empty-choice actions

`on empty` runs once after entering a state when no choice is available and the state has no host
event. It can use `goto` or `exit` to recover from an empty state.

```kit
initial state gate {
    choose "continue" when account.ready => goto next
    on empty => exit "Account is not ready"
}
```

A state with no choices, events, or fallback action completes immediately. An event-only state
stays active with an empty choice list.

### Dynamic choices

Put `for` on a `choose` declaration to generate one choice for each item in a Spellkit collection.

```kit
initial state browse {
    choose "leave" => exit

    choose item.id
        label item.name
        for item in shop.Stock
        when item.available
        => {
        cart.Add(item)
        goto browse
    }
}
```

The source is reevaluated when Spellkit publishes the UI state: on opening, after an action, and
after `RefreshAsync` or `InvalidateAsync`. Generated IDs must be nonempty and unique among all
choices in the state. Dynamic choices currently have no host-supplied parameters: the loop item is
their action input.

## Script-initiated and nested selects

`do expression` starts a select factory and suspends the Spellkit VM until it exits. The expression
evaluates to the value supplied by `exit`.

```kit
func visitTown() {
    let result = do town
    print("Town closed: ", result)
}
```

A choice can start another select. While it is active, the host sees only the inner select's current
state and choices. When it exits, the parent choice resumes after `do`.

```kit
choose "shop" => {
    let purchase = do "town.shop"
    recordPurchase(purchase)
    goto square
}
```

`do "dotted.name"` resolves a Script alias registered with `alias(factory, "dotted.name")`.
Disposing or cancelling an outer session also cancels its active nested select.

## Expanded child choices

Use `choose ...child` inside a named parent state to add the choices of a state-less child select.
The child keeps its own select-local values, but is directly composed into the parent interaction.
State-less parent selects cannot use choice spreads.

```kit
select filters {
    mut showArchived = false

    choose "toggle archived" => { showArchived = not showArchived }
}

select browser {
    initial state browsing {
        choose "close" => exit
        choose ...filters
    }
}
```

The child must be state-less. Its `on empty`, `enter` / `leave`, and host-event handlers run before
the corresponding parent handlers. Child `exit` exits the parent select; child `goto` targets a
state of the parent and is resolved at run time. A child `desc` is valid but does not change the
parent's `Description`. If the child and parent both handle the same event or both define related
hooks, that composition is intentional and its effects are the script author's responsibility.

## Asynchronous and event-driven hosts

Use `SelectAsync`, `SendAsync`, `SelectAtRevisionAsync`, and `SendAtRevisionAsync` to drive a
select. Calls on one select are serialized; do not issue concurrent actions.

```csharp
using var select = await instance.OpenSelectAsync("dialog");
var result = await select.SelectAsync("confirm");
```

When a script itself executes `do`, start it with `StartAsync`. The resulting
`SpellkitRunSession` exposes the active `State`, `Description`, `Choices`, and nullable `Revision`.
It also supports the same choice, revision, refresh, and invalidation operations while the VM is
waiting for a select.

```csharp
using var run = await instance.StartAsync(source);
while (!run.IsCompleted)
{
    var choice = await ui.PickAsync(run.Choices);
    await run.SelectAsync(choice);
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
