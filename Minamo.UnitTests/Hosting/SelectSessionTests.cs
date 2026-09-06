using Minamo.Hosting;
using System.Text;
using Xunit;

namespace Minamo.UnitTesting;

public sealed class SelectSessionTests
{
    [Fact]
    public async Task InteractiveSelectProvidesTheBasicChoiceApi()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select flow {
                initial state ready {
                    choose "finish" (value) => exit value
                }
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var select = await instance.OpenSelectAsync("flow");
        Assert.Equal("ready", select.State);
        Assert.False(select.IsCompleted);
        Assert.Equal("finish", Assert.Single(select.Choices).Id);

        var result = await select.SelectAsync("finish", 42);

        Assert.True(result.IsCompleted);
        Assert.True(select.IsCompleted);
        Assert.Empty(select.Choices);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public async Task StatelessSelectRepublishesChoicesUntilExit()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                mut count = 0

                choose "add" when count == 0 => {
                    count += 1
                }

                choose "finish" when count == 1 => exit count
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = instance.OpenSelectSession("flow");
        Assert.Equal(string.Empty, select.Snapshot.State.Id);
        Assert.Equal("add", Assert.Single(select.Snapshot.Choices).Id);

        var afterAdd = await select.SelectAsync("add");
        Assert.False(afterAdd.IsCompleted);
        Assert.Equal("finish", Assert.Single(afterAdd.Choices).Id);

        var completed = await select.SelectAsync("finish");
        Assert.True(completed.IsCompleted);
        Assert.Equal(1L, completed.GetValue<long>());
    }

    [Fact]
    public async Task StatelessSelectResolvesGotoAtRunTime()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                choose "next" => goto another
            }
            """);

        Assert.True(compiled.Success);

        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                choose "next" => goto another
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = instance.OpenSelectSession("flow");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => select.SelectAsync("next"));
        Assert.Contains("no state named 'another'", error.Message);
    }

    [Fact]
    public void StatelessSelectRejectsChoiceSpreads()
    {
        var compiled = new MinamoHost().Compile("""
            select child {
                choose "finish" => exit
            }

            select parent {
                choose ...child
            }
            """);

        Assert.False(compiled.Success);
        Assert.Contains(compiled.Errors, error =>
            error.Code == (int)Minamo.Compiler.CompilerError.SelectChoiceSpreadRequiresNamedState);
    }

    [Fact]
    public void SelectDescriptionRequiresAStringKeyDictionaryLiteral()
    {
        var host = new MinamoHost();
        var nonDictionary = host.Compile("""
            select flow {
                desc ["title"]
                initial state ready { choose "finish" => exit }
            }
            """);
        var nonStringKey = host.Compile("""
            select flow {
                desc [title: "Ready"]
                initial state ready { choose "finish" => exit }
            }
            """);
        var stateDescription = host.Compile("""
            select flow {
                initial state ready {
                    desc ["title": "Ready"]
                    choose "finish" => exit
                }
            }
            """);

        Assert.False(nonDictionary.Success);
        Assert.False(nonStringKey.Success);
        Assert.False(stateDescription.Success);
    }

    [Fact]
    public async Task StatelessChildSelectChoicesCanBeSpreadIntoAParentState()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select child {
                mut count = 0

                choose "next" when count == 0 => { count += 1 }
                choose "finish" when count == 1 => exit "child"
            }

            select parent {
                initial state open {
                    choose "cancel" => exit "cancel"
                    choose ...child
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("parent");

        Assert.Equal(new[] { "cancel", "next" }, select.Choices.Select(choice => choice.Id));

        var afterNext = await select.SelectAsync("next");
        Assert.False(afterNext.IsCompleted);
        Assert.Equal(new[] { "cancel", "finish" }, select.Choices.Select(choice => choice.Id));

        var afterFinish = await select.SelectAsync("finish");
        Assert.True(afterFinish.IsCompleted);
        Assert.Equal("child", afterFinish.GetValue<string>());
    }

    [Fact]
    public void ChoiceSpreadRejectsAStatefulChildSelect()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select child {
                initial state open {
                    choose "finish" => exit
                }
            }

            select parent {
                initial state open {
                    choose ...child
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        var error = Assert.Throws<InvalidOperationException>(() => instance.OpenSelectSession("parent"));
        Assert.Contains("state-less select", error.Message);
    }

    [Fact]
    public async Task ExpandedChildGotoMovesTheParentToItsState()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select child {
                choose "next" => goto done
            }

            select parent {
                initial state open {
                    choose ...child
                }

                state done {
                    choose "finish" => exit "done"
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("parent");

        Assert.Equal("next", Assert.Single(select.Choices).Id);
        var afterNext = await select.SelectAsync("next");
        Assert.False(afterNext.IsCompleted);
        Assert.Equal("done", select.State);
        Assert.Equal("finish", Assert.Single(select.Choices).Id);
        Assert.Equal("done", (await select.SelectAsync("finish")).GetValue<string>());
    }

    [Fact]
    public async Task ExpandedChildHooksAndEventsRunBeforeTheParent()
    {
        var output = new StringBuilder();
        using var instance = new MinamoHost().CreateInstance(
            new MinamoEnvironment().UseOutput(value => output.Append(value)));
        var initialized = await instance.ExecuteAsync("""
            select child {
                desc ["kind": "child"]
                enter => { print("child-enter", terminator: ",") }
                leave => { print("child-leave", terminator: ",") }
                on "tick" => { print("child-event", terminator: ",") }
                choose "finish" => exit
            }

            select parent {
                initial state open {
                    enter => { print("parent-enter", terminator: ",") }
                    leave => { print("parent-leave", terminator: ",") }
                    on "tick" => { print("parent-event", terminator: ",") }
                    choose ...child
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("parent");

        Assert.Equal("child-enter,parent-enter,", output.ToString());
        await select.SendAsync("tick");
        Assert.Equal("child-enter,parent-enter,child-event,parent-event,", output.ToString());

        Assert.True((await select.SelectAsync("finish")).IsCompleted);
        Assert.Equal(
            "child-enter,parent-enter,child-event,parent-event,child-leave,parent-leave,",
            output.ToString());
    }

    [Fact]
    public async Task AnEmptyExpandedChildAllowsTheParentEmptyHandlerToRun()
    {
        var output = new StringBuilder();
        using var instance = new MinamoHost().CreateInstance(
            new MinamoEnvironment().UseOutput(value => output.Append(value)));
        var initialized = await instance.ExecuteAsync("""
            select child {
                choose "hidden" when false => exit
                on empty => { print("child", terminator: ",") }
            }

            select parent {
                initial state open {
                    choose ...child
                    on empty => { print("parent", terminator: ","); exit "parent" }
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("parent");

        Assert.True(select.IsCompleted);
        Assert.Equal("parent", new MinamoSelectResult(select.Snapshot, select.CompletionValue).GetValue<string>());
        Assert.Equal("child,parent,", output.ToString());
    }

    [Fact]
    public async Task PublishedChoicesRejectInputAfterInvalidation()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select flow {
                initial state ready {
                    choose "finish" => exit 42
                }
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var select = await instance.OpenSelectAsync("flow");
        var staleChoice = Assert.Single(select.Choices);
        var initialRevision = select.Revision;

        await select.InvalidateAsync();

        Assert.Equal(initialRevision + 1, select.Revision);
        var mismatch = await Assert.ThrowsAsync<MinamoSelectRevisionMismatchException>(
            () => select.SelectAsync(staleChoice));
        Assert.Equal(initialRevision, mismatch.ExpectedRevision);
        Assert.Equal(select.Revision, mismatch.CurrentRevision);

        var result = await select.SelectAsync(Assert.Single(select.Choices));
        Assert.True(result.IsCompleted);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public async Task PublicSelectExposesDescriptionAndKeepsTheLastPublicationOnRefreshFailure()
    {
        var items = new[] { "old" };
        var failItems = false;
        var descriptionCalls = 0;
        using var instance = new MinamoHost()
            .Module("catalog", module =>
            {
                module.Command("Items", _ =>
                {
                    if (failItems)
                    {
                        throw new InvalidOperationException("Items failed.");
                    }

                    return items;
                });
                module.Command("Description", _ => $"Description {++descriptionCalls}");
            })
            .CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            import catalog

            select flow {
                desc ["title": catalog.Description()]

                initial state ready {
                    choose item
                        for item in catalog.Items()
                        => exit item
                }
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var select = await instance.OpenSelectAsync("flow");
        Assert.Equal("Description 1", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);
        Assert.Equal(1, descriptionCalls);
        var oldChoice = Assert.Single(select.Choices);
        var initialRevision = select.Revision;
        Assert.Equal("old", oldChoice.Id);

        items = ["new"];
        failItems = true;
        await Assert.ThrowsAnyAsync<Exception>(() => select.RefreshAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => select.InvalidateAsync());

        Assert.Equal(initialRevision, select.Revision);
        Assert.Equal(1, descriptionCalls);
        Assert.Same(oldChoice, Assert.Single(select.Choices));
        var result = await select.SelectAsync(oldChoice);
        Assert.True(result.IsCompleted);
        Assert.Equal("old", result.GetValue<string>());
    }

    [Fact]
    public async Task SelectKeepsItsFinalPublishedStateAndRejectsStaleChoices()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                desc ["title": "Ready"]

                initial state ready {
                    choose "finish" => exit 42
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        Assert.False(select.IsCompleted);
        Assert.Equal("ready", select.State);
        Assert.Equal("Ready", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);
        var staleChoice = Assert.Single(select.Choices);
        var initialRevision = Assert.IsType<long>(select.Revision);

        await select.RefreshAsync();
        Assert.Equal(initialRevision, select.Revision);

        await select.InvalidateAsync();

        Assert.Equal(initialRevision + 1, select.Revision);
        await Assert.ThrowsAsync<MinamoSelectRevisionMismatchException>(
            () => select.SelectAsync(staleChoice));

        var result = await select.SelectAsync(Assert.Single(select.Choices));
        Assert.True(result.IsCompleted);
        Assert.True(select.IsCompleted);
        Assert.Equal("ready", select.State);
        Assert.Equal(initialRevision + 2, select.Revision);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public async Task InteractiveSelectSendsHostEvents()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select flow {
                initial state ready {
                    on "complete" (value) => exit value
                }
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var select = await instance.OpenSelectAsync("flow");
        Assert.Empty(select.Choices);

        var result = await select.SendAsync("complete", "done");

        Assert.True(result.IsCompleted);
        Assert.Equal("done", result.GetValue<string>());
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
                initial state waiting {
                    choose "finish" => exit work.Value()
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        var selection = select.SelectAsync("finish");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(selection.IsCompleted);

        completion.SetResult(42);
        var result = await selection;
        Assert.True(result.IsCompleted);
        Assert.True(select.IsCompleted);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public async Task OpenSelectSessionAsyncCreatesAReadySession()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select flow {
                initial state ready {
                    choose "finish" => exit 42
                }
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var session = await instance.OpenSelectSessionAsync("flow");
        Assert.Equal("ready", session.State);
        Assert.True((await session.SelectAsync("finish")).IsCompleted);
    }

    [Fact]
    public void SelectRequiresExactlyOneInitialStateAndUniqueChoices()
    {
        var host = new MinamoHost();
        var stringState = host.Compile("""
            select player {
                initial state "stopped" {
                    choose "play" => { }
                }
            }
            """);
        Assert.False(stringState.Success);

        var missingInitial = host.Compile("""
            select player {
                state stopped {
                    choose "play" => { }
                }
            }
            """);
        Assert.False(missingInitial.Success);
        Assert.Contains(missingInitial.Errors, error =>
            error.Code == (int)Minamo.Compiler.CompilerError.SelectRequiresOneInitialState);

        var duplicateChoice = host.Compile("""
            select player {
                initial state stopped {
                    choose "play" => { }
                    choose "play" => { }
                }
            }
            """);
        Assert.False(duplicateChoice.Success);
        Assert.Contains(duplicateChoice.Errors, error =>
            error.Code == (int)Minamo.Compiler.CompilerError.SelectDuplicateChoice);

        var unknownState = host.Compile("""
            select player {
                initial state stopped {
                    choose "play" => goto missing
                }
            }
            """);
        Assert.False(unknownState.Success);
        Assert.Contains(unknownState.Errors, error =>
            error.Code == (int)Minamo.Compiler.CompilerError.SelectStateNotFound);
    }

    [Fact]
    public void SelectDoesNotSupportChoiceDescriptions()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                initial state ready {
                    choose "finish" description "Legacy display text" => exit
                }
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public async Task SelectSessionTransitionsAndAcceptsTupleArguments()
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
                initial state stopped {
                    choose "play" => goto playing

                    choose "set-volume" (value) => {
                        music.SetVolume(value)
                    }

                    choose "move" (x, y) => {
                        music.Move(x, y)
                    }

                    choose "status" label "Show status" when true => {
                    }

                    choose "hidden" label "Hidden command" when false => {
                    }
                }

                state playing {
                    choose "pause" => goto paused
                }

            state paused {
                    choose "exit" => exit "done"
                }
            }

            alias(player, "music.player")
            """).GetValueOrThrow();

        using var instance = host.CreateInstance(program);
        using var session = instance.OpenSelectSession("music.player");

        Assert.Equal("stopped", session.State);
        Assert.Collection(session.Choices,
            choice => Assert.Equal("play", choice.Id),
            choice => Assert.Equal("set-volume", choice.Id),
            choice => Assert.Equal("move", choice.Id),
            choice =>
            {
                Assert.Equal("status", choice.Id);
                Assert.Equal("Show status", choice.Label);
            });
        await Assert.ThrowsAsync<ArgumentException>(() => session.SelectAsync("hidden"));

        var afterVolume = await session.SelectAsync("set-volume", 80);
        Assert.False(afterVolume.IsCompleted);
        Assert.Equal(80, volume);

        await session.SelectAsync("move", (12, 34));
        Assert.Equal((12L, 34L), position);

        var afterPlay = await session.SelectAsync("play");
        Assert.Equal("playing", session.State);
        Assert.Single(afterPlay.Choices);
        Assert.Equal("pause", afterPlay.Choices[0].Id);

        var afterPause = await session.SelectAsync("pause");
        Assert.Equal("paused", session.State);
        Assert.Single(afterPause.Choices);
        Assert.Equal("exit", afterPause.Choices[0].Id);

        var completed = await session.SelectAsync("exit");
        Assert.True(completed.IsCompleted);
        Assert.Equal("done", completed.GetValue<string>());
    }

    [Fact]
    public async Task SelectSnapshotsExposeRevisionsAndDescription()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                desc ["title": "Flow", "step": 1]

                initial state start {
                    choose "next"
                        => {
                            goto finish
                        }
                }

                state finish {
                    choose "exit"
                        => exit "Done"
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("flow");
        var initial = session.Snapshot;
        var initialDescription = initial.Description?
            .GetValue<Dictionary<string, object?>>()
            ?? throw new InvalidOperationException("The select description is unavailable.");
        Assert.Equal("flow", initial.Name);
        Assert.Equal(0L, initial.Revision);
        Assert.Equal("start", initial.State.Id);
        Assert.Equal("Flow", initialDescription["title"]);
        Assert.Equal(1, initialDescription["step"]);

        var next = await session.SelectAtRevisionAsync("next", initial.Revision);
        var nextDescription = next.Snapshot.Description?
            .GetValue<Dictionary<string, object?>>()
            ?? throw new InvalidOperationException("The select description is unavailable.");
        Assert.Equal(initial.Revision + 1, next.Snapshot.Revision);
        Assert.Equal("finish", next.Snapshot.State.Id);
        Assert.Same(initial.Description, next.Snapshot.Description);
        Assert.Equal("Flow", nextDescription["title"]);
        Assert.Equal(next.Snapshot.Revision, session.Revision);
        Assert.Equal(next.Snapshot.Revision, session.Snapshot.Revision);

        var mismatch = await Assert.ThrowsAsync<MinamoSelectRevisionMismatchException>(
            () => session.SelectAtRevisionAsync("exit", initial.Revision));
        Assert.Equal(initial.Revision, mismatch.ExpectedRevision);
        Assert.Equal(next.Snapshot.Revision, mismatch.Snapshot.Revision);
        Assert.Equal("finish", mismatch.Snapshot.State.Id);

        var completed = await session.SelectAtRevisionAsync("exit", next.Snapshot.Revision);

        Assert.True(completed.IsCompleted);
        Assert.Equal(next.Snapshot.Revision + 1, completed.Snapshot.Revision);
        Assert.Equal("finish", completed.Snapshot.State.Id);
        Assert.Equal("Done", completed.GetValue<string>());
    }

    [Fact]
    public async Task RefreshReevaluatesTheSnapshotAndInvalidateRejectsStaleChoices()
    {
        var available = true;
        using var instance = new MinamoHost()
            .Module("inventory", module => module.Command("IsAvailable", _ => available))
            .CreateInstance();
        var initialized = instance.Execute("""
            import inventory

            select flow {
                initial state waiting {
                    choose "finish"
                        when inventory.IsAvailable()
                        => exit "done"
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("flow");
        var initial = session.Snapshot;
        available = false;
        var refreshed = await session.RefreshAsync();

        Assert.Equal(initial.Revision, refreshed.Revision);
        Assert.Empty(refreshed.Choices);

        var invalidated = await session.InvalidateAsync();

        Assert.Equal(initial.Revision + 1, invalidated.Revision);
        Assert.Empty(invalidated.Choices);
        Assert.Equal(invalidated.Revision, session.Revision);
        available = true;
        var availableAgain = await session.RefreshAsync();
        Assert.Equal(invalidated.Revision, availableAgain.Revision);
        Assert.Single(availableAgain.Choices);
        var stale = await Assert.ThrowsAsync<MinamoSelectRevisionMismatchException>(
            () => session.SelectAtRevisionAsync("finish", initial.Revision));
        Assert.Equal(invalidated.Revision, stale.Snapshot.Revision);

        var completed = await session.SelectAtRevisionAsync("finish", availableAgain.Revision);

        Assert.True(completed.IsCompleted);
        Assert.Equal("done", completed.GetValue<string>());
    }

    [Fact]
    public async Task RefreshAndInvalidateAsyncFollowTheSameRevisionRules()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                initial state waiting {
                    choose "finish" => exit
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = await instance.OpenSelectSessionAsync("flow");
        var initial = await session.RefreshAsync();
        var invalidated = await session.InvalidateAsync();

        Assert.Equal(initial.Revision + 1, invalidated.Revision);
        await Assert.ThrowsAsync<MinamoSelectRevisionMismatchException>(
            () => session.SelectAtRevisionAsync("finish", initial.Revision));

        var completed = await session.SelectAtRevisionAsync("finish", invalidated.Revision);
        Assert.True(completed.IsCompleted);
    }

    [Fact]
    public async Task AsyncRevisionBoundEventsRejectStaleSnapshots()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                initial state waiting {
                    on "advance" => goto ready
                }

                state ready {
                    on "finish" (value) => exit value
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = await instance.OpenSelectSessionAsync("flow");
        var waiting = session.Snapshot;
        var ready = await session.SendAtRevisionAsync("advance", waiting.Revision);

        var mismatch = await Assert.ThrowsAsync<MinamoSelectRevisionMismatchException>(
            () => session.SendAtRevisionAsync("finish", 42, waiting.Revision));
        Assert.Equal(waiting.Revision, mismatch.ExpectedRevision);
        Assert.Equal(ready.Snapshot.Revision, mismatch.Snapshot.Revision);
        Assert.Equal("ready", mismatch.Snapshot.State.Id);

        var completed = await session.SendAtRevisionAsync(
            "finish",
            42,
            ready.Snapshot.Revision);

        Assert.True(completed.IsCompleted);
        Assert.Equal(42L, completed.GetValue<long>());
    }

    [Fact]
    public async Task DynamicChoicesBindTheirItems()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select shop {
                initial state browse {
                    choose "leave" => exit "left"

                    choose item.id
                        label item.name
                        for item in [
                        (id: "apple", name: "Apple", enabled: true, price: 3),
                        (id: "pear", name: "Pear", enabled: false, price: 5)
                        ]
                        when item.enabled
                        => exit item.price
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("shop");
        var snapshot = session.Snapshot;

        Assert.Equal(new[] { "leave", "apple" }, snapshot.Choices.Select(choice => choice.Id));
        var apple = snapshot.Choices.Single(choice => choice.Id == "apple");
        Assert.Equal("Apple", apple.Label);
        await Assert.ThrowsAsync<ArgumentException>(() => session.SelectAsync("apple", 1));

        var completed = await session.SelectAtRevisionAsync("apple", snapshot.Revision);

        Assert.True(completed.IsCompleted);
        Assert.Equal(3L, completed.GetValue<long>());
    }

    [Fact]
    public void EmptyDynamicChoiceSourceKeepsTheStateActive()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                initial state waiting {
                    choose item
                        for item in []
                        => exit item
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("flow");

        Assert.False(session.IsCompleted);
        Assert.Empty(session.Snapshot.Choices);
    }

    [Fact]
    public void DynamicChoiceIdsMustBeUnique()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                initial state waiting {
                    choose item
                        for item in ["same", "same"]
                        => exit
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        var error = Assert.Throws<InvalidOperationException>(() => instance.OpenSelectSession("flow"));
        Assert.Contains("duplicate choice ID 'same'", error.Message);
    }

    [Fact]
    public void LegacyDynamicChoiceGroupSyntaxIsRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                initial state waiting {
                    for item in ["only"] {
                        choose item => exit
                    }
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
                initial state waiting {
                    view => ["style": "primary"]
                    choose "continue" => exit
                }
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public void OtherwiseSyntaxIsRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                initial state waiting {
                    otherwise => exit
                }
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public async Task EmptyHandlerRunsWhenDynamicChoicesAreEmpty()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                initial state waiting {
                    choose item
                        for item in []
                        => exit item

                    on empty => exit "empty"
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.True(select.IsCompleted);
        Assert.Equal("empty", new MinamoSelectResult(select.Snapshot, select.CompletionValue).GetValue<string>());
    }

    [Theory]
    [InlineData("do flow")]
    [InlineData("do \"flow\"")]
    [InlineData("let result = do flow")]
    [InlineData("func invoke() => do flow")]
    [InlineData("select parent { choose \"open\" => { do flow } }")]
    public void ScriptSelectInvocationIsRejected(string invocation)
    {
        var compiled = new MinamoHost().Compile(
            "select flow { choose \"finish\" => exit }\n" + invocation);

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
                    initial state stopped {
                        choose "louder" => {
                            print(volume)
                        }
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
                    initial state square {
                        choose "learn" when !known => {
                            known = true
                        }

                        choose "continue" when known => exit
                    }
                }
            }

            alias(questGame(), "quest.town")
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var first = instance.OpenSelectSession("quest.town");
        Assert.Equal("learn", Assert.Single(first.Choices).Id);
        await first.SelectAsync("learn");

        using var second = instance.OpenSelectSession("quest.town");
        Assert.Equal("continue", Assert.Single(second.Choices).Id);
    }

    [Fact]
    public async Task NamedSelectFactoryCreatesIndependentSessions()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select player {
                initial state stopped {
                    choose "play" => goto playing
                }

                state playing {
                    choose "exit" => exit
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var first = instance.OpenSelectSession("player");
        using var second = instance.OpenSelectSession("player");

        await first.SelectAsync("play");

        Assert.Equal("exit", first.Choices.Single().Id);
        Assert.Equal("play", second.Choices.Single().Id);
    }

    [Fact]
    public async Task SelectLocalsAreCreatedForEachSession()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select player {
                mut ready = false

                initial state stopped {
                    choose "ready" when !ready => {
                        ready = true
                    }

                    choose "exit" when ready => exit
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var first = instance.OpenSelectSession("player");
        Assert.Equal("ready", Assert.Single(first.Choices).Id);
        await first.SelectAsync("ready");
        Assert.Equal("exit", Assert.Single(first.Choices).Id);

        using var second = instance.OpenSelectSession("player");
        Assert.Equal("ready", Assert.Single(second.Choices).Id);
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
                    initial state open {
                        choose "leave" => {
                            print(name, terminator: nil)
                            exit
                        }
                    }
                }
            }

            let shop = createShop("weapons")
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("shop");

        Assert.False(select.IsCompleted);
        Assert.Equal("leave", Assert.Single(select.Choices).Id);

        var result = await select.SelectAsync("leave");

        Assert.True(result.IsCompleted);
        Assert.True(select.IsCompleted);
        Assert.Equal("weapons", output.ToString());
    }

    [Fact]
    public async Task HostCanOpenAFactoryVariable()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let player = select {
                initial state stopped {
                    choose "exit" => exit
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("player");

        Assert.False(select.IsCompleted);
        Assert.True((await select.SelectAsync("exit")).IsCompleted);
    }

    [Fact]
    public async Task OpeningAFactoryVariableCreatesASelectLocalFrame()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let player = select {
                mut ready = false

                initial state stopped {
                    choose "ready" when !ready => {
                        ready = true
                    }

                    choose "exit" when ready => exit
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("player");

        Assert.Equal("ready", Assert.Single(select.Choices).Id);
        Assert.Equal("exit", Assert.Single((await select.SelectAsync("ready")).Choices).Id);
        Assert.True((await select.SelectAsync("exit")).IsCompleted);
    }

    [Fact]
    public async Task OpeningASelectEvaluatesItsGuards()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let available = true
            let player = select {
                initial state stopped {
                    choose "exit" when available => exit
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("player");

        Assert.False(select.IsCompleted);
        Assert.Equal("exit", Assert.Single(select.Choices).Id);
    }

    [Fact]
    public async Task OpeningAnEmptyInitialStateCompletesImmediately()
    {
        var output = new StringBuilder();
        using var instance = new MinamoHost().CreateInstance(
            new MinamoEnvironment().UseOutput(value => output.Append(value)));
        var initialized = await instance.ExecuteAsync("""
            let empty = select {
                initial state empty {
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("empty");

        Assert.True(select.IsCompleted);
        Assert.Empty(select.Choices);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public async Task EmptyHandlerHandlesAStateWithoutAvailableChoices()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let available = false
            let flow = select {
                initial state waiting {
                    choose "finish" when available => exit "choice"
                    on empty => exit "empty"
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.True(select.IsCompleted);
        Assert.Equal("empty", new MinamoSelectResult(select.Snapshot, select.CompletionValue).GetValue<string>());
    }

    [Fact]
    public async Task EmptyHandlerRunsAfterTheLastChoiceBecomesUnavailable()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                mut available = true

                initial state waiting {
                    choose "disable" when available => { available = false }
                    choose "finish" when available => exit "choice"
                    on empty => exit "empty"
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.Equal(new[] { "disable", "finish" }, select.Choices.Select(choice => choice.Id));
        var result = await select.SelectAsync("disable");

        Assert.True(result.IsCompleted);
        Assert.True(select.IsCompleted);
        Assert.Equal("empty", result.GetValue<string>());
    }

    [Fact]
    public async Task EmptyHandlerDoesNotHideHostEvents()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                initial state waiting {
                    on "completed" => exit "event"
                    on empty => exit "empty"
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.False(select.IsCompleted);
        Assert.Empty(select.Choices);
        Assert.Equal("event", (await select.SendAsync("completed")).GetValue<string>());
    }

    [Fact]
    public async Task GotoAnEmptyStateCompletesImmediately()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                initial state open {
                    choose "finish" => goto done
                }

                state done {
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.False(select.IsCompleted);
        Assert.True((await select.SelectAsync("finish")).IsCompleted);
        Assert.True(select.IsCompleted);
    }

    [Fact]
    public async Task SelectLocalStateIsAvailableToStateActionsAndHooks()
    {
        var output = new StringBuilder();
        using var instance = new MinamoHost().CreateInstance(
            new MinamoEnvironment().UseOutput(value => output.Append(value)));
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                mut value = 0

                initial state start {
                    choose "begin" => {
                        value = 10
                        goto counter
                    }
                }

                state counter {
                    enter => { print("enter:", value, terminator: ",") }
                    leave => { print("leave:", value, terminator: ",") }
                    choose "add" (delta: Integer) when value < 20 => {
                        value += delta
                        goto counter
                    }
                    on empty => exit value
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.Equal("begin", Assert.Single(select.Choices).Id);
        await select.SelectAsync("begin");
        Assert.Equal("enter:,10,", output.ToString());
        var add = Assert.Single(select.Choices);
        Assert.Equal(1, add.ParameterCount);
        Assert.Equal("delta", Assert.Single(add.Parameters).Name);

        var result = await select.SelectAsync("add", 10);

        Assert.True(result.IsCompleted);
        Assert.True(select.IsCompleted);
        Assert.Equal(20L, result.GetValue<long>());
        Assert.Equal("enter:,10,leave:,20,enter:,20,leave:,20,", output.ToString());
    }

    [Fact]
    public void StateParametersAndTransitionArgumentsAreNotSupported()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                initial state start {
                    choose "begin" => goto counter(1)
                }

                state counter(value: Integer) {
                    choose "finish" => exit value
                }
            }
            """);

        Assert.False(compiled.Success);
    }

    [Fact]
    public async Task AliasCanExposeAFactoryFromAProperty()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let ui = (
                currentShop: select {
                    initial state open {
                        choose "leave" => exit "closed"
                    }
                }
            )

            alias(ui.currentShop, "shop")
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("shop");

        Assert.Equal("leave", Assert.Single(select.Choices).Id);
        var result = await select.SelectAsync("leave");
        Assert.True(result.IsCompleted);
        Assert.Equal("closed", result.GetValue<string>());
    }

    [Fact]
    public void ChoiceParametersExposeTheirNamesAndTypes()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                initial state ready {
                    choose "move" (x: Integer, name: String) => exit
                }
            }
            """);

        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var session = instance.OpenSelectSession("flow");

        var choice = Assert.Single(session.Choices);
        Assert.Equal(2, choice.ParameterCount);
        Assert.Collection(
            choice.Parameters,
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
    public async Task StateHooksRunWhenStatesAreEnteredAndLeft()
    {
        var output = new StringBuilder();
        using var instance = new MinamoHost().CreateInstance(
            new MinamoEnvironment().UseOutput(value => output.Append(value)));
        var initialized = instance.Execute("""
            select flow {
                initial state open {
                    enter => { print("enter-open", terminator: ",") }
                    leave => { print("leave-open", terminator: ",") }
                    choose "next" => goto closed
                }

                state closed {
                    enter => { print("enter-closed", terminator: ",") }
                    leave => { print("leave-closed", terminator: ",") }
                    choose "finish" => exit
                }
            }
            """);

        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var session = instance.OpenSelectSession("flow");
        Assert.Equal("enter-open,", output.ToString());

        await session.SelectAsync("next");
        Assert.Equal("enter-open,leave-open,enter-closed,", output.ToString());

        Assert.True((await session.SelectAsync("finish")).IsCompleted);
        Assert.Equal("enter-open,leave-open,enter-closed,leave-closed,", output.ToString());
    }

    [Fact]
    public async Task AsyncSelectSessionsRunStateHooks()
    {
        var output = new StringBuilder();
        using var instance = new MinamoHost().CreateInstance(
            new MinamoEnvironment().UseOutput(value => output.Append(value)));
        var initialized = await instance.ExecuteAsync("""
            select flow {
                initial state open {
                    enter => { print("enter-open", terminator: ",") }
                    leave => { print("leave-open", terminator: ",") }
                    choose "next" => goto closed
                }

                state closed {
                    enter => { print("enter-closed", terminator: ",") }
                    choose "finish" => exit
                }
            }
            """);

        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var session = await instance.OpenSelectSessionAsync("flow");
        Assert.Equal("enter-open,", output.ToString());

        await session.SelectAsync("next");
        Assert.Equal("enter-open,leave-open,enter-closed,", output.ToString());
    }

    [Fact]
    public async Task AsyncSelectSessionsPreserveSelectLocalState()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                mut value = 0

                initial state start {
                    choose "begin" => {
                        value = 7
                        goto ready
                    }
                }

                state ready {
                    choose "finish" => exit value
                }
            }
            """);

        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var session = await instance.OpenSelectSessionAsync("flow");

        await session.SelectAsync("begin");
        var result = await session.SelectAsync("finish");

        Assert.True(result.IsCompleted);
        Assert.Equal(7L, result.GetValue<long>());
    }

    [Fact]
    public async Task EventsAreHiddenAndCanCarryArguments()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                initial state waiting {
                    on "completed" (value) => exit value
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.False(select.IsCompleted);
        Assert.Empty(select.Choices);

        var result = await select.SendAsync("completed", 42);

        Assert.True(result.IsCompleted);
        Assert.True(select.IsCompleted);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public async Task OpenSelectSessionAcceptsHostEvents()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                initial state ready {
                    on "completed" (value) => exit value
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("flow");

        Assert.Equal("ready", session.State);
        Assert.Empty(session.Choices);
        var result = await session.SendAsync("completed", 42);
        Assert.True(result.IsCompleted);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public void DuplicateEventsAreRejected()
    {
        var compiled = new MinamoHost().Compile("""
            select flow {
                initial state waiting {
                    on "tick" => { }
                    on "tick" => { }
                }
            }
            """);

        Assert.False(compiled.Success);
        Assert.Contains(compiled.Errors, error =>
            error.Code == (int)Minamo.Compiler.CompilerError.SelectDuplicateEvent);
    }
}
