using Minamo.Hosting;
using Minamo.Library;
using System.Text;
using Xunit;

namespace Minamo.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class InstanceIoTests
{
    [Fact]
    public async Task RoutesHostInputAndTextOutputThroughTheInstanceEnvironment()
    {
        var output = new StringBuilder();
        using var instance = new MinamoHost()
            .AddStandardLibrary()
            .CreateInstance(
            new MinamoEnvironment()
                .UseInputAsync(_ => ValueTask.FromResult("instance input"))
                .UseOutput(value => output.Append(value)));

        var result = await instance.ExecuteAsync("print(host.Input(), terminator: nil)");

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("instance input", output.ToString());
    }

    [Fact]
    public async Task HostInputAwaitsTheConfiguredAsyncSource()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new StringBuilder();
        using var instance = new MinamoHost()
            .AddStandardLibrary()
            .CreateInstance(
                new MinamoEnvironment()
                    .UseInputAsync(async _ =>
                    {
                        entered.SetResult();
                        return await completion.Task.ConfigureAwait(false);
                    })
                    .UseOutput(value => output.Append(value)));

        var execution = instance.ExecuteAsync("print(host.Input(), terminator: nil)");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(execution.IsCompleted);

        completion.SetResult("async input");
        var result = await execution;

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("async input", output.ToString());
    }

    [Fact]
    public async Task HostInputAcceptsArbitraryValues()
    {
        var input = new Dictionary<string, object?>
        {
            ["kind"] = "order",
            ["id"] = 42
        };
        using var instance = new MinamoHost().CreateInstance(
            new MinamoEnvironment().UseInputAsync(
                _ => ValueTask.FromResult<object?>(input)));

        var result = await instance.ExecuteAsync("""
            let input = host.Input()
            assert("order", input["kind"])
            input["id"]
            """);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public async Task HostInputFailsWhenNoSourceIsConfigured()
    {
        using var instance = new MinamoHost().CreateInstance();

        var result = await instance.ExecuteAsync("host.Input()");

        Assert.False(result.Success);
        Assert.Equal(MinamoFailureKind.Runtime, result.Failure?.Kind);
    }

    [Fact]
    public async Task HostInputReportsAsyncSourceFailuresAsRuntimeFailures()
    {
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var instance = new MinamoHost().CreateInstance(
            new MinamoEnvironment().UseInputAsync(
                _ => new ValueTask<object?>(completion.Task)));

        var execution = instance.ExecuteAsync("host.Input()");
        completion.SetException(new InvalidOperationException("input failed"));
        var result = await execution;

        Assert.False(result.Success);
        Assert.Equal(MinamoFailureKind.Runtime, result.Failure?.Kind);
    }

    [Fact]
    public void FallsBackToConsoleOutputWhenOutputIsNotConfigured()
    {
        using var instance = new MinamoHost().CreateInstance();

        var result = instance.Execute("print(42, terminator: nil)");

        Assert.True(result.Success, result.Failure?.Message);
    }

    [Fact]
    public async Task IsolatesIoAcrossConcurrentInstances()
    {
        using var rendezvous = new Barrier(2);
        var firstOutput = new StringBuilder();
        var secondOutput = new StringBuilder();
        var host = new MinamoHost()
            .AddStandardLibrary();
        using var first = host.CreateInstance(
            Environment("first", firstOutput, rendezvous));
        using var second = host.CreateInstance(
            Environment("second", secondOutput, rendezvous));

        var firstRun = first.ExecuteAsync("print(host.Input(), terminator: nil)");
        var secondRun = second.ExecuteAsync("print(host.Input(), terminator: nil)");
        var results = await Task.WhenAll(firstRun, secondRun);

        Assert.All(results, result => Assert.True(result.Success, result.Failure?.Message));
        Assert.Equal("first", firstOutput.ToString());
        Assert.Equal("second", secondOutput.ToString());
    }

    private static MinamoEnvironment Environment(
        string input,
        StringBuilder output,
        Barrier rendezvous) =>
        new MinamoEnvironment()
            .UseInputAsync(_ =>
            {
                Assert.True(rendezvous.SignalAndWait(TimeSpan.FromSeconds(5)));
                return ValueTask.FromResult(input);
            })
            .UseOutput(value => output.Append(value));
}
