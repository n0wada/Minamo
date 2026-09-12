# Interactive selects

`select` describes a host-driven interaction such as a menu, dialogue, shop, game screen, or GUI
view model. The script publishes data and available operations; the host decides how to render them
and sends user or application input back through the C# API.

Language-level `case` declarations appear as `MinamoChoice` objects in C#. This guide uses *case*
for the language construct and *choice* for its published host representation.

## Quick start

Declare a named select at module scope:

```nami
select town {
    case "leave" => exit "goodbye"
}
```

Execute the declarations, open a new interaction, and pass a currently published choice back to
the select:

```csharp
var initialization = await instance.ExecuteAsync("""
    select town {
        case "leave" => exit "goodbye"
    }
    """);
if (!initialization.Success)
    throw new InvalidOperationException(initialization.Failure?.Message);

using var town = await instance.OpenSelectAsync("town");
while (!town.IsCompleted)
{
    var choice = ui.Pick(town.Choices);
    await town.SelectAsync(choice);
}

Console.WriteLine(town.GetValue<string>());
```

This minimal loop assumes every published screen has a visible choice and no action calls
`request`. The later sections cover requests and event-only screens.

`MinamoSelect` is a live handle whose properties describe the latest published state:

| Member | Purpose |
| --- | --- |
| `Name` | Declared name of the select currently at the top of the navigation stack. |
| `Description` | Free-form dictionary description declared with `desc`. |
| `Properties` | Read-only values published by `prop` declarations. |
| `Choices` | Currently visible cases, represented as `MinamoChoice` objects. |
| `Request` | Input currently requested by a running case action, or `null`. |
| `IsCompleted` | Whether the whole interaction has completed. |
| `SelectAsync(choice, argument?)` | Runs a choice from the current publication. |
| `SendAsync(id, argument?)` | Delivers a hidden host event declared with `on`. |
| `RespondAsync(request, response?)` | Resumes the action that yielded the request. |
| `GetValue<T>()` / `TryGetValue<T>()` | Reads the completion value. |
| `Dispose()` | Abandons the interaction and releases it. |

Each call to `OpenSelectAsync` creates a fresh root select instance and fresh select-local values.
After an ordinary action, Minamo republishes the same interaction through the same `MinamoSelect`
object.

## Declaration model

A select can contain one description, private local values, published properties, visible cases,
and hidden host events:

```nami
select player {
    desc ["prompt": "Present a compact music player"]

    mut playing = false

    prop playing ["control": "status"] => playing

    case "play" when !playing [
        "text": "Play",
        "control": "button"
    ] => {
        playing = true
    }

    case "stop" when playing ["text": "Stop"] => {
        playing = false
    }

    on "close" => exit
}
```

`desc`, when present, comes first. All `let` and `mut` select locals follow it and must precede
`prop`, `case`, and `on` declarations. Properties, cases, and events may then be interleaved.

Case syntax follows this order:

```text
case "id" [ select-parameters ] [ "when" guard ] [ select-metadata ] "=>" action
```

The guard therefore precedes case metadata. A case action must be a block or a direct `goto`,
`return`, or `exit` control statement.

### Description, properties, and metadata

`desc` is a free-form string-key dictionary evaluated once for each select instance, including an
instance created as a `goto` target. Its main role is explanatory text or an AI-generation prompt,
although a host may also use broad layout hints from it.

`prop` exposes a read-only value without exposing the select-local storage behind it. Property
names may be identifiers or strings:

```nami
prop count ["format": "number"] => count
prop "status-text" => fmt("Count: {0}", count)
```

```csharp
var count = select.Properties
    .Single(property => property.Name == "count")
    .GetValue<long>();
```

The optional dictionary after a property or after a case's optional `when` guard is free-form UI
metadata. Keys must be strings, but Minamo assigns no meaning to values such as `text`, `control`,
`icon`, `shape`, `format`, or `bind`. A CLI may ignore them, a conventional GUI may recognize a
subset, and an AI UI generator may use all of them as hints.

`MinamoSelectDescription`, `MinamoSelectProperty`, and `MinamoSelectMetadata` provide typed
`GetValue<T>()` and `TryGetValue<T>()` conversion. For example, a metadata dictionary can be read as
`Dictionary<string, object?>`. The bundled console treats a string-valued `text` entry as display
text and falls back to the case ID; this is a host convention, not language semantics.

### Publication and evaluation timing

Opening a select publishes its first screen. Minamo publishes again after a case or host event
finishes normally, after a request response lets its action finish, after entering a `goto` target,
and after `return` restores the previous instance.

| Declaration | Evaluation time |
| --- | --- |
| `desc` | Once when its select instance is created. |
| `prop` value and metadata | On every screen publication. |
| `case when` guard | On every screen publication. |
| Available case metadata | On every screen publication, after its guard succeeds. |
| Case or `on` action | Only when invoked by the host. |

Published properties and metadata are snapshots. Objects retained from an earlier publication keep
their old values. Guards, properties, and metadata should be free of side effects because the host
controls when interactions are driven and republished.

When an ordinary republication fails while evaluating a guard, property, or metadata dictionary,
the operation throws and does not replace the last successfully published state with partially
evaluated data.

## Selecting cases and passing arguments

Always select a `MinamoChoice` from the current `Choices` collection. There is intentionally no
string overload of `SelectAsync`; resolving an ID is a normal collection lookup:

```csharp
MinamoChoice Choice(string id) =>
    player.Choices.Single(choice => choice.Id == id);

await player.SelectAsync(Choice("play"));
```

Each successful screen publication creates fresh choice objects. An object retained from an older
publication is rejected, even if a case with the same ID is still visible. This prevents a local UI
from accidentally applying an input to a screen that has already changed.

A remote UI cannot preserve .NET object identity. Associate an opaque render token with the
published choices and validate that token on the same serialized dispatcher that resolves the
current ID and calls `SelectAsync`.

Case parameters determine the host payload shape:

```nami
case "play" => { }
case "select-track" (trackId) => { }
case "set-volume" (trackId, value) => { }
```

```csharp
await player.SelectAsync(Choice("play"));
await player.SelectAsync(Choice("select-track"), trackId);
await player.SelectAsync(Choice("set-volume"), (trackId, 80));
```

No parameter accepts no payload. One parameter receives the supplied value. Two or more parameters
receive one C# tuple with the same number of elements.

## Requests from case actions

Call `request(kind, payload?)` when a case action needs additional host input before it can finish:

```nami
select profile {
    case "rename" => {
        let name = request("text", ["prompt": "New name"])
        exit name
    }
}
```

The action yields without blocking a thread. The publication retains its properties but exposes no
choices until the host responds:

```csharp
var rename = profile.Choices.Single(choice => choice.Id == "rename");
await profile.SelectAsync(rename);

while (profile.Request is { } request)
{
    var response = await ui.GetResponseAsync(
        request.Kind,
        request.GetPayload<Dictionary<string, object?>>());
    await profile.RespondAsync(request, response);
}
```

An action may yield more than one request. Each published request accepts exactly one response and
must be passed back to `RespondAsync`; an older request object is rejected. While a request is
pending, choice selection and event delivery are rejected. `request` is not available in host
events, guards, properties, metadata, or descriptions. Disposing the select abandons a pending
request.

## Host events

`on` declares input that is not displayed in `Choices`. Use it for host-owned domain events such as
a timer, completed download, or inventory update:

```nami
select download {
    prop progress => downloads.Progress()
    on "progress-changed" => { }
    on "completed" (fileName) => exit fileName
    case "cancel" => exit nil
}
```

```csharp
await download.SendAsync("progress-changed");
await download.SendAsync("completed", fileName);
var completedFile = download.GetValue<string>();
```

`SendAsync` uses the same argument-shape rules as `SelectAsync`. Sending an event republishes the
screen after its action finishes, making it the explicit mechanism for reflecting changed
host-owned data. An undeclared event ID is rejected.

## Navigation and factories

A select expression produces a reusable factory. Select-local values belong to an instance created
from that factory, while captured outer variables belong to the factory closure and are shared by
its instances:

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
alias(shop, "town.shop")
```

The host opens a factory by its global variable name or registered alias:

```csharp
using var shop = await instance.OpenSelectAsync("town.shop");
```

Within an interaction, `goto select-expression` evaluates another factory, creates a fresh target
instance, and pushes the current instance onto an interaction-owned navigation stack:

```nami
func createDetails(itemId) {
    select {
        desc ["prompt": "Show item details", "itemId": itemId]
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

The control operations have different scopes:

- `goto select-expression` pushes the current instance and enters a new one.
- `return` completes the current instance with `nil` and restores one stack frame. At the root, it
  completes the whole interaction with `nil`.
- `exit value` completes the whole interaction from any depth, clears the stack, and publishes its
  optional value as the result.

The same `MinamoSelect` object publishes the active instance's `Name`, `Description`, `Properties`,
and `Choices`. Each `goto` creates a new instance even when it evaluates the same factory again.
Instances already on the stack retain their local values; `return` reevaluates their published
state, while their descriptions retain the value evaluated when those instances were created.

There is no automatic case named `back` and no `back` keyword. Put `return` in whichever case or
host event should move up one level. A select-level `return` cannot carry a value. Inside an
explicitly declared nested function, ordinary `return value` still returns from that function.

## Completion and empty selects

`exit`, root-level `return`, and an empty root select complete the interaction. Call `GetValue<T>()`
only after `IsCompleted` becomes true; before then it throws. `TryGetValue<T>()` returns `false`
while the interaction is active.

When a publication finds no available cases and the active select declares no host events, Minamo
implicitly returns from that select with `nil`. A nested select restores its caller; a root select
completes. Properties alone do not keep an otherwise empty select active and are not published for
that empty screen. An event-only select remains active with an empty `Choices` list and may publish
properties.

Use `exit value` when the whole interaction should finish with a meaningful result. `Dispose()`
abandons the interaction rather than producing a script completion value.

## Concurrency, errors, and limits

Operations on one `MinamoSelect` are serialized internally. Hosts should still avoid issuing
concurrent actions: after the first operation republishes the screen, another operation may hold a
stale choice or conflict with a pending request. Coordinate host-owned data changes and the
corresponding `SendAsync` call through the host's own dispatcher.

The current boundaries are deliberate:

- cases and host events execute serially rather than concurrently;
- only currently published choice and request objects are accepted;
- select instances have no public serialization format;
- scripts own navigation, local values, properties, and available cases, while hosts own rendering,
  event timing, and external state.

## Testing selects

Automated tests use the application API: execute declarations, call `OpenSelectAsync`, inspect the
published properties and choices, and drive `SelectAsync`, `SendAsync`, or `RespondAsync`. Assert
the next publication after each action, or read the completion value after `IsCompleted`.

For a manual test, let the bundled console act as the host:

```powershell
minamo.exe town.nami --do town
```

The console loads the file and calls `OpenSelectAsync("town")`. In the REPL, `do town` has the same
behavior. These are console testing commands, not language syntax; `do expression` is not accepted
in scripts.

For exact syntax and validation rules, see the [grammar reference](../Reference/Grammar.md).
