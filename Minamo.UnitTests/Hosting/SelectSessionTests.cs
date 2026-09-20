using Minamo.Hosting;
using System.Diagnostics;
using System.Text;
using Xunit;

namespace Minamo.UnitTesting;

public sealed class SelectSessionTests
{
    [Fact]
    public async Task ExecuteAsyncOpensANamedSelectCommand()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select Player {
                action "finish" => exit 42
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);
        Assert.Null(initialization.Select);

        var opened = await instance.ExecuteAsync("open Player");

        Assert.True(opened.Success, opened.Failure?.Message);
        using var select = Assert.IsType<MinamoSelect>(opened.Select);
        await select.SelectAsync(Action(select, "finish"));
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task ExecuteAsyncOpensARegisteredDottedSelectAlias()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select Player {
                action "finish" => exit
            }

            alias(Player, "media.player")
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        var opened = await instance.ExecuteAsync("open media.player");

        Assert.True(opened.Success, opened.Failure?.Message);
        using var select = Assert.IsType<MinamoSelect>(opened.Select);
        Assert.Equal("finish", Assert.Single(select.Actions).Id);
    }

    [Fact]
    public async Task ExecuteAsyncTreatsOtherInputAsScript()
    {
        using var instance = new MinamoHost().CreateInstance();

        var result = await instance.ExecuteAsync("""
            let open = 40
            open + 2
            """);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Null(result.Select);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public async Task ExecuteAsyncReportsAnUnknownOpenSelectAsInputFailure()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("let value = 42");
        Assert.True(initialization.Success, initialization.Failure?.Message);

        var result = await instance.ExecuteAsync("open Missing");

        Assert.False(result.Success);
        Assert.Null(result.Select);
        Assert.Equal(MinamoFailureKind.Input, result.Failure?.Kind);
        Assert.Contains("No select named 'Missing'", result.Failure?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenSelectCommandReportsMetricsAndTelemetry()
    {
        var logs = new List<MinamoLogEntry>();
        using var instance = new MinamoHost(new() { Log = logs.Add })
            .Module("work", module => module.Command("Value", context =>
            {
                context.Log(MinamoLogLevel.Info, "select property");
                return 42;
            }))
            .CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            import work

            select Player {
                prop value => work.Value()
                action "finish" => exit
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        var opened = await instance.ExecuteAsync("open Player");

        Assert.True(opened.Success, opened.Failure?.Message);
        using var select = Assert.IsType<MinamoSelect>(opened.Select);
        Assert.True(opened.Metrics.Instructions > 0);
        Assert.True(opened.Metrics.HostCommands > 0);
        Assert.True(opened.Metrics.VmDuration > TimeSpan.Zero);
        var log = Assert.Single(logs, entry => entry.Message == "select property");
        Assert.Equal(opened.ExecutionId, log.ExecutionId);
        Assert.Equal("Value", log.Command);
    }

    [Fact]
    public async Task OpenSelectCommandPropagatesCancellationDuringInitialization()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var instance = new MinamoHost()
            .Module("work", module => module.AsyncCommand(
                "Wait",
                async context =>
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                    return "unreachable";
                }))
            .CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            import work

            select Player {
                desc ["title": work.Wait()]
                action "finish" => exit
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);
        using var cancellation = new CancellationTokenSource();

        var opening = instance.ExecuteAsync("open Player", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var cancelled = await opening.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(cancelled.Success);
        Assert.Equal(MinamoFailureKind.Cancelled, cancelled.Failure?.Kind);
        var next = await instance.ExecuteAsync("40 + 2");
        Assert.True(next.Success, next.Failure?.Message);
        Assert.Equal(42L, next.GetValue<long>());
    }

    [Fact]
    public async Task InteractiveSelectProvidesTheBasicActionApi()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select flow {
                action "finish" (value) => exit value
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var select = await instance.OpenSelectAsync("flow");
        Assert.False(select.IsCompleted);
        Assert.False(select.TryGetValue<long>(out _));
        Assert.Throws<InvalidOperationException>(() => select.GetValue<long>());
        Assert.Equal("finish", Assert.Single(select.Actions).Id);

        await select.SelectAsync(Action(select, "finish"), 42);

        Assert.True(select.IsCompleted);
        Assert.Empty(select.Actions);
        Assert.Equal(42L, select.GetValue<long>());
        Assert.True(select.TryGetValue<long>(out var value));
        Assert.Equal(42L, value);
    }

    [Fact]
    public async Task InteractiveSelectAcceptsUppercaseIdentifiers()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select Flow {
                prop Status => "ready"
                action "finish" (Value) => exit Value
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var select = await instance.OpenSelectAsync("Flow");
        Assert.Equal("ready", Property(select, "Status").GetValue<string>());

        await select.SelectAsync(Action(select, "finish"), 42);

        Assert.True(select.IsCompleted);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task SelectRepublishesActionsUntilExit()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                mut count = 0

                action "add" when count == 0 => {
                    count += 1
                }

                action "finish" when count == 1 => exit count
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = instance.OpenSelectSession("flow");
        Assert.Equal("add", Assert.Single(select.Snapshot.Actions).Id);

        await select.SelectAsync(Action(select, "add"));
        Assert.False(select.IsCompleted);
        Assert.Equal("finish", Assert.Single(select.Actions).Id);

        await select.SelectAsync(Action(select, "finish"));
        Assert.True(select.IsCompleted);
        Assert.Equal(1L, select.GetValue<long>());
    }

    [Fact]
    public async Task SelectPublishesPropertiesAndFreeFormMetadata()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select counter {
                mut count = 0

                prop count [
                    "control": "number",
                    "emphasis": count == 0 ? "quiet" : "strong"
                ] => count

                prop "status-text" => fmt("Count: {0}", count)

                action "add" when count < 2 [
                    "text": count == 0 ? "Start" : "Add again",
                    "control": "button",
                    "shape": "round"
                ] => {
                    count += 1
                }

                action "finish" when count == 2 => exit count
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = await instance.OpenSelectAsync("counter");
        var initialCount = Property(select, "count");
        Assert.Equal(0L, initialCount.GetValue<long>());
        Assert.Equal(
            "quiet",
            initialCount.Metadata?.GetValue<Dictionary<string, object?>>()?["emphasis"]);
        Assert.Equal("Count: 0", Property(select, "status-text").GetValue<string>());
        Assert.Equal(
            "Start",
            Action(select, "add").Metadata?.GetValue<Dictionary<string, object?>>()?["text"]);

        await select.SelectAsync(Action(select, "add"));

        Assert.Equal(1L, Property(select, "count").GetValue<long>());
        Assert.Equal(
            "strong",
            Property(select, "count").Metadata?.GetValue<Dictionary<string, object?>>()?["emphasis"]);
        Assert.Equal("Count: 1", Property(select, "status-text").GetValue<string>());
        Assert.Equal(
            "Add again",
            Action(select, "add").Metadata?.GetValue<Dictionary<string, object?>>()?["text"]);
        Assert.Equal(0L, initialCount.GetValue<long>());

        await select.SelectAsync(Action(select, "add"));
        await select.SelectAsync(Action(select, "finish"));

        Assert.Equal(2L, select.GetValue<long>());
    }

    [Fact]
    public void SelectRejectsLegacyChooseAndLabelSyntax()
    {
        var host = new MinamoHost();
        var choose = host.Compile("""
            select flow {
                choose "finish" => exit
            }
            """);
        var label = host.Compile("""
            select flow {
                action "finish" label "Finish" => exit
            }
            """);

        Assert.False(choose.Success);
        Assert.False(label.Success);
    }

    [Fact]
    public void SelectRejectsDuplicatePropertiesAndNonDictionaryMetadata()
    {
        var host = new MinamoHost();
        var duplicate = host.Compile("""
            select flow {
                prop value => 1
                prop value => 2
                action "finish" => exit
            }
            """);
        var propertyMetadata = host.Compile("""
            select flow {
                prop value ["number"] => 1
                action "finish" => exit
            }
            """);
        var caseMetadata = host.Compile("""
            select flow {
                action "finish" [control: "button"] => exit
            }
            """);

        Assert.False(duplicate.Success);
        Assert.Contains(duplicate.Errors, error =>
            error.Code == (int)Minamo.Compiler.CompilerError.SelectDuplicateProperty);
        Assert.False(propertyMetadata.Success);
        Assert.False(caseMetadata.Success);
    }

    [Fact]
    public void SelectCaseGuardPrecedesMetadata()
    {
        var host = new MinamoHost();
        var expectedOrder = host.Compile("""
            select flow {
                let flags = ["ready": true]
                action "finish" when flags["ready"] ["text": "Finish"] => exit
            }
            """);
        var reversedOrder = host.Compile("""
            select flow {
                action "finish" ["text": "Finish"] when true => exit
            }
            """);

        Assert.True(expectedOrder.Success);
        Assert.False(reversedOrder.Success);
    }

    [Fact]
    public void StateSyntaxIsRejected()
    {
        var state = new MinamoHost().Compile("""
            select flow {
                initial state ready {
                    action "finish" => exit
                }
            }
            """);

        Assert.False(state.Success);
    }

    [Fact]
    public async Task GotoPushesASelectAndReturnRestoresItsInstance()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select child {
                desc ["title": "Child"]
                action "return" => return
            }

            select root {
                desc ["title": "Root"]
                mut opened = false

                action "open" when !opened => {
                    opened = true
                    goto child
                }

                action "finish" when opened => exit "done"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = await instance.OpenSelectAsync("root");
        var staleRootAction = Action(select, "open");
        Assert.Equal("Root", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);

        await select.SelectAsync(staleRootAction);

        Assert.Equal("child", select.Name);
        Assert.Equal("Child", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);
        await Assert.ThrowsAsync<ArgumentException>(() => select.SelectAsync(staleRootAction));
        await select.SelectAsync(Action(select, "return"));

        Assert.Equal("root", select.Name);
        Assert.Equal("Root", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);
        Assert.Equal("finish", Assert.Single(select.Actions).Id);
        await select.SelectAsync(Action(select, "finish"));

        Assert.True(select.IsCompleted);
        Assert.Equal("done", select.GetValue<string>());
    }

    [Fact]
    public async Task NavigationStackReturnsInLastInFirstOutOrder()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select leaf {
                action "up" => return
            }

            select middle {
                mut visited = false
                action "down" when !visited => {
                    visited = true
                    goto leaf
                }
                action "up" when visited => return
            }

            select root {
                mut visited = false
                action "down" when !visited => {
                    visited = true
                    goto middle
                }
                action "finish" when visited => exit 42
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = await instance.OpenSelectAsync("root");
        await select.SelectAsync(Action(select, "down"));
        Assert.Equal("middle", select.Name);
        await select.SelectAsync(Action(select, "down"));
        Assert.Equal("leaf", select.Name);
        await select.SelectAsync(Action(select, "up"));
        Assert.Equal("middle", select.Name);
        await select.SelectAsync(Action(select, "up"));
        Assert.Equal("root", select.Name);
        await select.SelectAsync(Action(select, "finish"));
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task ExitFromNestedSelectCompletesTheWholeSession()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select child {
                action "finish" => exit 42
            }

            select root {
                action "open" => goto child
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = await instance.OpenSelectAsync("root");
        await select.SelectAsync(Action(select, "open"));
        await select.SelectAsync(Action(select, "finish"));

        Assert.True(select.IsCompleted);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task HostEventsCanNavigateAndReturn()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select child {
                on "close" => return
            }

            select root {
                on "open" => goto child
                on "finish" => exit "done"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = await instance.OpenSelectAsync("root");
        await select.SendAsync("open");
        Assert.Equal("child", select.Name);
        await select.SendAsync("close");
        Assert.Equal("root", select.Name);
        await select.SendAsync("finish");

        Assert.True(select.IsCompleted);
        Assert.Equal("done", select.GetValue<string>());
    }

    [Fact]
    public async Task GotoRejectsANonSelectValueAndKeepsTheCurrentPublication()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select root {
                action "invalid" => goto 42
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = await instance.OpenSelectAsync("root");
        var published = Assert.Single(select.Actions);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => select.SelectAsync(published));

        Assert.False(select.IsCompleted);
        Assert.Equal("root", select.Name);
        Assert.Same(published, Assert.Single(select.Actions));
    }

    [Fact]
    public async Task GotoAcceptsASelectFactoryExpression()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            func createChild(title) {
                select {
                    desc ["title": title]
                    action "up" => return
                }
            }

            select root {
                mut returned = false
                action "open" when !returned => {
                    returned = true
                    goto createChild("Generated")
                }
                action "finish" when returned => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = await instance.OpenSelectAsync("root");
        await select.SelectAsync(Action(select, "open"));

        Assert.Equal("<anonymous>", select.Name);
        Assert.Equal("Generated", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);
        await select.SelectAsync(Action(select, "up"));
        Assert.Equal("root", select.Name);
    }

    [Fact]
    public async Task EmptyGotoTargetReturnsToItsCaller()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select empty { }

            select root {
                mut returned = false
                action "open" when !returned => {
                    returned = true
                    goto empty
                }
                action "finish" when returned => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = await instance.OpenSelectAsync("root");
        await select.SelectAsync(Action(select, "open"));

        Assert.Equal("root", select.Name);
        Assert.Equal("finish", Assert.Single(select.Actions).Id);
    }

    [Fact]
    public async Task ReturnAtRootCompletesWithNil()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select root {
                action "return" => return
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = await instance.OpenSelectAsync("root");
        await select.SelectAsync(Action(select, "return"));

        Assert.True(select.IsCompleted);
        Assert.Null(select.GetValue<object?>());
    }

    [Fact]
    public void ReturnWithValueInSelectActionIsRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select root {
                action "invalid" => return 42
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public async Task ReturnInsideANestedFunctionRemainsAFunctionReturn()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select root {
                action "finish" => {
                    func value() { return 42 }
                    exit value()
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = await instance.OpenSelectAsync("root");
        await select.SelectAsync(Action(select, "finish"));

        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public void SelectDescriptionRequiresAStringKeyDictionaryLiteral()
    {
        var host = new MinamoHost();
        var nonDictionary = host.Compile("""
            select flow {
                desc ["title"]
                action "finish" => exit
            }
            """);
        var nonStringKey = host.Compile("""
            select flow {
                desc [title: "Ready"]
                action "finish" => exit
            }
            """);
        var stateDescription = host.Compile("""
            select flow {
                initial state ready {
                    desc ["title": "Ready"]
                    action "finish" => exit
                }
            }
            """);

        Assert.False(nonDictionary.Success);
        Assert.False(nonStringKey.Success);
        Assert.False(stateDescription.Success);
    }

    [Fact]
    public void ChildActionSpreadSyntaxIsRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select child {
                action "finish" => exit
            }

            select parent {
                action ...child
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public async Task PublishedActionsRejectInputAfterAnAction()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select flow {
                action "advance" => { }
                action "finish" => exit 42
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var select = await instance.OpenSelectAsync("flow");
        var staleAction = Action(select, "finish");

        await select.SelectAsync(Action(select, "advance"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => select.SelectAsync(staleAction));

        await select.SelectAsync(Action(select, "finish"));
        Assert.True(select.IsCompleted);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task PublicSelectExposesDescriptionAndKeepsTheLastPublicationOnEventFailure()
    {
        var failAvailability = false;
        var descriptionCalls = 0;
        using var instance = new MinamoHost()
            .Module("catalog", module =>
            {
                module.Command("Available", _ =>
                {
                    if (failAvailability)
                    {
                        throw new InvalidOperationException("Availability failed.");
                    }

                    return true;
                });
                module.Command("Description", _ => $"Description {++descriptionCalls}");
            })
            .CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            import catalog

            select flow {
                desc ["title": catalog.Description()]

                on "changed" => { }
                action "old" when catalog.Available() => exit "old"
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var select = await instance.OpenSelectAsync("flow");
        Assert.Equal("Description 1", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);
        Assert.Equal(1, descriptionCalls);
        var oldAction = Assert.Single(select.Actions);
        Assert.Equal("old", oldAction.Id);

        failAvailability = true;
        await Assert.ThrowsAnyAsync<Exception>(() => select.SendAsync("changed"));

        Assert.Equal(1, descriptionCalls);
        Assert.Same(oldAction, Assert.Single(select.Actions));
        failAvailability = false;
        await select.SelectAsync(oldAction);
        Assert.True(select.IsCompleted);
        Assert.Equal("old", select.GetValue<string>());
    }

    [Fact]
    public async Task SelectKeepsItsFinalPublishedStateAndRejectsStaleActions()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                desc ["title": "Ready"]

                action "continue" => { }
                action "finish" => exit 42
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        Assert.False(select.IsCompleted);
        Assert.Equal("Ready", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);
        var staleAction = Action(select, "finish");

        await select.SelectAsync(Action(select, "continue"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => select.SelectAsync(staleAction));

        await select.SelectAsync(Action(select, "finish"));
        Assert.True(select.IsCompleted);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task InteractiveSelectSendsHostEvents()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select flow {
                on "complete" (value) => exit value
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var select = await instance.OpenSelectAsync("flow");
        Assert.Empty(select.Actions);

        await select.SendAsync("complete", "done");

        Assert.True(select.IsCompleted);
        Assert.Equal("done", select.GetValue<string>());
    }

    [Fact]
    public async Task SelectActionsAwaitHostCommands()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var instance = new MinamoHost()
            .Module("work", module => module.AsyncCommand(
                "Value",
                async _ =>
                {
                    entered.SetResult();
                    return await completion.Task.ConfigureAwait(false);
                }))
            .CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            import work

            let flow = select {
                action "finish" => exit work.Value()
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        var selection = select.SelectAsync(Action(select, "finish"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(selection.IsCompleted);

        completion.SetResult(42);
        await selection;
        Assert.True(select.IsCompleted);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task DisposingSelectCancelsAnInFlightActionWithoutBlocking()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var instance = new MinamoHost()
            .Module("work", module => module.AsyncCommand(
                "Value",
                async _ =>
                {
                    entered.SetResult();
                    return await completion.Task.ConfigureAwait(false);
                }))
            .CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            import work

            select flow {
                action "finish" => exit work.Value()
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        var select = await instance.OpenSelectAsync("flow");
        var selection = select.SelectAsync(Action(select, "finish"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var started = Stopwatch.GetTimestamp();
        select.Dispose();
        var disposeDuration = Stopwatch.GetElapsedTime(started);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => selection.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(disposeDuration < TimeSpan.FromSeconds(1));
        var next = await instance.ExecuteAsync("40 + 2");
        Assert.True(next.Success, next.Failure?.Message);
        Assert.Equal(42L, next.GetValue<long>());
        completion.TrySetResult(42);
    }

    [Fact]
    public async Task SelectActionYieldsARequestAndResumesWithAResponse()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                action "ask" => {
                    let name = request("text", ["prompt": "Name"])
                    exit fmt("Hello, {0}", name)
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        var requestAction = Action(select, "ask");
        await select.SelectAsync(requestAction);

        Assert.Empty(select.Actions);
        var request = Assert.IsType<MinamoSelectRequest>(select.Request);
        Assert.Equal("text", request.Kind);
        Assert.Equal("Name", request.GetPayload<Dictionary<string, object?>>()?["prompt"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => select.SelectAsync(requestAction));

        await select.RespondAsync(request, "Minamo");

        Assert.True(select.IsCompleted);
        Assert.Null(select.Request);
        Assert.Equal("Hello, Minamo", select.GetValue<string>());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => select.RespondAsync(request, "again"));
    }

    [Fact]
    public async Task SelectActionCanYieldMultipleRequests()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                action "sum" => {
                    let first = request("number")
                    let second = request("number", ["previous": first])
                    exit first + second
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        await select.SelectAsync(Action(select, "sum"));
        var first = Assert.IsType<MinamoSelectRequest>(select.Request);
        Assert.Equal("number", first.Kind);
        Assert.Null(first.GetPayload<object?>());

        await select.RespondAsync(first, 20);
        var second = Assert.IsType<MinamoSelectRequest>(select.Request);
        Assert.Equal(20L, second.GetPayload<Dictionary<string, long>>()?["previous"]);
        Assert.NotSame(first, second);

        await select.RespondAsync(second, 22);

        Assert.True(select.IsCompleted);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task PendingRequestIsAbandonedOnDispose()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                action "ask" => exit request("text")
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        var select = await instance.OpenSelectAsync("flow");
        await select.SelectAsync(Action(select, "ask"));
        var request = Assert.IsType<MinamoSelectRequest>(select.Request);

        select.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => select.RespondAsync(request, "late"));
    }

    [Fact]
    public void OnEmptySyntaxIsRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                on empty => exit
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public async Task RequestOutsideASelectActionFailsWithoutSuspending()
    {
        using var instance = new MinamoHost().CreateInstance();

        var result = await instance.ExecuteAsync("request(\"text\")");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task RequestInAHostEventIsRejectedWithoutWaiting()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                on "ask" => { request("text") }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        await Assert.ThrowsAsync<Minamo.Runtime.MinamoCodeException>(() => select.SendAsync("ask"));

        Assert.Null(select.Request);
    }

    [Fact]
    public async Task OpenSelectSessionAsyncCreatesAReadySession()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select flow {
                action "finish" => exit 42
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var session = await instance.OpenSelectSessionAsync("flow");
        await session.SelectAsync(Action(session, "finish"));
        Assert.True(session.IsCompleted);
    }

    [Fact]
    public void SelectRejectsStateSyntaxAndDuplicateActions()
    {
        var host = new MinamoHost();
        var stringState = host.Compile("""
            select player {
                initial state "stopped" {
                    action "play" => { }
                }
            }
            """);
        Assert.False(stringState.Success);

        var missingInitial = host.Compile("""
            select player {
                state stopped {
                    action "play" => { }
                }
            }
            """);
        Assert.False(missingInitial.Success);

        var duplicateAction = host.Compile("""
            select player {
                action "play" => { }
                action "play" => { }
            }
            """);
        Assert.False(duplicateAction.Success);
        Assert.Contains(duplicateAction.Errors, error =>
            error.Code == (int)Minamo.Compiler.CompilerError.SelectDuplicateAction);
    }

    [Fact]
    public void SelectDoesNotSupportActionDescriptions()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                action "finish" description "Legacy display text" => exit
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public async Task SelectSessionUsesLocalValuesAndAcceptsTupleArguments()
    {
        var volume = 0L;
        var position = (0L, 0L);
        var host = new MinamoHost()
            .Module("music", module =>
            {
                module.Command("Play", _ => null);
                module.Command("Pause", _ => null);
                module.Command("SetVolume", context =>
                {
                    volume = context.Argument<long>("value");
                    return null;
                }, MinamoCommandParameter.Required<long>("value"));
                module.Command("Move", context =>
                {
                    position = (context.Argument<long>("x"), context.Argument<long>("y"));
                    return null;
                },
                    MinamoCommandParameter.Required<long>("x"),
                    MinamoCommandParameter.Required<long>("y"));
            });
        var program = host.Compile("""
            import music

            select player {
                mut mode = "stopped"

                action "play" when mode == "stopped" => {
                    music.Play()
                    mode = "playing"
                }

                action "set-volume" (value) when mode == "stopped" => {
                    music.SetVolume(value)
                }

                action "move" (x, y) when mode == "stopped" => {
                    music.Move(x, y)
                }

                action "status" when mode == "stopped" ["text": "Show status"] => {
                }

                action "hidden" when false ["text": "Hidden command"] => {
                }

                action "pause" when mode == "playing" => {
                    music.Pause()
                    mode = "paused"
                }

                action "exit" when mode == "paused" => exit "done"
            }

            alias(player, "music.player")
            """).GetValueOrThrow();

        using var instance = host.CreateInstance(program);
        using var session = instance.OpenSelectSession("music.player");

        Assert.Collection(session.Actions,
            action => Assert.Equal("play", action.Id),
            action => Assert.Equal("set-volume", action.Id),
            action => Assert.Equal("move", action.Id),
            action =>
            {
                Assert.Equal("status", action.Id);
                Assert.Equal(
                    "Show status",
                    action.Metadata?.GetValue<Dictionary<string, object?>>()?["text"]);
            });
        var unknown = new MinamoSelectAction("hidden", 0);
        await Assert.ThrowsAsync<ArgumentException>(() => session.SelectAsync(unknown));

        await session.SelectAsync(Action(session, "set-volume"), 80);
        Assert.False(session.IsCompleted);
        Assert.Equal(80, volume);

        await session.SelectAsync(Action(session, "move"), (12, 34));
        Assert.Equal((12L, 34L), position);

        await session.SelectAsync(Action(session, "play"));
        Assert.Single(session.Actions);
        Assert.Equal("pause", session.Actions[0].Id);

        await session.SelectAsync(Action(session, "pause"));
        Assert.Single(session.Actions);
        Assert.Equal("exit", session.Actions[0].Id);

        await session.SelectAsync(Action(session, "exit"));
        Assert.True(session.IsCompleted);
        Assert.Equal("done", session.GetValue<string>());
    }

    [Fact]
    public async Task SelectSnapshotsExposeAndRetainDescription()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                desc ["title": "Flow", "step": 1]
                mut advanced = false

                action "next" when !advanced => {
                    advanced = true
                }

                action "exit" when advanced => exit "Done"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("flow");
        var initial = session.Snapshot;
        var initialDescription = initial.Description?
            .GetValue<Dictionary<string, object?>>()
            ?? throw new InvalidOperationException("The select description is unavailable.");
        Assert.Equal("flow", initial.Name);
        Assert.Equal("Flow", initialDescription["title"]);
        Assert.Equal(1, initialDescription["step"]);

        await session.SelectAsync(Action(session, "next"));
        var next = session.Snapshot;
        var nextDescription = next.Description?
            .GetValue<Dictionary<string, object?>>()
            ?? throw new InvalidOperationException("The select description is unavailable.");
        Assert.Same(initial.Description, next.Description);
        Assert.Equal("Flow", nextDescription["title"]);

        await session.SelectAsync(Action(session, "exit"));

        Assert.True(session.IsCompleted);
        Assert.Equal("Done", session.GetValue<string>());
    }

    [Fact]
    public async Task HostEventReevaluatesTheSnapshotAndRejectsPreviouslyPublishedActions()
    {
        var available = true;
        using var instance = new MinamoHost()
            .Module("inventory", module => module.Command("IsAvailable", _ => available))
            .CreateInstance();
        var initialized = instance.Execute("""
            import inventory

            select flow {
                on "inventory-changed" => { }
                action "finish"
                    when inventory.IsAvailable()
                    => exit "done"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("flow");
        var initial = session.Snapshot;
        var staleAction = Assert.Single(initial.Actions);
        available = false;
        await session.SendAsync("inventory-changed");

        Assert.Empty(session.Actions);
        await Assert.ThrowsAsync<ArgumentException>(() => session.SelectAsync(staleAction));
        available = true;
        await session.SendAsync("inventory-changed");
        Assert.Single(session.Actions);

        await session.SelectAsync(Assert.Single(session.Actions));

        Assert.True(session.IsCompleted);
        Assert.Equal("done", session.GetValue<string>());
    }

    [Fact]
    public async Task HostEventPublishesNewActionObjects()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                on "republish" => { }
                action "finish" => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = await instance.OpenSelectSessionAsync("flow");
        var initial = Assert.Single(session.Actions);
        await session.SendAsync("republish");
        var refreshed = Assert.Single(session.Actions);

        Assert.NotSame(initial, refreshed);
        await Assert.ThrowsAsync<ArgumentException>(() => session.SelectAsync(initial));

        await session.SelectAsync(refreshed);
        Assert.True(session.IsCompleted);
    }

    [Fact]
    public async Task AsyncEventsUseTheCurrentSelectSession()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                on "advance" => { }
                on "finish" (value) => exit value
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = await instance.OpenSelectSessionAsync("flow");
        await session.SendAsync("advance");
        Assert.False(session.IsCompleted);

        await session.SendAsync("finish", 42);

        Assert.True(session.IsCompleted);
        Assert.Equal(42L, session.GetValue<long>());
    }

    [Fact]
    public void DynamicActionSyntaxIsRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select shop {
                action item.id
                    label item.name
                    for item in [
                    (id: "apple", name: "Apple", enabled: true, price: 3),
                    (id: "pear", name: "Pear", enabled: false, price: 5)
                    ]
                    when item.enabled
                    => exit item.price
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public void LegacyDynamicActionGroupSyntaxIsRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                for item in ["only"] {
                    action item => exit
                }
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public void ViewKeywordIsRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                view => ["style": "primary"]
                action "continue" => exit
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public void OtherwiseSyntaxIsRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                otherwise => exit
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public void LegacyCaseKeywordIsRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                case "finish" => exit
            }
            """);

        Assert.False(compiled.Success);
    }

    [Theory]
    [InlineData("open flow")]
    [InlineData("do flow")]
    [InlineData("do \"flow\"")]
    [InlineData("let result = do flow")]
    [InlineData("func invoke() => do flow")]
    [InlineData("select parent { action \"open\" => { do flow } }")]
    public void ScriptSelectInvocationIsRejected(string invocation)
    {
        var compiled = new MinamoHost().Compile(
            "select flow { action \"finish\" => exit }\n" + invocation);

        Assert.False(compiled.Success);
    }

    [Fact]
    public void DoWhileStillExecutesItsLoop()
    {
        using var instance = new MinamoHost().CreateInstance();

        var result = instance.Execute("""
            mut count = 0
            do {
                count += 1
            } while count < 2
            count
            """);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(2L, result.GetValue<long>());
    }

    [Fact]
    public void AnonymousSelectIsAClosureBackedFactoryValue()
    {
        using var instance = new MinamoHost().CreateInstance();

        var result = instance.Execute("""
            func createPlayer(volume) {
                select {
                    action "louder" => {
                        print(volume)
                    }
                }
            }

            let player = createPlayer(50)
            """);

        Assert.True(result.Success, result.Failure?.Message);
    }

    [Fact]
    public async Task AliasCanExposeAFunctionProducedSelectFactory()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            func questGame() {
                mut known = false

                select {
                    action "learn" when !known => {
                        known = true
                    }

                    action "continue" when known => exit
                }
            }

            alias(questGame(), "quest.town")
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var first = instance.OpenSelectSession("quest.town");
        Assert.Equal("learn", Assert.Single(first.Actions).Id);
        await first.SelectAsync(Action(first, "learn"));

        using var second = instance.OpenSelectSession("quest.town");
        Assert.Equal("continue", Assert.Single(second.Actions).Id);
    }

    [Fact]
    public async Task NamedSelectFactoryCreatesIndependentSessions()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select player {
                mut playing = false

                action "play" when !playing => {
                    playing = true
                }

                action "exit" when playing => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var first = instance.OpenSelectSession("player");
        using var second = instance.OpenSelectSession("player");

        await first.SelectAsync(Action(first, "play"));

        Assert.Equal("exit", first.Actions.Single().Id);
        Assert.Equal("play", second.Actions.Single().Id);
    }

    [Fact]
    public async Task SelectLocalsAreCreatedForEachSession()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select player {
                mut ready = false

                action "ready" when !ready => {
                    ready = true
                }

                action "exit" when ready => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var first = instance.OpenSelectSession("player");
        Assert.Equal("ready", Assert.Single(first.Actions).Id);
        await first.SelectAsync(Action(first, "ready"));
        Assert.Equal("exit", Assert.Single(first.Actions).Id);

        using var second = instance.OpenSelectSession("player");
        Assert.Equal("ready", Assert.Single(second.Actions).Id);
    }

    [Fact]
    public async Task HostCanOpenAFunctionProducedFactoryVariable()
    {
        var output = new StringBuilder();
        using var instance = new MinamoHost().CreateInstance(
            new MinamoEnvironment().UseOutput(value => output.Append(value)));
        var initialized = await instance.ExecuteAsync("""
            func createShop(name) {
                select {
                    action "leave" => {
                        print(name, terminator: nil)
                        exit
                    }
                }
            }

            let shop = createShop("weapons")
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("shop");

        Assert.False(select.IsCompleted);
        Assert.Equal("leave", Assert.Single(select.Actions).Id);

        await select.SelectAsync(Action(select, "leave"));

        Assert.True(select.IsCompleted);
        Assert.Equal("weapons", output.ToString());
    }

    [Fact]
    public async Task HostCanOpenAFactoryVariable()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let player = select {
                action "exit" => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("player");

        Assert.False(select.IsCompleted);
        await select.SelectAsync(Action(select, "exit"));
        Assert.True(select.IsCompleted);
    }

    [Fact]
    public async Task OpeningAFactoryVariableCreatesASelectLocalFrame()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let player = select {
                mut ready = false

                action "ready" when !ready => {
                    ready = true
                }

                action "exit" when ready => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("player");

        Assert.Equal("ready", Assert.Single(select.Actions).Id);
        await select.SelectAsync(Action(select, "ready"));
        Assert.Equal("exit", Assert.Single(select.Actions).Id);
        await select.SelectAsync(Action(select, "exit"));
        Assert.True(select.IsCompleted);
    }

    [Fact]
    public async Task OpeningASelectEvaluatesItsGuards()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let available = true
            let player = select {
                action "exit" when available => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("player");

        Assert.False(select.IsCompleted);
        Assert.Equal("exit", Assert.Single(select.Actions).Id);
    }

    [Fact]
    public async Task OpeningAnEmptyInitialStateCompletesImmediately()
    {
        var output = new StringBuilder();
        using var instance = new MinamoHost().CreateInstance(
            new MinamoEnvironment().UseOutput(value => output.Append(value)));
        var initialized = await instance.ExecuteAsync("""
            let empty = select {
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("empty");

        Assert.True(select.IsCompleted);
        Assert.Empty(select.Actions);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public async Task ASelectWithoutAvailableActionsCompletesWithNil()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let available = false
            let flow = select {
                action "finish" when available => exit "choice"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.True(select.IsCompleted);
        Assert.Null(select.GetValue<object?>());
    }

    [Fact]
    public async Task SelectCompletesAfterTheLastActionBecomesUnavailable()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                mut available = true

                action "disable" when available => { available = false }
                action "finish" when available => exit "choice"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.Equal(new[] { "disable", "finish" }, select.Actions.Select(action => action.Id));
        await select.SelectAsync(Action(select, "disable"));

        Assert.True(select.IsCompleted);
        Assert.Null(select.GetValue<object?>());
    }

    [Fact]
    public async Task HostEventsKeepASelectWithoutActionsActive()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                on "completed" => exit "event"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.False(select.IsCompleted);
        Assert.Empty(select.Actions);
        await select.SendAsync("completed");
        Assert.Equal("event", select.GetValue<string>());
    }

    [Fact]
    public async Task SelectLocalValuesAreAvailableToActions()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                mut value = 0
                mut started = false

                action "begin" when !started => {
                    value = 10
                    started = true
                }

                action "add" (delta: Integer) when started && value < 20 => {
                    value += delta
                    exit value
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.Equal("begin", Assert.Single(select.Actions).Id);
        await select.SelectAsync(Action(select, "begin"));
        var add = Assert.Single(select.Actions);
        Assert.Equal(1, add.ParameterCount);
        Assert.Equal("delta", Assert.Single(add.Parameters).Name);

        await select.SelectAsync(Action(select, "add"), 10);

        Assert.True(select.IsCompleted);
        Assert.Equal(20L, select.GetValue<long>());
    }

    [Fact]
    public void StateLifecycleHookSyntaxIsRejected()
    {
        var enter = new MinamoHost().Compile("""
            select flow {
                enter => { }
                action "finish" => exit
            }
            """);
        var leave = new MinamoHost().Compile("""
            select flow {
                leave => { }
                action "finish" => exit
            }
            """);

        Assert.False(enter.Success);
        Assert.False(leave.Success);
    }

    [Fact]
    public async Task AliasCanExposeAFactoryFromAProperty()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let ui = (
                currentShop: select {
                    action "leave" => exit "closed"
                }
            )

            alias(ui.currentShop, "shop")
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("shop");

        Assert.Equal("leave", Assert.Single(select.Actions).Id);
        await select.SelectAsync(Action(select, "leave"));
        Assert.True(select.IsCompleted);
        Assert.Equal("closed", select.GetValue<string>());
    }

    [Fact]
    public void ActionParametersExposeTheirNamesAndTypes()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                action "move" (x: Integer, name: String) => exit
            }
            """);

        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var session = instance.OpenSelectSession("flow");

        var action = Assert.Single(session.Actions);
        Assert.Equal(2, action.ParameterCount);
        Assert.Collection(
            action.Parameters,
            parameter =>
            {
                Assert.Equal("x", parameter.Name);
                Assert.Equal("Integer", parameter.TypeName);
            },
            parameter =>
            {
                Assert.Equal("name", parameter.Name);
                Assert.Equal("String", parameter.TypeName);
            });
    }

    [Fact]
    public async Task AsyncSelectSessionsPreserveSelectLocalState()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                mut value = 0
                mut ready = false

                action "begin" when !ready => {
                    value = 7
                    ready = true
                }

                action "finish" when ready => exit value
            }
            """);

        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var session = await instance.OpenSelectSessionAsync("flow");

        await session.SelectAsync(Action(session, "begin"));
        await session.SelectAsync(Action(session, "finish"));

        Assert.True(session.IsCompleted);
        Assert.Equal(7L, session.GetValue<long>());
    }

    [Fact]
    public async Task EventsAreHiddenAndCanCarryArguments()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                on "completed" (value) => exit value
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.False(select.IsCompleted);
        Assert.Empty(select.Actions);

        await select.SendAsync("completed", 42);

        Assert.True(select.IsCompleted);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task OpenSelectSessionAcceptsHostEvents()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                on "completed" (value) => exit value
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("flow");

        Assert.Empty(session.Actions);
        await session.SendAsync("completed", 42);
        Assert.True(session.IsCompleted);
        Assert.Equal(42L, session.GetValue<long>());
    }

    [Fact]
    public void DuplicateEventsAreRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                on "tick" => { }
                on "tick" => { }
            }
            """);

        Assert.False(compiled.Success);
        Assert.Contains(compiled.Errors, error =>
            error.Code == (int)Minamo.Compiler.CompilerError.SelectDuplicateEvent);
    }

    private static MinamoSelectAction Action(MinamoSelect select, string id) =>
        Action(select.Actions, id);

    private static MinamoSelectProperty Property(MinamoSelect select, string name) =>
        Assert.Single(select.Properties, property =>
            string.Equals(property.Name, name, StringComparison.Ordinal));

    private static MinamoSelectAction Action(MinamoSelectSession select, string id) =>
        Action(select.Actions, id);

    private static MinamoSelectAction Action(IReadOnlyList<MinamoSelectAction> actions, string id) =>
        Assert.Single(actions, action => string.Equals(action.Id, id, StringComparison.Ordinal));
}
