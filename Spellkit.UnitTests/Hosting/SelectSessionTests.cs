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
        Assert.Throws<ArgumentException>(() => session.Choose("hidden"));

        var afterVolume = session.Choose("set-volume", 80);
        Assert.False(afterVolume.IsCompleted);
        Assert.Equal(80, volume);

        session.Choose("move", (12, 34));
        Assert.Equal((12L, 34L), position);

        var afterPlay = session.Choose("play");
        Assert.Single(afterPlay.Choices);
        Assert.Equal("pause", afterPlay.Choices[0].Id);

        var afterPause = session.Choose("pause");
        Assert.Single(afterPause.Choices);
        Assert.Equal("exit", afterPause.Choices[0].Id);

        var completed = session.Choose("exit");
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
                    select.Choose("exit");
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

        var result = run.Choose("exit");

        Assert.True(result.IsCompleted);
        Assert.True(run.IsCompleted);
        Assert.Equal("42", output.ToString());
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
}
