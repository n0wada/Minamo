# Interactive selects

`select` defines a host-driven interaction such as a menu, dialogue, or GUI screen. The script
publishes values and available actions; the host renders them and supplies input through C#.

## Declaration model

Save this module-level declaration as `counter.nami`:

```nami
select counter {
    desc ["prompt": "Show a small counter"]

    mut count = 0

    prop count ["format": "number"] => count

    action "add" when count < 3 ["text": "Add one"] => {
        count += 1
    }

    action "finish" => exit count
}
```

In `prop count => count`, the first `count` is the name published to the host. The expression
after `=>` reads the local variable declared by `mut count`. `prop` does not introduce a local
binding, so its published name alone cannot be used to access the property inside the select.

| Declaration | Purpose |
| --- | --- |
| `desc` | Free-form description, evaluated once per select instance. |
| `let` / `mut` | Private values belonging to that instance. |
| `prop` | Read-only values published to the host. |
| `action` | Operations available when their optional `when` guard succeeds. |
| `hidden action` | Available operations that the host omits from user-facing choices. |

`desc` comes first when present, followed by locals, then properties and actions. An action's
`when` guard precedes its metadata dictionary. Its body is a block or a direct `goto`, `return`,
or `exit` statement. See the [grammar reference](../Reference/Grammar.md#interactive-selects)
for exact syntax and validation rules.

Descriptions and property/action metadata are string-key dictionaries. Their meanings belong to
the host; the bundled console uses a string-valued `text` entry as an action's display label.
The C# wrappers provide `GetValue<T>()` and `TryGetValue<T>()` for reading properties, descriptions,
and metadata, for example as `Dictionary<string, object?>`.

## Selecting actions and passing arguments

Load the declarations, open a select, and invoke its actions by ID:

```csharp
using Minamo.Hosting;

var host = new MinamoHost();
using var instance = host.CreateInstance();
var initialized = await instance.ExecuteFileAsync("counter.nami");
if (!initialized.Success)
    throw new InvalidOperationException(initialized.Failure?.Message);

using var counter = await instance.OpenSelectAsync("counter");
var initialCount = counter.GetProperty("count");
Console.WriteLine(initialCount.GetValue<long>()); // 0
await counter.SelectAsync("add");
Console.WriteLine(counter.GetProperty("count").GetValue<long>()); // 1
Console.WriteLine(initialCount.GetValue<long>()); // 0 (earlier publication)
await counter.SelectAsync("finish");
Console.WriteLine(counter.GetValue<long>()); // 1
```

Alternatively, `await instance.ExecuteAsync("open counter")` returns the handle through the
result's `Select` property. `open name` must be the entire host command; it is not script syntax.
Each opening creates a fresh root instance. The caller owns the handle and must dispose it.

The handle exposes the latest published state:

| Member | Value |
| --- | --- |
| `Name` / `Description` | Active select's name and description. |
| `Properties` | Published `MinamoSelectProperty` values. |
| `GetProperty(name)` | A published property retrieved by its exact name; throws if absent. |
| `Actions` | Available `MinamoSelectAction` objects, including hidden actions. |
| `Token` | `MinamoSelectToken` identifying this publication. |
| `Request` | Pending input request, or `null`. |
| `IsCompleted` | Whether the whole interaction has finished. |

`GetProperty(name)` returns a property from the current publication. Use its `GetValue<T>()`
to read the value or `Metadata` to access its UI hints. Retained property objects keep the values
from their publication, as `initialCount` does above.

Action parameters determine the host payload shape:

| Declared parameters | Example host call |
| --- | --- |
| None | `SelectAsync("play")` |
| `(trackId)` | `SelectAsync("select-track", trackId)` |
| `(trackId, value)` | `SelectAsync("set-volume", (trackId, 80))` |

An explicit `null` argument supplies `nil`. Unknown IDs, guarded-out actions, and incorrect payload
shapes are rejected. The ID is resolved against the active select when the operation begins
executing, after waiting for earlier operations.

### Validating the displayed state

For UI input, capture the token when rendering and return it with the chosen action ID:

```csharp
var token = player.Token;
var action = ui.Pick(player.Actions.Where(candidate => !candidate.IsHidden).ToArray());
await player.SelectAsync(action.Id, expectedToken: token);
```

With an argument, use `SelectAsync(id, argument, expectedToken: token)`.

The token identifies the whole publication and works with any available action on that screen.
It changes on each successful publication, including nested entry, return, requests, and completion.
Returning to a parent does not restore its old token. An old, foreign, or default token is rejected
inside the serialized dispatcher before an action executes. Without a token, the call targets the
currently active select.

Keep the token associated with the rendered choices; reading a new token when handling old input
would bypass this check. A remote UI can transmit its `Guid` `Value` and the host can reconstruct
`new MinamoSelectToken(value)`. There is no implicit conversion between tokens and `Guid`, so a
`Guid` action argument can be passed normally.

## Hidden actions and host events

Use hidden actions for external notifications such as timers or completed background work:

```nami
select clock {
    mut elapsed = 0
    prop elapsed => elapsed

    hidden action "tick" (seconds) => { elapsed += seconds }
    hidden action "close" => exit elapsed
}
```

After loading these declarations, the host can dispatch notifications through the same API:

```csharp
using var clock = await instance.OpenSelectAsync("clock");
await clock.SelectAsync("tick", 1);
await clock.SelectAsync("close");
```

Hidden actions appear in `Actions` with `IsHidden == true`; the host filters them when rendering.
The bundled console neither displays nor accepts them. They support the same arguments, guards,
metadata, requests, and navigation as visible actions. `hidden` affects presentation;
`when false` makes an action unavailable.

A notification handler can update script or host-owned data and trigger republication.
External changes alone do not cause republication. Buffer notifications that
arrive before the select is open, and coordinate delivery through the host's dispatcher.

## Requests from action handlers

An action can call `request(kind, payload?)` to pause for additional host input:

```nami
select profile {
    action "rename" => exit request("text", ["prompt": "New name"])
}
```

The host reads the request and resumes the action with a response:

```csharp
await profile.SelectAsync("rename");
while (profile.Request is { } request)
{
    var response = await ui.GetResponseAsync(
        request.Kind,
        request.GetPayload<Dictionary<string, object?>>());
    await profile.RespondAsync(request, response);
}
```

While a request is pending, properties retain their last values, `Actions` is empty, and all action
selection is rejected. An action may issue multiple requests; each request object accepts one
response, and old objects are rejected. `request` is available only in action handlers, including
hidden actions. Disposing the select abandons a pending request.

## Navigation and factories

A select expression produces a reusable factory. `goto` creates a new instance from a factory
and preserves the current instance on a navigation stack:

```nami
func details(itemId) {
    select {
        prop item => itemId
        action "close" => return
    }
}

select browser {
    action "open" (itemId) => goto details(itemId)
    action "finish" => exit "done"
}
```

| Statement | Effect |
| --- | --- |
| `goto expression` | Creates and enters a select instance, pushing the current one. |
| `return` | Completes the current instance with `nil` and restores its parent; at the root, completes the interaction. |
| `exit value` | Completes the whole interaction from any depth, with an optional result. |

The same C# handle exposes the active instance's name, description, properties, and actions.
Parent locals remain intact while a child is active. Returning reevaluates the parent's published
values but retains its original description. A select-level `return` cannot carry a value;
ordinary returns inside nested functions keep their usual meaning.

Locals declared inside a select are fresh for each instance. Captured outer variables belong to
the factory closure and are shared by its instances; see [Functions and closures](../Language/FunctionsAndClosures.md#closures).
A factory can also be registered with `alias(browser, "app.browser")` and opened by that dotted
alias through the hosting API.

When no visible or hidden action is available, the select implicitly returns with `nil`: a child
restores its parent and a root completes. Properties alone do not keep a select active and are
not published for an empty screen. A select with available hidden actions remains active even
when it has no visible choices.

Read the result with `GetValue<T>()` after `IsCompleted` becomes true. Before completion it throws,
while `TryGetValue<T>()` returns `false`. `Dispose()` abandons the interaction without producing a
script result.

## Publication and execution

Opening a select publishes its initial state. Normal action completion republishes it; `goto`
publishes the new instance, and `return` republishes the restored parent.

| Value | Evaluation time |
| --- | --- |
| `desc` | Once per instance. |
| `prop` values and metadata | Every publication. |
| Action guards | Every publication. |
| Action metadata | Every publication where that action is available. |
| Action bodies | When invoked by the host. |

Properties and metadata are snapshots; retained objects keep their old values. Keep guards,
properties, and metadata free of side effects. If ordinary republication fails while evaluating
them, the operation throws and retains the last successful publication and its token.

Operations on one handle execute serially. Hosts should coordinate rendering data reads, token
capture, external data changes, and action calls through their own dispatcher. Concurrent calls
may encounter a stale token, a different active select, or a pending request.

Tests can use the hosting API and assert the next published state or completion value. For a
manual check, run the initial counter example in the console:

```powershell
minamo.exe counter.nami --open counter
```

The REPL command `open counter` starts the same interaction after the declarations have loaded.
For general host setup and execution errors, see the [Hosting API guide](HostingGuide.md).
