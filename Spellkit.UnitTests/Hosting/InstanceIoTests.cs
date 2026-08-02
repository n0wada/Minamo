using Spellkit.Hosting;
using Spellkit.Library;
using System.Text;
using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class InstanceIoTests
{
    [Fact]
    public void RoutesInputAndOutputThroughTheInstanceEnvironment()
    {
        var output = new StringBuilder();
        using var instance = new SpellkitHost()
            .AddStandardLibrary()
            .CreateInstance(
            new SpellkitEnvironment()
                .UseInput(_ => "instance input")
                .UseOutput(value => output.Append(value)));

        var result = instance.Execute("import * from readline\nprint(readLine(), terminator: nil)");

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("instance input", output.ToString());
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
            .UseInput(_ =>
            {
                Assert.True(rendezvous.SignalAndWait(TimeSpan.FromSeconds(5)));
                return input;
            })
            .UseOutput(value => output.Append(value));
}
