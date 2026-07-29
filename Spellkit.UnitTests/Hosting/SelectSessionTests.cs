using Spellkit.Hosting;
using System.Text;
using Xunit;

namespace Spellkit.UnitTesting;

public sealed class SelectSessionTests
{
    [Fact]
    public void SelectRequiresExactlyOneInitialStateAndUniqueChoices()
    {
        var host = new SpellkitHost();
        var missingInitial = host.Compile("""
            select player {
                state "stopped" {
                    choose "play" => { }
                }
            }
            """);
        Assert.False(missingInitial.Success);
        Assert.Contains(missingInitial.Errors, error =>
            error.Code == (int)Spellkit.Compiler.CompilerError.SelectRequiresOneInitialState);

        var duplicateChoice = host.Compile("""
            select player {
                initial state "stopped" {
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
                initial state "stopped" {
                    choose "play" => goto "missing"
                }
            }
            """);
        Assert.False(unknownState.Success);
        Assert.Contains(unknownState.Errors, error =>
            error.Code == (int)Spellkit.Compiler.CompilerError.SelectStateNotFound);
    }

    [Fact]
    public void SelectSessionTransitionsAndAcceptsTupleArguments()
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
                initial state "stopped" {
                    choose "play" => goto "playing"

                    choose "set-volume" (value) => {
                        music.SetVolume(value)
                    }

                    choose "move" (x, y) => {
                        music.Move(x, y)
                    }

                    choose "status" label "Show status" description "Display player status" when true => {
                    }

                    choose "hidden" label "Hidden command" when false => {
                    }
                }

                state "playing" {
                    choose "pause" => goto "paused"
                }

            state "paused" {
                    choose "exit" => exit "done"
                }
            }

            alias(player, "music.player")
            """).GetValueOrThrow();

        using var instance = host.CreateInstance(program);
        using var session = instance.OpenSelect("music.player");

        Assert.Equal("stopped", session.State);
        Assert.Collection(session.Choices,
            choice => Assert.Equal("play", choice.Id),
            choice => Assert.Equal("set-volume", choice.Id),
            choice => Assert.Equal("move", choice.Id),
            choice =>
            {
                Assert.Equal("status", choice.Id);
                Assert.Equal("Show status", choice.Label);
                Assert.Equal("Display player status", choice.Description);
            });
        Assert.Throws<ArgumentException>(() => session.Select("hidden"));

        var afterVolume = session.Select("set-volume", 80);
        Assert.False(afterVolume.IsCompleted);
        Assert.Equal(80, volume);

        session.Select("move", (12, 34));
        Assert.Equal((12L, 34L), position);

        var afterPlay = session.Select("play");
        Assert.Equal("playing", session.State);
        Assert.Single(afterPlay.Choices);
        Assert.Equal("pause", afterPlay.Choices[0].Id);

        var afterPause = session.Select("pause");
        Assert.Equal("paused", session.State);
        Assert.Single(afterPause.Choices);
        Assert.Equal("exit", afterPause.Choices[0].Id);

        var completed = session.Select("exit");
        Assert.True(completed.IsCompleted);
        Assert.Equal("done", completed.GetValue<string>());
    }

    [Fact]
    public void ScriptDoRunsTheSelectAndThenContinuesExecution()
    {
        var output = new StringBuilder();
        var invoked = 0;
        using var instance = new SpellkitHost().CreateInstance(
            new SpellkitEnvironment()
                .UseOutput(value => output.Append(value))
                .UseSelect(select =>
                {
                    invoked++;
                    Assert.Equal("player", select.Name);
                    select.Select("exit");
                }));

        var result = instance.Execute("""
            select player {
                initial state "stopped" {
                    choose "exit" => exit
                }
            }

            alias(player, "music.player")
            do music.player
            print("after", terminator: nil)
            """);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(1, invoked);
        Assert.Equal("after", output.ToString());
    }

    [Fact]
    public void StartSuspendsAtDoAndResumesAfterExit()
    {
        var output = new StringBuilder();
        using var instance = new SpellkitHost().CreateInstance(
            new SpellkitEnvironment().UseOutput(value => output.Append(value)));

        using var run = instance.Start("""
            select player {
                initial state "stopped" {
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

        var result = run.Select("exit");

        Assert.True(result.IsCompleted);
        Assert.True(run.IsCompleted);
        Assert.Equal("42", output.ToString());
    }

    [Fact]
    public void NestedDoResumesTheOuterChoiceAfterInnerExit()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            select shop {
                initial state "open" {
                    choose "leave" => exit
                }
            }

            select town {
                initial state "square" {
                    choose "shop" => {
                        do shop
                        goto "square"
                    }

                    choose "exit" => exit
                }
            }

            do town
            """);

        Assert.Equal(new[] { "shop", "exit" }, run.Choices.Select(choice => choice.Id));

        var inShop = run.Select("shop");
        Assert.Equal(new[] { "leave" }, inShop.Choices.Select(choice => choice.Id));

        var backInTown = run.Select("leave");
        Assert.Equal(new[] { "shop", "exit" }, backInTown.Choices.Select(choice => choice.Id));

        Assert.True(run.Select("exit").IsCompleted);
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
                    initial state "stopped" {
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
    public void AliasCanExposeAFunctionProducedSelectFactory()
    {
        using var instance = new SpellkitHost().CreateInstance();
        var initialized = instance.Execute("""
            func questGame() {
                mut known = false

                select {
                    initial state "square" {
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

        using var first = instance.OpenSelect("quest.town");
        Assert.Equal("learn", Assert.Single(first.Choices).Id);
        first.Select("learn");

        using var second = instance.OpenSelect("quest.town");
        Assert.Equal("continue", Assert.Single(second.Choices).Id);
    }

    [Fact]
    public void NamedSelectFactoryCreatesIndependentSessions()
    {
        using var instance = new SpellkitHost().CreateInstance();
        var initialized = instance.Execute("""
            select player {
                initial state "stopped" {
                    choose "play" => goto "playing"
                }

                state "playing" {
                    choose "exit" => exit
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var first = instance.OpenSelect("player");
        using var second = instance.OpenSelect("player");

        first.Select("play");

        Assert.Equal("exit", first.Choices.Single().Id);
        Assert.Equal("play", second.Choices.Single().Id);
    }

    [Fact]
    public void SelectLocalsAreCreatedForEachSession()
    {
        using var instance = new SpellkitHost().CreateInstance();
        var initialized = instance.Execute("""
            select player {
                mut ready = false

                initial state "stopped" {
                    choose "ready" when !ready => {
                        ready = true
                    }

                    choose "exit" when ready => exit
                }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);

        using var first = instance.OpenSelect("player");
        Assert.Equal("ready", Assert.Single(first.Choices).Id);
        first.Select("ready");
        Assert.Equal("exit", Assert.Single(first.Choices).Id);

        using var second = instance.OpenSelect("player");
        Assert.Equal("ready", Assert.Single(second.Choices).Id);
    }

    [Fact]
    public void DoEvaluatesAnAnonymousSelectFactoryExpression()
    {
        var output = new StringBuilder();
        using var instance = new SpellkitHost().CreateInstance(
            new SpellkitEnvironment().UseOutput(value => output.Append(value)));
        using var run = instance.Start("""
            func createShop(name) {
                select {
                    initial state "open" {
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

        var result = run.Select("leave");

        Assert.True(result.IsCompleted);
        Assert.True(run.IsCompleted);
        Assert.Equal("weapons done", output.ToString());
    }

    [Fact]
    public void DoCanUseAFactoryVariable()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let player = select {
                initial state "stopped" {
                    choose "exit" => exit
                }
            }

            do player
            """);

        Assert.True(run.IsWaitingForSelect, run.Failure?.ToString());
        Assert.True(run.Select("exit").IsCompleted);
    }

    [Fact]
    public void DoCreatesASelectLocalFrame()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let player = select {
                mut ready = false

                initial state "stopped" {
                    choose "ready" when !ready => {
                        ready = true
                    }

                    choose "exit" when ready => exit
                }
            }

            do player
            """);

        Assert.Equal("ready", Assert.Single(run.Choices).Id);
        Assert.Equal("exit", Assert.Single(run.Select("ready").Choices).Id);
        Assert.True(run.Select("exit").IsCompleted);
    }

    [Fact]
    public void SuspendedRunCanEvaluateGuardsWhenChoicesAreRead()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let available = true
            let player = select {
                initial state "stopped" {
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
                initial state "empty" {
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
    public void NestedDoCanUseAnAnonymousFactoryVariable()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var run = instance.Start("""
            let shop = select {
                initial state "open" {
                    choose "leave" => exit
                }
            }

            select town {
                initial state "square" {
                    choose "shop" => {
                        do shop
                        goto "square"
                    }

                    choose "exit" => exit
                }
            }

            do town
            """);

        Assert.Equal(new[] { "shop", "exit" }, run.Choices.Select(choice => choice.Id));
        Assert.Equal("leave", Assert.Single(run.Select("shop").Choices).Id);
        Assert.Equal(new[] { "shop", "exit" }, run.Select("leave").Choices.Select(choice => choice.Id));
        Assert.True(run.Select("exit").IsCompleted);
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
}
