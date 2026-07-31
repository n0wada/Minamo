using Spellkit.Hosting;
using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class StateAndSignalTests
{
    [Fact]
    public void SharesState() =>
        HostingScenarios.SharedState();

    [Fact]
    public void DeliversAndCleansUpSignals() =>
        HostingScenarios.Signals();

    [Fact]
    public void BoundsThePendingQueueAndSupportsTryEmit()
    {
        using var instance = new SpellkitHost(new()
            {
                Signals = new() { MaxPending = 2 }
            })
            .AddSignal("tick")
            .CreateInstance();
        var signals = instance.Environment.Signals;

        Assert.Equal(2, signals.MaxPending);
        Assert.True(signals.TryEmit("tick", 1));
        Assert.True(signals.TryEmit("tick", 2));
        Assert.False(signals.TryEmit("tick", 3));
        Assert.Equal(2, signals.PendingCount);
        Assert.Throws<InvalidOperationException>(() => signals.Emit("tick", 4));

        var dispatch = instance.DispatchSignals();

        Assert.True(dispatch.Success);
        Assert.Equal(2, dispatch.Delivered);
        Assert.Equal(0, signals.PendingCount);
        Assert.True(signals.TryEmit("tick", 5));
    }

    [Fact]
    public void ExposesTryEmitToScripts()
    {
        using var instance = new SpellkitHost(new()
            {
                Signals = new() { MaxPending = 1 }
            })
            .AddSignal("tick")
            .CreateInstance();

        var result = instance.Execute("""
            assert(true, host.Signals.TryEmit("tick", 1))
            assert(false, host.Signals.TryEmit("tick", 2))
            """);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.False(instance.Execute("host.Signals.Emit(\"tick\", 3)").Success);
        Assert.Equal(1, instance.Environment.Signals.PendingCount);
    }

    [Fact]
    public void ValidatesThePendingSignalLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpellkitHost(new()
        {
            Signals = new() { MaxPending = 0 }
        }));
        Assert.Throws<ArgumentNullException>(() => new SpellkitHost(new()
        {
            Signals = null!
        }));
    }

    [Fact]
    public async Task DispatchesScriptSignalHandlersAsynchronously()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var instance = new SpellkitHost()
            .AddCapabilities("state.*")
            .AddSignal("tick")
            .Module("work", module => module.AsyncCommand(
                "Wait",
                async _ =>
                {
                    entered.SetResult();
                    await completion.Task.ConfigureAwait(false);
                }))
            .CreateInstance();

        var initialization = instance.Execute("""
            import work

            func receive(value) {
                work.Wait()
                host.State["last"] = value
            }

            host.Signals.On("tick", receive)
            """);
        Assert.True(initialization.Success, initialization.Failure?.Message);

        instance.Environment.Signals.Emit("tick", 42);
        var dispatch = instance.DispatchSignalsAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(dispatch.IsCompleted);

        completion.SetResult();
        var result = await dispatch;

        Assert.True(result.Success);
        Assert.Equal(42L, instance.Environment.State.Get<long>("last"));
    }
}
