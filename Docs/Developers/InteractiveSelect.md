# Interactive selects

`select` describes a host-driven interaction such as a menu, dialogue, shop, game screen, or GUI
view model. The script publishes data and available operations; the host decides how to render them
and sends user or application input back through the C# API.

Language-level `action` declarations appear as `MinamoSelectAction` objects in C#.

## Quick start

Declare a named select at module scope:

```nami
select town {
    action "leave" => exit "goodbye"
}
```

Execute the declarations, submit an `open` execution command, and pass a currently published action
back to the select:

```csharp
var initialization = await instance.ExecuteAsync("""
    select town {
        action "leave" => exit "goodbye"
    }
    """);
if (!initialization.Success)
    throw new InvalidOperationException(initialization.Failure?.Message);

var opened = await instance.ExecuteAsync("open town");
if (!opened.Success)
    throw new InvalidOperationException(opened.Failure?.Message);

using var town = opened.Select
    ?? throw new InvalidOperationException("The execution did not open a select.");
while (!town.IsCompleted)
{
    var action = ui.Pick(town.Actions);
    await town.SelectAsync(action);
}

Console.WriteLine(town.GetValue<string>());
```

This minimal loop assumes every published screen has a visible action and no action calls
`request`. The later sections cover requests and event-only screens.

`MinamoSelect` is a live handle whose properties describe the latest published state:

| Member | Purpose |
| --- | --- |
| `Name` | Declared name of the select currently at the top of the navigation stack. |
| `Description` | Free-form dictionary description declared with `desc`. |
| `Properties` | Read-only values published by `prop` declarations. |
| `Actions` | Currently visible actions, represented as `MinamoSelectAction` objects. |
| `Request` | Input currently requested by a running action handler, or `null`. |
| `IsCompleted` | Whether the whole interaction has completed. |
| `SelectAsync(action, argument?)` | Runs an action from the current publication. |
| `SendAsync(id, argument?)` | Delivers a hidden host event declared with `on`. |
| `RespondAsync(request, response?)` | Resumes the action that yielded the request. |
| `GetValue<T>()` / `TryGetValue<T>()` | Reads the completion value. |
| `Dispose()` | Abandons the interaction and releases it. |

Each `open name` execution command creates a fresh root select instance and fresh select-local
values. Hosts that already distinguish interaction startup from script execution may call
`OpenSelectAsync(name)` directly. After an ordinary action, Minamo republishes the same interaction
through the same `MinamoSelect` object.

## Declaration model

A select can contain one description, private local values, published properties, visible actions,
and hidden host events:

```nami
select player {
    desc ["prompt": "Present a compact music player"]

    mut playing = false

    prop playing ["control": "status"] => playing

    action "play" when !playing [
        "text": "Play",
        "control": "button"
    ] => {
        playing = true
    }

    action "stop" when playing ["text": "Stop"] => {
        playing = false
    }

    on "close" => exit
}
```

`desc`, when present, comes first. All `let` and `mut` select locals follow it and must precede
`prop`, `action`, and `on` declarations. Properties, actions, and events may then be interleaved.

Action syntax follows this order:

```text
action "id" [ select-parameters ] [ "when" guard ] [ select-metadata ] "=>" action-body
```

The guard therefore precedes action metadata. An action body must be a block or a direct `goto`,
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

The optional dictionary after a property or after an action's optional `when` guard is free-form UI
metadata. Keys must be strings, but Minamo assigns no meaning to values such as `text`, `control`,
`icon`, `shape`, `format`, or `bind`. A CLI may ignore them, a conventional GUI may recognize a
subset, and an AI UI generator may use all of them as hints.

`MinamoSelectDescription`, `MinamoSelectProperty`, and `MinamoSelectMetadata` provide typed
`GetValue<T>()` and `TryGetValue<T>()` conversion. For example, a metadata dictionary can be read as
`Dictionary<string, object?>`. The bundled console treats a string-valued `text` entry as display
text and falls back to the action ID; this is a host convention, not language semantics.

### Publication and evaluation timing

Opening a select publishes its first screen. Minamo publishes again after an action or host event
finishes normally, after a request response lets its action finish, after entering a `goto` target,
and after `return` restores the previous instance.

| Declaration | Evaluation time |
| --- | --- |
| `desc` | Once when its select instance is created. |
| `prop` value and metadata | On every screen publication. |
| `action when` guard | On every screen publication. |
| Available action metadata | On every screen publication, after its guard succeeds. |
| Action or `on` handler | Only when invoked by the host. |

Published properties and metadata are snapshots. Objects retained from an earlier publication keep
their old values. Guards, properties, and metadata should be free of side effects because the host
controls when interactions are driven and republished.

When an ordinary republication fails while evaluating a guard, property, or metadata dictionary,
the operation throws and does not replace the last successfully published state with partially
evaluated data.

## Selecting actions and passing arguments

Always select a `MinamoSelectAction` from the current `Actions` collection. There is intentionally no
string overload of `SelectAsync`; resolving an ID is a normal collection lookup:

```csharp
MinamoSelectAction Action(string id) =>
    player.Actions.Single(action => action.Id == id);

await player.SelectAsync(Action("play"));
```

Each successful screen publication creates fresh action objects. An object retained from an older
publication is rejected, even if an action with the same ID is still visible. This prevents a local
UI from accidentally applying an input to a screen that has already changed.

A remote UI cannot preserve .NET object identity. Associate an opaque render token with the
published actions and validate that token on the same serialized dispatcher that resolves the
current ID and calls `SelectAsync`.

Action parameters determine the host payload shape:

```nami
action "play" => { }
action "select-track" (trackId) => { }
action "set-volume" (trackId, value) => { }
```

```csharp
await player.SelectAsync(Action("play"));
await player.SelectAsync(Action("select-track"), trackId);
await player.SelectAsync(Action("set-volume"), (trackId, 80));
```

No parameter accepts no payload. One parameter receives the supplied value. Two or more parameters
receive one C# tuple with the same number of elements.

## Requests from action handlers

Call `request(kind, payload?)` when an action handler needs additional host input before it can
finish:

```nami
select profile {
    action "rename" => {
        let name = request("text", ["prompt": "New name"])
        exit name
    }
}
```

The action yields without blocking a thread. The publication retains its properties but exposes no
actions until the host responds:

```csharp
var rename = profile.Actions.Single(action => action.Id == "rename");
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
pending, action selection and event delivery are rejected. `request` is not available in host
events, guards, properties, metadata, or descriptions. Disposing the select abandons a pending
request.

## Host events

`on` declares input that is not displayed in `Actions`. Use it for host-owned domain events such as
a timer, completed download, or inventory update:

```nami
import startDownload from downloads

select download {
    let downloadStatus = startDownload()

    prop status => downloadStatus.StatusName

    on "progress-changed" (value) => {
        downloadStatus.Update(value)
    }

    on "completed" (fileName) => exit fileName
}
```

The host command starts the transfer and immediately returns an object whose declared interface is
the script-visible boundary. The download task writes notifications to a channel so events raised
while the select is opening are retained until the host can dispatch them:

```csharp
using Minamo.Hosting;
using System.Threading.Channels;

public interface IDownloadStatus
{
    string StatusName { get; }
    void Update(long bytesReceived);
}

public sealed class DownloadStatus : IDownloadStatus
{
    public string StatusName { get; private set; } = "Starting";

    public void Update(long bytesReceived) =>
        StatusName = $"Downloaded {bytesReceived:N0} bytes";
}

public sealed record DownloadEvent(string Name, object Value);

public sealed class DownloadService(
    HttpClient client,
    Uri source,
    string destination)
{
    private readonly Channel<DownloadEvent> events =
        Channel.CreateUnbounded<DownloadEvent>();

    public ChannelReader<DownloadEvent> Events => events.Reader;

    public IDownloadStatus StartDownload()
    {
        _ = DownloadAsync();
        return new DownloadStatus();
    }

    private async Task DownloadAsync()
    {
        try
        {
            using var response = await client.GetAsync(
                source,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(destination);

            var buffer = new byte[81920];
            long bytesReceived = 0;
            long lastReported = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer);
                if (count == 0)
                    break;

                await output.WriteAsync(buffer.AsMemory(0, count));
                bytesReceived += count;

                if (bytesReceived - lastReported >= 1024 * 1024)
                {
                    await events.Writer.WriteAsync(
                        new("progress-changed", bytesReceived));
                    lastReported = bytesReceived;
                }
            }

            if (bytesReceived != lastReported)
            {
                await events.Writer.WriteAsync(
                    new("progress-changed", bytesReceived));
            }

            await events.Writer.WriteAsync(new("completed", destination));
            events.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            events.Writer.TryComplete(exception);
        }
    }
}

public static class DownloadExample
{
    public static async Task<string?> RunAsync()
    {
        using var client = new HttpClient();
        var downloads = new DownloadService(
            client,
            new Uri("https://example.com/"),
            Path.GetFullPath("example.html"));

        var host = new MinamoHost()
            .Module("downloads", module => module.Command<IDownloadStatus>(
                "startDownload",
                _ => downloads.StartDownload()));

        using var instance = host.CreateInstance();
        var initialization = await instance.ExecuteFileAsync("download.nami");
        if (!initialization.Success)
            throw new InvalidOperationException(initialization.Failure?.Message);

        using var download = await instance.OpenSelectAsync("download");
        await foreach (var notification in downloads.Events.ReadAllAsync())
        {
            await download.SendAsync(notification.Name, notification.Value);
        }

        return download.GetValue<string>();
    }
}
```

`SendAsync` uses the same argument-shape rules as `SelectAsync`. Sending an event republishes the
screen after its action finishes, making it the explicit mechanism for reflecting changed
host-owned data. In this example, `Update(value)` runs on the select dispatcher, and the following
publication reads the new `StatusName`. Progress notifications are limited to roughly one per
mebibyte instead of one per I/O chunk. An undeclared event ID is rejected.

## Navigation and factories

A select expression produces a reusable factory. Select-local values belong to an instance created
from that factory, while captured outer variables belong to the factory closure and are shared by
its instances:

```nami
func createShop() {
    mut visits = 0

    select {
        mut cartCount = 0

        action "add" => { cartCount += 1 }
        action "leave" => {
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
        action "close" => return
        action "delete" => exit ("deleted", itemId)
    }
}

select browser {
    mut opened = false

    action "open" (itemId) when !opened => {
        opened = true
        goto createDetails(itemId)
    }

    action "finish" when opened => exit "closed"
}
```

The control operations have different scopes:

- `goto select-expression` pushes the current instance and enters a new one.
- `return` completes the current instance with `nil` and restores one stack frame. At the root, it
  completes the whole interaction with `nil`.
- `exit value` completes the whole interaction from any depth, clears the stack, and publishes its
  optional value as the result.

The same `MinamoSelect` object publishes the active instance's `Name`, `Description`, `Properties`,
and `Actions`. Each `goto` creates a new instance even when it evaluates the same factory again.
Instances already on the stack retain their local values; `return` reevaluates their published
state, while their descriptions retain the value evaluated when those instances were created.

There is no automatic action named `back` and no `back` keyword. Put `return` in whichever action or
host event should move up one level. A select-level `return` cannot carry a value. Inside an
explicitly declared nested function, ordinary `return value` still returns from that function.

## Completion and empty selects

`exit`, root-level `return`, and an empty root select complete the interaction. Call `GetValue<T>()`
only after `IsCompleted` becomes true; before then it throws. `TryGetValue<T>()` returns `false`
while the interaction is active.

When a publication finds no available actions and the active select declares no host events, Minamo
implicitly returns from that select with `nil`. A nested select restores its caller; a root select
completes. Properties alone do not keep an otherwise empty select active and are not published for
that empty screen. An event-only select remains active with an empty `Actions` list and may publish
properties.

Use `exit value` when the whole interaction should finish with a meaningful result. `Dispose()`
abandons the interaction rather than producing a script completion value.

## Concurrency, errors, and limits

Operations on one `MinamoSelect` are serialized internally. Hosts should still avoid issuing
concurrent actions: after the first operation republishes the screen, another operation may hold a
stale action or conflict with a pending request. Coordinate host-owned data changes and the
corresponding `SendAsync` call through the host's own dispatcher.

The current boundaries are deliberate:

- actions and host events execute serially rather than concurrently;
- only currently published action and request objects are accepted;
- select instances have no public serialization format;
- scripts own navigation, local values, properties, and available actions, while hosts own rendering,
  event timing, and external state.

## Testing selects

Automated tests use the application API: execute declarations, submit `open name` to `ExecuteAsync`,
inspect `MinamoExecutionResult.Select`, and drive `SelectAsync`, `SendAsync`, or `RespondAsync`.
`OpenSelectAsync(name)` remains available when a direct Select-opening API is more convenient.
Assert the next publication after each action, or read the completion value after `IsCompleted`.

For a manual test, let the bundled console act as the host:

```powershell
minamo.exe town.nami --open town
```

The console loads the file and submits `open town` to `ExecuteAsync`. In the REPL, entering
`open town` has the same behavior. The command must contain only a Select identifier or registered
dotted alias. It is a host execution command rather than language syntax, so it is not accepted in
script files.

For exact syntax and validation rules, see the [grammar reference](../Reference/Grammar.md).
