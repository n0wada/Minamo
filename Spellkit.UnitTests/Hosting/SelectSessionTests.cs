using Spellkit.Hosting;
using System.Text;
using Xunit;

namespace Spellkit.UnitTesting;

public sealed class SelectSessionTests
{
    [Fact]
    public async Task InteractiveSelectProvidesTheBasicChoiceApi()
    {
        using var instance = new SpellkitHost().CreateInstance();
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
    public async Task InteractiveSelectSendsHostEvents()
    {
        using var instance = new SpellkitHost().CreateInstance();
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
    public async Task AsyncRunActionsAwaitHostCommands()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var instance = new SpellkitHost()
            .Module("work", module => module.AsyncCommand(
                "Value",
                async _ =>
                {
                    entered.SetResult();
                    return await completion.Task.ConfigureAwait(false);
                }))
            .CreateInstance();
        using var run = await instance.StartAsync("""
            import work

            let flow = select {
                initial state waiting {
                    choose "finish" => exit work.Value()
                }
            }

            do flow
            """);

        var selection = run.SelectAsync("finish");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(selection.IsCompleted);

        completion.SetResult(42);
        Assert.True((await selection).IsCompleted);
        Assert.True(run.IsCompleted);
        Assert.Equal(42L, run.GetValue<long>());
    }

    [Fact]
    public async Task OpenSelectSessionAsyncCreatesAReadySession()
    {
        using var instance = new SpellkitHost().CreateInstance();
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
    public async Task ExecuteAsyncAwaitsAnAsynchronousSelectRunner()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var environment = new SpellkitEnvironment()
            .UseSelectAsync(async select =>
            {
                entered.SetResult();
                await completion.Task.ConfigureAwait(false);
                await select.SelectAsync("finish").ConfigureAwait(false);
            });
        using var instance = new SpellkitHost().CreateInstance(environment);

        var execution = instance.ExecuteAsync("""
            let flow = select {
                initial state waiting {
                    choose "finish" => exit 42
                }
            }

            do flow
            """);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(execution.IsCompleted);

        completion.SetResult();
        var result = await execution;

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public void SelectRequiresExactlyOneInitialStateAndUniqueChoices()
    {
        var host = new SpellkitHost();
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
            error.Code == (int)Spellkit.Compiler.CompilerError.SelectRequiresOneInitialState);

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
            error.Code == (int)Spellkit.Compiler.CompilerError.SelectDuplicateChoice);

        var unknownState = host.Compile("""
            select player {
                initial state stopped {
                    choose "play" => goto missing
                }
            }
            """);
        Assert.False(unknownState.Success);
        Assert.Contains(unknownState.Errors, error =>
            error.Code == (int)Spellkit.Compiler.CompilerError.SelectStateNotFound);
    }

    [Fact]
    public void SelectDoesNotSupportChoiceDescriptions()
    {
        var compiled = new SpellkitHost().Compile("""
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
        var host = new SpellkitHost()
            .Module("music", module =>
            {
                module.Command("Play", _ => null);
                module.Command("Pause", _ => null);
                module.Command("SetVolume", context =>
                {
                    volume = context.Argument<long>("value");
                    return null;
                }, SpellkitCommandParameter.Required<long>("value"));
                module.Command("Move", context =>
                {
                    position = (context.Argument<long>("x"), context.Argument<long>("y"));
                    return null;
                },
                    SpellkitCommandParameter.Required<long>("x"),
                    SpellkitCommandParameter.Required<long>("y"));
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
    public async Task SelectSnapshotsExposeRevisionsAndViews()
    {
        using var instance = new SpellkitHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                mut title = ""

                initial state start {
                    view => ["title": "Start", "step": 1]
                    choose "next"
                        view => ["style": "primary"]
                        => {
                            title = "Done"
                            goto finish
                        }
                }

                state finish {
                    view => ["title": title, "step": 2]
                    choose "exit"
                        view => ["style": "danger"]
                        => exit title
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("flow");
        var initial = session.Snapshot;
        var initialStateView = initial.State.View?
            .GetValue<Dictionary<string, object?>>()
            ?? throw new InvalidOperationException("The initial state view is unavailable.");
        var initialChoiceView = Assert.Single(initial.Choices).View?
            .GetValue<Dictionary<string, object?>>()
            ?? throw new InvalidOperationException("The initial choice view is unavailable.");

        Assert.Equal("flow", initial.Name);
        Assert.Equal(0L, initial.Revision);
        Assert.Equal("start", initial.State.Id);
        Assert.Equal("Start", initialStateView["title"]);
        Assert.Equal(1, initialStateView["step"]);
        Assert.Equal("primary", initialChoiceView["style"]);

        var next = await session.SelectAtRevisionAsync("next", initial.Revision);
        var nextStateView = next.Snapshot.State.View?
            .GetValue<Dictionary<string, object?>>()
            ?? throw new InvalidOperationException("The next state view is unavailable.");
        var nextChoiceView = Assert.Single(next.Snapshot.Choices).View?
            .GetValue<Dictionary<string, object?>>()
            ?? throw new InvalidOperationException("The next choice view is unavailable.");

        Assert.Equal(initial.Revision + 1, next.Snapshot.Revision);
        Assert.Equal("finish", next.Snapshot.State.Id);
        Assert.Equal("Done", nextStateView["title"]);
        Assert.Equal("danger", nextChoiceView["style"]);
        Assert.Equal(next.Snapshot.Revision, session.Revision);
        Assert.Equal(next.Snapshot.Revision, session.Snapshot.Revision);

        var mismatch = await Assert.ThrowsAsync<SpellkitSelectRevisionMismatchException>(
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
        using var instance = new SpellkitHost()
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
        var stale = await Assert.ThrowsAsync<SpellkitSelectRevisionMismatchException>(
            () => session.SelectAtRevisionAsync("finish", initial.Revision));
        Assert.Equal(invalidated.Revision, stale.Snapshot.Revision);

        var completed = await session.SelectAtRevisionAsync("finish", availableAgain.Revision);

        Assert.True(completed.IsCompleted);
        Assert.Equal("done", completed.GetValue<string>());
    }

    [Fact]
    public async Task RefreshAndInvalidateAsyncFollowTheSameRevisionRules()
    {
        using var instance = new SpellkitHost().CreateInstance();
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
        await Assert.ThrowsAsync<SpellkitSelectRevisionMismatchException>(
            () => session.SelectAtRevisionAsync("finish", initial.Revision));

        var completed = await session.SelectAtRevisionAsync("finish", invalidated.Revision);
        Assert.True(completed.IsCompleted);
    }

    [Fact]
    public async Task AsyncRevisionBoundEventsRejectStaleSnapshots()
    {
        using var instance = new SpellkitHost().CreateInstance();
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

        var mismatch = await Assert.ThrowsAsync<SpellkitSelectRevisionMismatchException>(
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
    public async Task DynamicChoicesExposeViewsAndBindTheirItems()
    {
        using var instance = new SpellkitHost().CreateInstance();
        var initialized = instance.Execute("""
            select shop {
                initial state browse {
                    choose "leave" => exit "left"

                    for item in [
                        (id: "apple", name: "Apple", enabled: true, price: 3),
                        (id: "pear", name: "Pear", enabled: false, price: 5)
                    ] {
                        choose item.id
                            label item.name
                            when item.enabled
                            view => ["price": item.price]
                            => exit item.price
                    }
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("shop");
        var snapshot = session.Snapshot;

        Assert.Equal(new[] { "leave", "apple" }, snapshot.Choices.Select(choice => choice.Id));
        var apple = snapshot.Choices.Single(choice => choice.Id == "apple");
        Assert.Equal("Apple", apple.Label);
        var appleView = apple.View?.GetValue<Dictionary<string, object?>>()
            ?? throw new InvalidOperationException("The dynamic choice view is unavailable.");
        Assert.Equal(3, appleView["price"]);
        await Assert.ThrowsAsync<ArgumentException>(() => session.SelectAsync("apple", 1));

        var completed = await session.SelectAtRevisionAsync("apple", snapshot.Revision);

        Assert.True(completed.IsCompleted);
        Assert.Equal(3L, completed.GetValue<long>());
    }

    [Fact]
    public void EmptyDynamicChoiceSourceKeepsTheStateActive()
    {
        using var instance = new SpellkitHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                initial state waiting {
                    for item in [] {
                        choose item
                            => exit item
                    }
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
        using var instance = new SpellkitHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                initial state waiting {
                    for item in ["same", "same"] {
                        choose item
                            => exit
                    }
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        var error = Assert.Throws<InvalidOperationException>(() => instance.OpenSelectSession("flow"));
        Assert.Contains("duplicate choice ID 'same'", error.Message);
    }

    [Fact]
    public void OtherwiseRunsWhenDynamicChoicesAreEmpty()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let flow = select {
                initial state waiting {
                    for item in [] {
                        choose item
                            => exit item
                    }

                    otherwise => exit "empty"
                }
            }

            do flow
            """);

        Assert.True(run.IsCompleted, run.Failure?.ToString());
        Assert.Equal("empty", run.GetValue<string>());
    }

    [Fact]
    public async Task ScriptDoRunsTheSelectAndThenContinuesExecution()
    {
        var output = new StringBuilder();
        var invoked = 0;
        using var instance = new SpellkitHost().CreateInstance(
            new SpellkitEnvironment()
                .UseOutput(value => output.Append(value))
                .UseSelectAsync(async select =>
                {
                    invoked++;
                    Assert.Equal("player", select.Name);
                    await select.SelectAsync("exit");
                }));

        var result = await instance.ExecuteAsync("""
            select player {
                initial state stopped {
                    choose "exit" => exit "done"
                }
            }

            alias(player, "music.player")
            let selectResult = do "music.player"
            print(selectResult + " after", terminator: nil)
            """);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(1, invoked);
        Assert.Equal("done after", output.ToString());
    }

    [Fact]
    public async Task StartSuspendsAtDoAndResumesAfterExit()
    {
        var output = new StringBuilder();
        using var instance = new SpellkitHost().CreateInstance(
            new SpellkitEnvironment().UseOutput(value => output.Append(value)));

        using var run = instance.Start("""
            select player {
                initial state stopped {
                    choose "exit" => exit
                }
            }

            func game() {
                let score = 40
                do player
                print(score + 2, terminator: nil)
            }

            game()
            """);

        Assert.True(run.IsWaitingForSelect, run.Failure?.ToString());
        Assert.Single(run.Choices);
        Assert.Equal("exit", run.Choices[0].Id);

        var result = await run.SelectAsync("exit");

        Assert.True(result.IsCompleted);
        Assert.True(run.IsCompleted);
        Assert.Equal("42", output.ToString());
    }

    [Fact]
    public async Task NestedDoResumesTheOuterChoiceAfterInnerExit()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            select shop {
                initial state open {
                    choose "leave" => exit
                }
            }

            select town {
                initial state square {
                    choose "shop" => {
                        do shop
                        goto square
                    }

                    choose "exit" => exit
                }
            }

            do town
            """);

        Assert.Equal(new[] { "shop", "exit" }, run.Choices.Select(choice => choice.Id));

        var inShop = await run.SelectAsync("shop");
        Assert.Equal(new[] { "leave" }, inShop.Choices.Select(choice => choice.Id));

        var backInTown = await run.SelectAsync("leave");
        Assert.Equal(new[] { "shop", "exit" }, backInTown.Choices.Select(choice => choice.Id));

        Assert.True((await run.SelectAsync("exit")).IsCompleted);
        Assert.True(run.IsCompleted);
    }

    [Fact]
    public void DoWhileRemainsDistinctFromSelectInvocation()
    {
        using var instance = new SpellkitHost().CreateInstance();

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
        using var instance = new SpellkitHost().CreateInstance();

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
        using var instance = new SpellkitHost().CreateInstance();
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
        using var instance = new SpellkitHost().CreateInstance();
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
        using var instance = new SpellkitHost().CreateInstance();
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
    public async Task DoEvaluatesAnAnonymousSelectFactoryExpression()
    {
        var output = new StringBuilder();
        using var instance = new SpellkitHost().CreateInstance(
            new SpellkitEnvironment().UseOutput(value => output.Append(value)));
        using var run = instance.Start("""
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

            do createShop("weapons")
            print(" done", terminator: nil)
            """);

        Assert.True(run.IsWaitingForSelect, run.Failure?.ToString());
        Assert.Equal("leave", Assert.Single(run.Choices).Id);

        var result = await run.SelectAsync("leave");

        Assert.True(result.IsCompleted);
        Assert.True(run.IsCompleted);
        Assert.Equal("weapons done", output.ToString());
    }

    [Fact]
    public async Task DoCanUseAFactoryVariable()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let player = select {
                initial state stopped {
                    choose "exit" => exit
                }
            }

            do player
            """);

        Assert.True(run.IsWaitingForSelect, run.Failure?.ToString());
        Assert.True((await run.SelectAsync("exit")).IsCompleted);
    }

    [Fact]
    public async Task DoCreatesASelectLocalFrame()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let player = select {
                mut ready = false

                initial state stopped {
                    choose "ready" when !ready => {
                        ready = true
                    }

                    choose "exit" when ready => exit
                }
            }

            do player
            """);

        Assert.Equal("ready", Assert.Single(run.Choices).Id);
        Assert.Equal("exit", Assert.Single((await run.SelectAsync("ready")).Choices).Id);
        Assert.True((await run.SelectAsync("exit")).IsCompleted);
    }

    [Fact]
    public void SuspendedRunCanEvaluateGuardsWhenChoicesAreRead()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let available = true
            let player = select {
                initial state stopped {
                    choose "exit" when available => exit
                }
            }

            do player
            """);

        Assert.True(run.IsWaitingForSelect, run.Failure?.ToString());
        Assert.Equal("exit", Assert.Single(run.Choices).Id);
    }

    [Fact]
    public void DoImmediatelyContinuesPastAnEmptyInitialState()
    {
        var output = new StringBuilder();
        using var instance = new SpellkitHost().CreateInstance(
            new SpellkitEnvironment().UseOutput(value => output.Append(value)));
        using var run = instance.Start("""
            let empty = select {
                initial state empty {
                }
            }

            do empty
            print("after", terminator: nil)
            """);

        Assert.True(run.IsCompleted, run.Failure?.ToString());
        Assert.False(run.IsWaitingForSelect);
        Assert.Equal("after", output.ToString());
    }

    [Fact]
    public void OtherwiseHandlesAStateWithoutAvailableChoices()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let available = false
            let flow = select {
                initial state waiting {
                    choose "finish" when available => exit "choice"
                    otherwise => exit "otherwise"
                }
            }

            do flow
            """);

        Assert.True(run.IsCompleted, run.Failure?.ToString());
        Assert.Equal("otherwise", run.GetValue<string>());
    }

    [Fact]
    public async Task OtherwiseRunsAfterTheLastChoiceBecomesUnavailable()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let flow = select {
                mut available = true

                initial state waiting {
                    choose "disable" when available => { available = false }
                    choose "finish" when available => exit "choice"
                    otherwise => exit "otherwise"
                }
            }

            do flow
            """);

        Assert.Equal(new[] { "disable", "finish" }, run.Choices.Select(choice => choice.Id));
        var result = await run.SelectAsync("disable");

        Assert.True(result.IsCompleted);
        Assert.True(run.IsCompleted);
        Assert.Equal("otherwise", run.GetValue<string>());
    }

    [Fact]
    public async Task OtherwiseDoesNotHideHostEvents()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let flow = select {
                initial state waiting {
                    on "completed" => exit "event"
                    otherwise => exit "otherwise"
                }
            }

            do flow
            """);

        Assert.True(run.IsWaitingForSelect, run.Failure?.ToString());
        Assert.Empty(run.Choices);
        Assert.Equal("event", (await run.SendAsync("completed")).GetValue<string>());
    }

    [Fact]
    public async Task GotoAnEmptyStateCompletesImmediately()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let flow = select {
                initial state open {
                    choose "finish" => goto done
                }

                state done {
                }
            }

            do flow
            """);

        Assert.True(run.IsWaitingForSelect, run.Failure?.ToString());
        Assert.True((await run.SelectAsync("finish")).IsCompleted);
        Assert.True(run.IsCompleted);
    }

    [Fact]
    public async Task SelectLocalStateIsAvailableToStateActionsAndHooks()
    {
        var output = new StringBuilder();
        using var instance = new SpellkitHost().CreateInstance(
            new SpellkitEnvironment().UseOutput(value => output.Append(value)));
        using var run = instance.Start("""
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
                    otherwise => exit value
                }
            }

            do flow
            """);

        Assert.Equal("begin", Assert.Single(run.Choices).Id);
        await run.SelectAsync("begin");
        Assert.Equal("enter:,10,", output.ToString());
        var add = Assert.Single(run.Choices);
        Assert.Equal(1, add.ParameterCount);
        Assert.Equal("delta", Assert.Single(add.Parameters).Name);

        var result = await run.SelectAsync("add", 10);

        Assert.True(result.IsCompleted);
        Assert.True(run.IsCompleted);
        Assert.Equal(20L, run.GetValue<long>());
        Assert.Equal("enter:,10,leave:,20,enter:,20,leave:,20,", output.ToString());
    }

    [Fact]
    public void StateParametersAndTransitionArgumentsAreNotSupported()
    {
        var compiled = new SpellkitHost().Compile("""
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
    public async Task NestedDoCanUseAnAnonymousFactoryVariable()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let shop = select {
                initial state open {
                    choose "leave" => exit
                }
            }

            select town {
                initial state square {
                    choose "shop" => {
                        do shop
                        goto square
                    }

                    choose "exit" => exit
                }
            }

            do town
            """);

        Assert.Equal(new[] { "shop", "exit" }, run.Choices.Select(choice => choice.Id));
        Assert.Equal("leave", Assert.Single((await run.SelectAsync("shop")).Choices).Id);
        Assert.Equal(new[] { "shop", "exit" }, (await run.SelectAsync("leave")).Choices.Select(choice => choice.Id));
        Assert.True((await run.SelectAsync("exit")).IsCompleted);
    }

    [Fact]
    public void DoRejectsValuesThatAreNotSelectFactories()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("do 42");

        Assert.True(run.IsCompleted);
        Assert.NotNull(run.Failure);
        Assert.Contains("select factory", run.Failure!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DoExpressionReturnsTheSelectExitValue()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let quest = select {
                initial state open {
                    choose "finish" => exit 41
                }
            }

            let result = do quest
            result + 1
            """);

        Assert.True(run.IsWaitingForSelect, run.Failure?.ToString());
        Assert.True((await run.SelectAsync("finish")).IsCompleted);
        Assert.True(run.IsCompleted);
        Assert.Equal(42L, run.GetValue<long>());
    }

    [Fact]
    public async Task DoTreatsPropertyAccessAsAnOrdinaryFactoryExpression()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let ui = (
                currentShop: select {
                    initial state open {
                        choose "leave" => exit "closed"
                    }
                }
            )

            do ui.currentShop
            """);

        Assert.Equal("leave", Assert.Single(run.Choices).Id);
        Assert.True((await run.SelectAsync("leave")).IsCompleted);
        Assert.Equal("closed", run.GetValue<string>());
    }

    [Fact]
    public void ChoiceParametersExposeTheirNamesAndTypes()
    {
        using var instance = new SpellkitHost().CreateInstance();
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
        using var instance = new SpellkitHost().CreateInstance(
            new SpellkitEnvironment().UseOutput(value => output.Append(value)));
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
        using var instance = new SpellkitHost().CreateInstance(
            new SpellkitEnvironment().UseOutput(value => output.Append(value)));
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
        using var instance = new SpellkitHost().CreateInstance();
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
    public async Task NestedDoReturnsItsExitValueToTheOuterAction()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let shop = select {
                initial state open {
                    choose "buy" => exit 41
                }
            }

            let town = select {
                initial state square {
                    choose "shop" => {
                        let purchase = do shop
                        exit purchase + 1
                    }
                }
            }

            do town
            """);

        Assert.Equal("shop", Assert.Single(run.Choices).Id);
        Assert.Equal("buy", Assert.Single((await run.SelectAsync("shop")).Choices).Id);
        Assert.True((await run.SelectAsync("buy")).IsCompleted);
        Assert.Equal(42L, run.GetValue<long>());
    }

    [Fact]
    public async Task EventsAreHiddenAndCanCarryArguments()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let flow = select {
                initial state waiting {
                    on "completed" (value) => exit value
                }
            }

            do flow
            """);

        Assert.True(run.IsWaitingForSelect, run.Failure?.ToString());
        Assert.Empty(run.Choices);

        var result = await run.SendAsync("completed", 42);

        Assert.True(result.IsCompleted);
        Assert.True(run.IsCompleted);
        Assert.Equal(42L, run.GetValue<long>());
    }

    [Fact]
    public async Task OpenSelectSessionAcceptsHostEvents()
    {
        using var instance = new SpellkitHost().CreateInstance();
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
        var compiled = new SpellkitHost().Compile("""
            select flow {
                initial state waiting {
                    on "tick" => { }
                    on "tick" => { }
                }
            }
            """);

        Assert.False(compiled.Success);
        Assert.Contains(compiled.Errors, error =>
            error.Code == (int)Spellkit.Compiler.CompilerError.SelectDuplicateEvent);
    }
}
