# Interactive selects

`select` lets a Minamo script describe a menu, dialogue, shop, or similar interaction. The script
publishes cases and properties; the C# host renders them and sends a selected case back as a
`MinamoChoice` object.

This page covers the normal host integration. It uses `OpenSelectAsync` and its small
`MinamoSelect` API. For choice identity and reusable factories, see
[Advanced interactive selects](InteractiveSelectAdvanced.md).

## Quick start

Declare a named select at module scope. For a single-screen interaction, cases can appear directly
in the select body.

```nami
select town {
    case "leave" => exit "goodbye"
}
```

Execute the script, then open a new interaction and drive it with the currently available choices.

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

This minimal loop assumes its actions do not call `request`. When an action requests additional
input, respond as shown below before asking the user to pick another choice.

`MinamoSelect` exposes only the operations needed for this loop:

| Member | Purpose |
| --- | --- |
| `Name` | Declared name of the select currently at the top of the navigation stack. |
| `Description` | Free-form dictionary description declared for the select. |
| `Properties` | Read-only values published by `prop` declarations. |
| `Choices` | The currently visible language-level cases, represented as `MinamoChoice` objects. |
| `IsCompleted` | Whether the select has exited. |
| `Request` | Input currently requested by a running action, or `null`. |
| `SelectAsync(choice, argument?)` | Runs a visible choice from the published UI state. |
| `SendAsync(id, argument?)` | Delivers a hidden host event declared with `on`. |
| `RespondAsync(request, response?)` | Resumes the action that yielded the request. |
| `GetValue<T>()` / `TryGetValue<T>()` | Reads the exit value after completion. |
| `Dispose()` | Abandons the interaction and releases it. |

Each call to `OpenSelectAsync` creates a new interaction with its own select-local values. A select
remains active and republishes its properties and choices after an ordinary action. Navigation
changes the same live `MinamoSelect` object, so the host loop does not need special routing code.

## Requests from actions

Call `request(kind, payload?)` when an action needs host input before it can finish. The action
yields to the host, and the current publication contains no choices until the host responds.

```nami
select profile {
    case "rename" => {
        let name = request("text", ["prompt": "New name"])
        exit name
    }
}
```

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

An action may yield more than one request. Each request accepts exactly one response. While a
request is pending, choice selection and event delivery are rejected; `Dispose` abandons the
interaction. `request` is not available to host events, guards, or descriptions.

## Testing selects

Automated tests use the same C# API as the application: execute the declarations, call
`OpenSelectAsync`, inspect `Choices`, and drive `SelectAsync` or `SendAsync`. Assert the live
`Choices` after each action, or call `MinamoSelect.GetValue<T>()` after completion. No script
invocation syntax or suspended script run is needed.

For a manual test, let the console act as the host:

```powershell
minamo.exe town.nami --do town
```

The console loads the file and calls `OpenSelectAsync("town")`. In the REPL, `do town` is a
console command with the same behavior. These are testing commands, not language syntax;
`do expression` is not accepted in scripts. The ordinary `do { ... } while ...` loop is supported.

## Properties, cases, and local values

`prop` publishes read-only values for the current UI state. Property values are reevaluated after a
case, host event, response, or navigation return. Select-local values remain private to the script.
Case IDs are the stable values sent by the host. After an action finishes, properties, metadata,
guards, and available choices are published again. `exit` completes the interaction, optionally
with a value.

```nami
select player {
    mut playing = false

    prop playing => playing

    case "play" when !playing [
        "text": "Play",
        "control": "button"
    ] => {
        playing = true
    }

    case "stop" when playing => {
        playing = false
    }

    case "exit" ["text": "Close player"] => exit
}
```

Property names may be identifiers or strings. Resolve a property by name and convert its value:

```csharp
var playing = player.Properties
    .Single(property => property.Name == "playing")
    .GetValue<bool>();
```

The optional string-key dictionary following a property or case is free-form UI metadata. Minamo
does not interpret keys such as `text`, `control`, `icon`, or `shape`; each host may use or ignore
them. `MinamoSelectProperty.Metadata` and `MinamoChoice.Metadata` expose the dictionary through
`GetValue<T>()` and `TryGetValue<T>()`. A simple CLI can display the case ID when it does not use a
`text` hint.

## Passing values to cases

Case parameters determine the C# payload shape:

```nami
case "play" => { }
case "select-track" (trackId) => { }
case "set-volume" (trackId, value) => { }
```

```csharp
MinamoChoice Choice(string id) =>
    player.Choices.Single(choice => choice.Id == id);

await player.SelectAsync(Choice("play"));
await player.SelectAsync(Choice("select-track"), trackId);
await player.SelectAsync(Choice("set-volume"), (trackId, 80));
```

No parameter means that no payload is accepted. One parameter receives the supplied value. Two or
more parameters receive one C# tuple with the same number of elements.

## Navigation between selects

Use `goto select-expression` to open another select inside the same host interaction. Minamo creates
a fresh instance of the target factory and keeps the current instance on a navigation stack. A
value-less `return` completes the current instance and restores the previous one; its local values
are preserved. `exit value` completes the entire interaction from any depth and makes `value`
available through `GetValue<T>()`.

```nami
select details {
    desc ["title": "Details"]
    case "close" => return
}

select menu {
    desc ["title": "Menu"]
    case "details" => goto details
    case "quit" => exit "closed"
}
```

The host continues to read `Name`, `Description`, and `Choices` from the same `MinamoSelect`.
`return` at the root completes with `nil`. A select with no available choices and no host events
also returns implicitly; at the root that completes with `nil`. There is currently no `back`
keyword, and `return` in a select action does not accept a value. An explicitly declared function
inside an action still uses ordinary function-return semantics.

## Conditional cases

Use `when` to hide a case until it is available.

```nami
case "accept"
    when game.CanAcceptCourierQuest()
    ["text": "Accept the courier quest", "control": "button"]
    => {
    game.AcceptCourierQuest()
}
```

A false guard removes the case from `Choices`. Guards run when Minamo publishes a select screen:
on opening and after a case, host event, or response finishes. They should be free of side
effects.

## Host events

`on` declares an event that is not displayed in `Choices`. Use it for host-owned domain events
such as a timer, a completed download, or an inventory update.

```nami
select download {
    on "completed" (fileName) => exit fileName
    case "cancel" => exit nil
}
```

```csharp
await download.SendAsync("completed", fileName);
var completedFile = download.GetValue<string>();
```

`SendAsync` uses the same argument rules as `SelectAsync`. An event is accepted only when the
select declares it.

## When to use the advanced API

Use this basic API for a CLI, a single-threaded desktop UI, or any host that renders choices and
handles the next input immediately. Move to [Advanced interactive selects](InteractiveSelectAdvanced.md)
when choice identity matters or selects are created and composed as reusable factories.

For the complete grammar, see the [grammar reference](../Reference/Grammar.md). The advanced guide
also covers descriptions, factory closures, and empty interactions.
