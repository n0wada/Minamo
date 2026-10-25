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
        await select.SelectAsync("finish");
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

        await select.SelectAsync("finish", 42);

        Assert.True(select.IsCompleted);
        Assert.Empty(select.Actions);
        Assert.Equal(42L, select.GetValue<long>());
        Assert.True(select.TryGetValue<long>(out var value));
        Assert.Equal(42L, value);
    }

    [Fact]
    public async Task StringSelectionsUseTheCurrentSelect()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select child {
                hidden action "finish" => exit "child"
            }
            select root {
                action "open" => goto child
                action "finish" => exit "root"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("root");
        var rootToken = select.Token;

        await select.SelectAsync("open");
        Assert.NotEqual(rootToken, select.Token);
        await Assert.ThrowsAsync<ArgumentException>(
            () => select.SelectAsync("finish", expectedToken: rootToken));
        await select.SelectAsync("finish");

        Assert.Equal("child", select.GetValue<string>());
    }

    [Fact]
    public async Task SelectionsRejectTokensFromAnotherSelect()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                action "finish" (value) => exit value
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");
        using var other = await instance.OpenSelectAsync("flow");
        var token = select.Token;
        Assert.NotEqual(token, other.Token);

        await Assert.ThrowsAsync<ArgumentException>(
            () => select.SelectAsync("finish", expectedToken: other.Token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => select.SelectAsync("finish", 10, expectedToken: other.Token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => select.SelectAsync("finish", 10, expectedToken: default));
        Assert.False(select.IsCompleted);
        Assert.Equal(token, select.Token);

        await select.SelectAsync("finish", 42, expectedToken: new MinamoSelectToken(token.Value));
        Assert.NotEqual(token, select.Token);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuidArgumentsAreNotInterpretedAsPublicationTokens(bool verifyPublication)
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                action "finish" (value) => exit value
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");
        var resourceId = Guid.NewGuid();

        if (verifyPublication)
        {
            await select.SelectAsync("finish", resourceId, expectedToken: select.Token);
        }
        else
        {
            await select.SelectAsync("finish", resourceId);
        }

        Assert.True(select.IsCompleted);
        Assert.Equal(resourceId, select.GetValue<Guid>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("missing")]
    [InlineData("guarded")]
    [InlineData("FINISH")]
    public async Task StringSelectionsRejectInvalidOrUnavailableIds(string? actionId)
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                action "guarded" when false => exit
                action "finish" => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        await Assert.ThrowsAnyAsync<ArgumentException>(() => select.SelectAsync(actionId!));
        Assert.False(select.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionsDistinguishNilArgumentsFromNoArgument(bool verifyPublication)
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                action "finish" (value) => exit value
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        await Assert.ThrowsAsync<ArgumentException>(() => select.SelectAsync("finish"));
        if (verifyPublication)
        {
            await select.SelectAsync("finish", null, expectedToken: select.Token);
        }
        else
        {
            await select.SelectAsync("finish", null);
        }

        Assert.True(select.IsCompleted);
        Assert.Null(select.GetValue<object?>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionsResolveIdsAndValidateTokensAfterEnteringTheGate(bool verifyToken)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var instance = new MinamoHost()
            .Module("work", module => module.AsyncCommand("Wait", async _ =>
            {
                entered.SetResult();
                return await completion.Task;
            }))
            .CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            import work
            select flow {
                mut ready = false
                action "begin" when !ready => {
                    work.Wait()
                    ready = true
                }
                hidden action "finish" when ready => exit "done"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        var token = select.Token;
        var begin = select.SelectAsync("begin");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var finish = verifyToken
            ? select.SelectAsync("finish", expectedToken: token)
            : select.SelectAsync("finish");
        Assert.False(finish.IsCompleted);
        completion.SetResult(42);
        await begin.WaitAsync(TimeSpan.FromSeconds(5));
        if (verifyToken)
        {
            var error = await Assert.ThrowsAsync<ArgumentException>(
                () => finish.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("expectedToken", error.ParamName);
            Assert.False(select.IsCompleted);
            await select.SelectAsync("finish", expectedToken: select.Token);
        }
        else
        {
            await finish.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal("done", select.GetValue<string>());
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

        await select.SelectAsync("finish", 42);

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

        await select.SelectAsync("add");
        Assert.False(select.IsCompleted);
        Assert.Equal("finish", Assert.Single(select.Actions).Id);

        await select.SelectAsync("finish");
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
        var initialCount = select.GetProperty("count");
        Assert.Equal(0L, initialCount.GetValue<long>());
        Assert.Equal(
            "quiet",
            initialCount.Metadata?.GetValue<Dictionary<string, object?>>()?["emphasis"]);
        Assert.Equal("Count: 0", select.GetProperty("status-text").GetValue<string>());
        Assert.Equal(
            "Start",
            Action(select, "add").Metadata?.GetValue<Dictionary<string, object?>>()?["text"]);

        await select.SelectAsync("add");

        Assert.Equal(1L, select.GetProperty("count").GetValue<long>());
        Assert.Equal(
            "strong",
            select.GetProperty("count").Metadata?.GetValue<Dictionary<string, object?>>()?["emphasis"]);
        Assert.Equal("Count: 1", select.GetProperty("status-text").GetValue<string>());
        Assert.Equal(
            "Add again",
            Action(select, "add").Metadata?.GetValue<Dictionary<string, object?>>()?["text"]);
        Assert.Equal(0L, initialCount.GetValue<long>());

        await select.SelectAsync("add");
        await select.SelectAsync("finish");

        Assert.Equal(2L, select.GetValue<long>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("Count")]
    public async Task GetPropertyRejectsNullOrUnpublishedNames(string? name)
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                prop count => 0
                action "finish" => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        var error = Assert.ThrowsAny<ArgumentException>(() => select.GetProperty(name!));
        Assert.Equal("name", error.ParamName);
    }

    [Fact]
    public async Task GetPropertyReadsTheActiveNestedSelect()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select child {
                prop label => "child"
                action "close" => return
            }
            select root {
                prop label => "root"
                action "open" => goto child
                action "finish" => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("root");
        var rootLabel = select.GetProperty("label");

        await select.SelectAsync("open");
        Assert.Equal("child", select.GetProperty("label").GetValue<string>());
        Assert.Equal("root", rootLabel.GetValue<string>());

        await select.SelectAsync("close");
        Assert.Equal("root", select.GetProperty("label").GetValue<string>());
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
        var rootToken = select.Token;
        Assert.Equal("Root", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);

        await select.SelectAsync("open", expectedToken: rootToken);

        Assert.Equal("child", select.Name);
        Assert.Equal("Child", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);
        await Assert.ThrowsAsync<ArgumentException>(
            () => select.SelectAsync("return", expectedToken: rootToken));
        await select.SelectAsync("return");

        Assert.Equal("root", select.Name);
        Assert.Equal("Root", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);
        Assert.Equal("finish", Assert.Single(select.Actions).Id);
        Assert.NotEqual(rootToken, select.Token);
        await Assert.ThrowsAsync<ArgumentException>(
            () => select.SelectAsync("finish", expectedToken: rootToken));
        await select.SelectAsync("finish", expectedToken: select.Token);

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
        await select.SelectAsync("down");
        Assert.Equal("middle", select.Name);
        await select.SelectAsync("down");
        Assert.Equal("leaf", select.Name);
        await select.SelectAsync("up");
        Assert.Equal("middle", select.Name);
        await select.SelectAsync("up");
        Assert.Equal("root", select.Name);
        await select.SelectAsync("finish");
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
        await select.SelectAsync("open");
        await select.SelectAsync("finish");

        Assert.True(select.IsCompleted);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task HiddenActionsCanNavigateAndReturn()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select child {
                hidden action "close" => return
            }

            select root {
                hidden action "open" => goto child
                hidden action "finish" => exit "done"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var select = await instance.OpenSelectAsync("root");
        await select.SelectAsync("open");
        Assert.Equal("child", select.Name);
        await select.SelectAsync("close");
        Assert.Equal("root", select.Name);
        await select.SelectAsync("finish");

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
        var token = select.Token;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => select.SelectAsync(published.Id, expectedToken: token));

        Assert.False(select.IsCompleted);
        Assert.Equal("root", select.Name);
        Assert.Same(published, Assert.Single(select.Actions));
        Assert.Equal(token, select.Token);
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
        await select.SelectAsync("open");

        Assert.Equal("<anonymous>", select.Name);
        Assert.Equal("Generated", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);
        await select.SelectAsync("up");
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
        await select.SelectAsync("open");

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
        await select.SelectAsync("return");

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
        await select.SelectAsync("finish");

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
    public async Task PublicationTokensRejectInputAfterAnAction()
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
        var token = select.Token;

        await select.SelectAsync("advance");

        await Assert.ThrowsAsync<ArgumentException>(
            () => select.SelectAsync("finish", expectedToken: token));

        await select.SelectAsync("finish");
        Assert.True(select.IsCompleted);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task PublicSelectExposesDescriptionAndKeepsTheLastPublicationOnHiddenActionFailure()
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

                hidden action "changed" => { }
                action "old" when catalog.Available() => exit "old"
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var select = await instance.OpenSelectAsync("flow");
        Assert.Equal("Description 1", select.Description?
            .GetValue<Dictionary<string, object?>>()?["title"]);
        Assert.Equal(1, descriptionCalls);
        var oldAction = Action(select, "old");
        var token = select.Token;
        Assert.Equal("old", oldAction.Id);

        failAvailability = true;
        await Assert.ThrowsAnyAsync<Exception>(() => select.SelectAsync("changed"));

        Assert.Equal(1, descriptionCalls);
        Assert.Same(oldAction, Action(select, "old"));
        Assert.Equal(token, select.Token);
        failAvailability = false;
        await select.SelectAsync(oldAction.Id, expectedToken: token);
        Assert.True(select.IsCompleted);
        Assert.Equal("old", select.GetValue<string>());
    }

    [Fact]
    public async Task SelectKeepsItsFinalPublishedStateAndRejectsStaleTokens()
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
        var token = select.Token;

        await select.SelectAsync("continue");
        await Assert.ThrowsAsync<ArgumentException>(
            () => select.SelectAsync("finish", expectedToken: token));

        await select.SelectAsync("finish");
        Assert.True(select.IsCompleted);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task InteractiveSelectExecutesHiddenActions()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialization = await instance.ExecuteAsync("""
            select flow {
                hidden action "complete" (value) => exit value
            }
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        using var select = await instance.OpenSelectAsync("flow");
        Assert.True(Assert.Single(select.Actions).IsHidden);

        await select.SelectAsync("complete", "done");

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

        var selection = select.SelectAsync("finish");
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
        var selection = select.SelectAsync("finish");
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

        var token = select.Token;
        await select.SelectAsync("ask", expectedToken: token);

        Assert.Empty(select.Actions);
        Assert.NotEqual(token, select.Token);
        var request = Assert.IsType<MinamoSelectRequest>(select.Request);
        Assert.Equal("text", request.Kind);
        Assert.Equal("Name", request.GetPayload<Dictionary<string, object?>>()?["prompt"]);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => select.SelectAsync("ask", expectedToken: token));

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

        await select.SelectAsync("sum");
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
        await select.SelectAsync("ask");
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
    public async Task HiddenActionsCanRequestHostInput()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                hidden action "ask" => exit request("text")
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        var token = select.Token;
        await select.SelectAsync("ask", expectedToken: token);
        var request = Assert.IsType<MinamoSelectRequest>(select.Request);
        Assert.Equal("text", request.Kind);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => select.SelectAsync("ask", expectedToken: token));

        await select.RespondAsync(request, "done");
        Assert.True(select.IsCompleted);
        Assert.Equal("done", select.GetValue<string>());
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
        await session.SelectAsync("finish");
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
        await Assert.ThrowsAsync<ArgumentException>(
            () => session.SelectAsync("hidden", expectedToken: session.Token));

        await session.SelectAsync("set-volume", 80);
        Assert.False(session.IsCompleted);
        Assert.Equal(80, volume);

        await session.SelectAsync("move", (12, 34));
        Assert.Equal((12L, 34L), position);

        await session.SelectAsync("play");
        Assert.Single(session.Actions);
        Assert.Equal("pause", session.Actions[0].Id);

        await session.SelectAsync("pause");
        Assert.Single(session.Actions);
        Assert.Equal("exit", session.Actions[0].Id);

        await session.SelectAsync("exit");
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

        await session.SelectAsync("next");
        var next = session.Snapshot;
        var nextDescription = next.Description?
            .GetValue<Dictionary<string, object?>>()
            ?? throw new InvalidOperationException("The select description is unavailable.");
        Assert.Same(initial.Description, next.Description);
        Assert.Equal("Flow", nextDescription["title"]);

        await session.SelectAsync("exit");

        Assert.True(session.IsCompleted);
        Assert.Equal("Done", session.GetValue<string>());
    }

    [Fact]
    public async Task HiddenActionReevaluatesTheSnapshotAndRejectsPreviouslyPublishedTokens()
    {
        var available = true;
        using var instance = new MinamoHost()
            .Module("inventory", module => module.Command("IsAvailable", _ => available))
            .CreateInstance();
        var initialized = instance.Execute("""
            import inventory

            select flow {
                hidden action "inventory-changed" => { }
                action "finish"
                    when inventory.IsAvailable()
                    => exit "done"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("flow");
        var initial = session.Snapshot;
        available = false;
        await session.SelectAsync("inventory-changed");

        Assert.True(Assert.Single(session.Actions).IsHidden);
        await Assert.ThrowsAsync<ArgumentException>(
            () => session.SelectAsync("finish", expectedToken: initial.Token));
        available = true;
        await session.SelectAsync("inventory-changed");
        Assert.Equal(2, session.Actions.Count);

        await session.SelectAsync("finish");

        Assert.True(session.IsCompleted);
        Assert.Equal("done", session.GetValue<string>());
    }

    [Fact]
    public async Task HiddenActionPublishesANewTokenForAllActions()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                hidden action "republish" => { }
                action "finish" => exit
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = await instance.OpenSelectSessionAsync("flow");
        var initial = Action(session, "republish");
        var token = session.Token;
        await session.SelectAsync("republish");
        var refreshed = Action(session, "republish");

        Assert.NotSame(initial, refreshed);
        Assert.NotEqual(token, session.Token);
        await Assert.ThrowsAsync<ArgumentException>(
            () => session.SelectAsync("republish", expectedToken: token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => session.SelectAsync("finish", expectedToken: token));

        await session.SelectAsync("finish");
        Assert.True(session.IsCompleted);
    }

    [Fact]
    public async Task AsyncHiddenActionsUseTheCurrentSelectSession()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                hidden action "advance" => { }
                hidden action "finish" (value) => exit value
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = await instance.OpenSelectSessionAsync("flow");
        await session.SelectAsync("advance");
        Assert.False(session.IsCompleted);

        await session.SelectAsync("finish", 42);

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
        await first.SelectAsync("learn");

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

        await first.SelectAsync("play");

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
        await first.SelectAsync("ready");
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

        await select.SelectAsync("leave");

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
        await select.SelectAsync("exit");
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
        await select.SelectAsync("ready");
        Assert.Equal("exit", Assert.Single(select.Actions).Id);
        await select.SelectAsync("exit");
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
        await select.SelectAsync("disable");

        Assert.True(select.IsCompleted);
        Assert.Null(select.GetValue<object?>());
    }

    [Fact]
    public async Task HiddenActionsKeepASelectWithoutVisibleActionsActive()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                hidden action "completed" => exit "event"
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.False(select.IsCompleted);
        Assert.True(Assert.Single(select.Actions).IsHidden);
        await select.SelectAsync("completed");
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
        await select.SelectAsync("begin");
        var add = Assert.Single(select.Actions);
        Assert.Equal(1, add.ParameterCount);
        Assert.Equal("delta", Assert.Single(add.Parameters).Name);

        await select.SelectAsync("add", 10);

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
        await select.SelectAsync("leave");
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

        await session.SelectAsync("begin");
        await session.SelectAsync("finish");

        Assert.True(session.IsCompleted);
        Assert.Equal(7L, session.GetValue<long>());
    }

    [Fact]
    public async Task HiddenActionsArePublishedAndCanCarryArguments()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let flow = select {
                hidden action "completed" (value) => exit value
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectSessionAsync("flow");

        Assert.False(select.IsCompleted);
        Assert.True(Assert.Single(select.Actions).IsHidden);

        await select.SelectAsync("completed", 42);

        Assert.True(select.IsCompleted);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task OpenSelectSessionAcceptsHiddenActions()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = instance.Execute("""
            select flow {
                hidden action "completed" (value) => exit value
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var session = instance.OpenSelectSession("flow");

        Assert.True(Assert.Single(session.Actions).IsHidden);
        await session.SelectAsync("completed", 42);
        Assert.True(session.IsCompleted);
        Assert.Equal(42L, session.GetValue<long>());
    }

    [Theory]
    [InlineData("hidden action")]
    [InlineData("action")]
    public void DuplicateHiddenActionsAreRejected(string secondDeclaration)
    {
        var compiled = new MinamoHost().Compile($$"""
            select flow {
                hidden action "tick" => { }
                {{secondDeclaration}} "tick" => { }
            }
            """);

        Assert.False(compiled.Success);
        Assert.Contains(compiled.Errors, error =>
            error.Code == (int)Minamo.Compiler.CompilerError.SelectDuplicateAction);
    }

    [Fact]
    public async Task HiddenActionsUseGuardsMetadataAndTypedParameters()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            let hidden = 1
            let on = 2
            select flow {
                mut ready = false
                action "begin" when !ready => { ready = true }
                hidden action "finish" (value: Integer) when ready ["kind": "completion"] => exit value
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        Assert.False(Assert.Single(select.Actions).IsHidden);
        await select.SelectAsync("begin");

        var finish = Assert.Single(select.Actions);
        Assert.True(finish.IsHidden);
        Assert.Equal("Integer", Assert.Single(finish.Parameters).TypeName);
        Assert.Equal("completion", finish.Metadata?.GetValue<Dictionary<string, object?>>()?["kind"]);
        await select.SelectAsync(finish.Id, 42, expectedToken: select.Token);
        Assert.Equal(42L, select.GetValue<long>());
    }

    [Fact]
    public async Task SelectCompletesWhenAllHiddenActionsAreUnavailable()
    {
        using var instance = new MinamoHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select flow {
                mut active = true
                hidden action "stop" when active => { active = false }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("flow");

        Assert.False(select.IsCompleted);
        await select.SelectAsync("stop");
        Assert.True(select.IsCompleted);
        Assert.Empty(select.Actions);
        Assert.Null(select.GetValue<object?>());
    }

    [Theory]
    [InlineData("on \"completed\" => exit")]
    [InlineData("hidden \"completed\" => exit")]
    public void InvalidOrRemovedSelectMemberSyntaxIsRejected(string member)
    {
        var compiled = new MinamoHost().Compile("select flow { " + member + " }");
        Assert.False(compiled.Success);
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
