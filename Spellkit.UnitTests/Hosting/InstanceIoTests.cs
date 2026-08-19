using Spellkit.Hosting;
using Spellkit.Library;
using System.Text;
using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class InstanceIoTests
{
    [Fact]
    public async Task RoutesInputAndOutputThroughTheInstanceEnvironment()
    {
        var output = new StringBuilder();
        using var instance = new SpellkitHost()
            .AddStandardLibrary()
            .CreateInstance(
            new SpellkitEnvironment()
                .UseInputAsync(_ => ValueTask.FromResult<string?>("instance input"))
                .UseOutput(value => output.Append(value)));

        var result = await instance.ExecuteAsync("import * from readline\nprint(readLine(), terminator: nil)");

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("instance input", output.ToString());
    }

    [Fact]
    public async Task ReadLineAwaitsTheConfiguredAsyncInput()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new StringBuilder();
        using var instance = new SpellkitHost()
            .AddStandardLibrary()
            .CreateInstance(
                new SpellkitEnvironment()
                    .UseInputAsync(async _ =>
                    {
                        entered.SetResult();
                        return await completion.Task.ConfigureAwait(false);
                    })
                    .UseOutput(value => output.Append(value)));

        var execution = instance.ExecuteAsync("import * from readline\nprint(readLine(), terminator: nil)");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(execution.IsCompleted);

        completion.SetResult("async input");
        var result = await execution;

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("async input", output.ToString());
    }

    [Fact]
    public void FallsBackToConsoleOutputWhenOutputIsNotConfigured()
    {
        using var instance = new SpellkitHost().CreateInstance();

        var result = instance.Execute("print(42, terminator: nil)");

        Assert.True(result.Success, result.Failure?.Message);
    }

    [Fact]
    public async Task IsolatesIoAcrossConcurrentInstances()
    {
        using var rendezvous = new Barrier(2);
        var firstOutput = new StringBuilder();
        var secondOutput = new StringBuilder();
        var host = new SpellkitHost()
            .AddStandardLibrary();
        using var first = host.CreateInstance(
            Environment("first", firstOutput, rendezvous));
        using var second = host.CreateInstance(
            Environment("second", secondOutput, rendezvous));

        var firstRun = first.ExecuteAsync("import * from readline\nprint(readLine(), terminator: nil)");
        var secondRun = second.ExecuteAsync("import * from readline\nprint(readLine(), terminator: nil)");
        var results = await Task.WhenAll(firstRun, secondRun);

        Assert.All(results, result => Assert.True(result.Success, result.Failure?.Message));
        Assert.Equal("first", firstOutput.ToString());
        Assert.Equal("second", secondOutput.ToString());
    }

    private static SpellkitEnvironment Environment(
        string input,
        StringBuilder output,
        Barrier rendezvous) =>
        new SpellkitEnvironment()
            .UseInputAsync(_ =>
            {
                Assert.True(rendezvous.SignalAndWait(TimeSpan.FromSeconds(5)));
                return ValueTask.FromResult<string?>(input);
            })
            .UseOutput(value => output.Append(value));
}
