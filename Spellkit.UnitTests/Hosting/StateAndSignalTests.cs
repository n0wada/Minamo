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
}
