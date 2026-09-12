# Advanced interactive selects

This page covers the parts of `select` intended for richer UI integrations and script composition.
Start with [Interactive selects](InteractiveSelect.md) for the ordinary `OpenSelectAsync`
loop.

## Published UI state and choice identity

`OpenSelectAsync` returns one live `MinamoSelect` object. Its `Description`, `Properties`,
`Choices`, `Request`, and `IsCompleted` properties are the most recently published UI state.
Reading them does not execute Minamo code; opening the select and completing a case, event, or
response publish a new state asynchronously. A failed publication leaves the preceding published
state unchanged.

Each successful publication creates new `MinamoChoice` objects. Pass a choice object directly to
`SelectAsync` when the UI keeps those objects: a choice from an earlier publication is rejected.
Resolve an ID against the current `Choices` when the host keeps only string IDs. Serializing choices
for a remote UI loses object identity. If delayed input is possible, the host must associate an
opaque token with each render and validate it on the same serialized dispatcher that invokes the
select.

```csharp
using var town = await instance.OpenSelectAsync("game.town");

var choice = ui.Pick(town.Choices);
await town.SelectAsync(choice);
ui.Render(town.Choices);
```

### Updating host-owned data

Declare a host event when changing host-owned data must reevaluate the choices. After its action
finishes, Minamo republishes the choices and rejects choice objects from the previous publication.
If reevaluation fails, the preceding publication remains current.

```nami
select inventoryView {
    on "inventory-changed" => { }
    case "checkout" when inventory.CanCheckout() => exit
}
```

```csharp
inventory.Apply(change);
await inventoryView.SendAsync("inventory-changed");
ui.Render(inventoryView.Choices);
```

Host events serialize with case actions. Update host-owned data and send the corresponding event
through the host's own dispatcher. Event callbacks should queue this sequence on that dispatcher.

### Select descriptions

`desc` puts a free-form dictionary description next to a select. It appears first in the select
body, uses no `=>`, and must be a dictionary literal with string keys. It is evaluated once when
the interaction opens. Its primary role is explanatory text or an AI-generation prompt, though a
host may also read broad layout hints from it.

```nami
select courierQuest {
    desc [
        "prompt": "Present this as a compact courier quest panel",
        "layout": "Put the quest summary above the available operations"
    ]

    mut accepted = false

    prop accepted ["control": "status"] => accepted

    case "accept" when !accepted ["text": "Accept", "control": "button"] => {
        accepted = true
    }

    case "leave" when accepted => exit
}
```

`MinamoSelectDescription.GetValue<T>()` and `TryGetValue<T>()` use standard host conversion. For
example, a dictionary description can be read as `Dictionary<string, object?>`.

### Published properties and member metadata

`prop` exposes a read-only value without exposing its select-local storage. Properties are
reevaluated on every publication and returned as `MinamoSelect.Properties`. Each published
`MinamoSelectProperty` has a `Name`, optional `Metadata`, and typed `GetValue<T>()` and
`TryGetValue<T>()` accessors.

```nami
select player {
    mut volume = 50

    prop volume [
        "format": "percentage",
        "importance": "primary"
    ] => volume

    case "set-volume" (value) [
        "control": "slider",
        "bind": "volume",
        "min": 0,
        "max": 100
    ] => {
        volume = value
    }
}
```

The dictionary immediately following a `prop` or `case` is optional free-form metadata. String
keys are required, but Minamo assigns no meaning to values such as `control`, `text`, `icon`,
`shape`, or `bind`. A CLI may ignore them, a conventional GUI may recognize a subset, and an AI UI
generator may use all of them as hints. Metadata never grants permission to invoke anything: only
currently published `MinamoChoice` objects can be selected.

Property values and metadata are snapshots. Objects retained from an earlier publication keep
their old values, just as retained choices become stale. A failed property, metadata, or guard
evaluation leaves the preceding published state unchanged.

## Select-local values and factories

A select expression is a reusable factory. Each `OpenSelectAsync`
creates a new interaction instance, including its select-local values. Captured outer
variables belong to the factory and are shared by its instances.

```nami
func createShop() {
    mut visits = 0

    select {
        mut cartCount = 0

        case "add" => { cartCount += 1 }
        case "leave" => {
            visits += 1
            exit (visits, cartCount)
        }
    }
}

let shop = createShop()
```

In this example, `visits` belongs to the `shop` factory closure and is shared by interactions opened
from that factory, while `cartCount` starts again for each interaction. Select-local declarations
must precede properties, cases, and events. Actions can update those values; published properties,
metadata, and guards are then reevaluated together.

## Navigation stack

`goto expression` evaluates a select-factory expression and creates a fresh target instance. The
current instance and its description are pushed onto an interaction-owned navigation stack. The
same `MinamoSelect` object then publishes the target instance's `Name`, `Description`, `Properties`,
and `Choices`.

```nami
func createDetails(itemId) {
    select {
        desc ["template": "details", "itemId": itemId]
        case "close" => return
        case "delete" => exit ("deleted", itemId)
    }
}

select browser {
    mut opened = false

    case "open" (itemId) when !opened => {
        opened = true
        goto createDetails(itemId)
    }

    case "finish" when opened => exit "closed"
}
```

The control operations have deliberately different scopes:

- `goto select-expression` pushes the current instance and enters a new one.
- `return` completes only the current instance with `nil` and pops one stack frame. At the root,
  it completes the interaction with `nil`.
- `exit value` completes the entire interaction and clears the stack. Its optional value becomes
  the host-visible completion value.

There is no automatic case named "back" and no `back` keyword. A script exposes navigation by
placing `return` in whichever case or host event should go up one level. A select-level `return`
does not accept a value. Inside an explicitly declared nested function, ordinary `return value`
continues to return from that function rather than navigating.

Each `goto` creates a new target instance even when it evaluates the same factory again. Instances
already on the stack retain their select-local values. Their guards are reevaluated when `return`
restores them, while their descriptions retain the value evaluated when those instances were first
entered.

### Empty interactions

A select returns with `nil` when publication produces no available choices and it has no host event.
If it is nested, the preceding stack entry is restored; at the root, the interaction completes.
This applies both when a select opens empty and when an action makes its last case unavailable.
Use `exit value` when the entire interaction should finish with a meaningful result. An event-only
select stays active with an empty choice list.

## Exposing factory expressions to the host

The host opens a select by its global variable name or an alias. To expose a factory produced by a
function or stored in an object, bind it to a global variable or register it with `alias`:

```nami
let shop = createShop()
alias(shop, "town.shop")
```

```csharp
using var shop = await instance.OpenSelectAsync("town.shop");
var leave = shop.Choices.Single(choice => choice.Id == "leave");
await shop.SelectAsync(leave);
var result = shop.GetValue<object[]>();
```

Scripts declare factories and actions; the host controls when to open each root interaction, while
`goto` composes further factory instances within it. Use select-local values and case guards when
later choices depend on earlier actions.

## Asynchronous and event-driven hosts

Use `SelectAsync` and `SendAsync` to drive a select. Calls on one select are serialized; do not
issue concurrent actions.

```csharp
using var select = await instance.OpenSelectAsync("dialog");
var confirm = select.Choices.Single(choice => choice.Id == "confirm");
await select.SelectAsync(confirm);
```

Automated tests can use this same API. The console also drives `OpenSelectAsync` and `SelectAsync`
for manual tests through `--do name` or the REPL command `do name`; neither is a script expression.

## Host boundary and limits

Script owns navigation, published properties, available cases, and select-local values. The host
owns rendering, event timing, and external facts such as inventory or network state. When those
facts change, send a declared host event before accepting further input. Passing the displayed
`MinamoChoice` object lets the select reject input from a publication that has since been replaced.

Current limits are deliberate:

- serialized select actions rather than concurrent actions;
- no public serialization of select instances.

For exact syntax and all validation rules, see the [grammar reference](../Reference/Grammar.md).
