using Spellkit.Hosting;
using Xunit;

namespace Spellkit.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class LifecycleAndConcurrencyTests
{
    [Fact]
    public void RejectsProgramsCompiledByAnotherHost()
    {
        var firstHost = new SpellkitHost();
        var program = firstHost.Compile("40 + 2").GetValueOrThrow();
        var secondHost = new SpellkitHost();

        Assert.Throws<InvalidOperationException>(() => secondHost.CreateInstance(program));
    }

    [Fact]
    public void InvalidatesCapturedCallbackWhenCommandFails()
    {
        Func<long, long>? captured = null;
        using var instance = new SpellkitHost()
            .Module("callbacks", module => module.Command(
                "CaptureAndFail",
                context =>
                {
                    captured = context.Callback<long, long>("callback");
                    throw new InvalidOperationException("failure after capture");
                },
                SpellkitCommandParameter.Required<object>("callback")))
            .CreateInstance();

        var result = instance.Execute("import callbacks\ncallbacks.CaptureAndFail(value => value + 1)");

        Assert.False(result.Success);
        Assert.NotNull(captured);
        Assert.Throws<InvalidOperationException>(() => captured!(1));
    }

    [Fact]
    public void RejectsOperationsAfterDisposal()
    {
        var instance = new SpellkitHost()
            .AddSignal("tick")
            .CreateInstance();
        var state = instance.Environment.State;
        var signals = instance.Environment.Signals;

        instance.Dispose();
        instance.Dispose();

        Assert.Throws<ObjectDisposedException>(() => instance.Execute("1"));
        Assert.Throws<ObjectDisposedException>(() => instance.DispatchSignals());
        Assert.Throws<ObjectDisposedException>(() => state.Contains("value"));
        Assert.Throws<ObjectDisposedException>(() => signals.Emit("tick"));
    }

    [Fact]
    public void ResetsProgramBackedInstanceWithoutLosingProgram()
    {
        var host = new SpellkitHost().AddCapabilities("state.*");
        var program = host.Compile("""
            let current = if host.State["runs"] is nil { 0 } else { host.State["runs"] }
            host.State["runs"] = current + 1
            host.State["runs"]
            """).GetValueOrThrow();
        using var instance = host.CreateInstance(program);

        Assert.Equal(1L, instance.Execute().GetValue<long>());
        Assert.Equal(2L, instance.Execute().GetValue<long>());

        instance.Reset();

        Assert.Equal(1L, instance.Execute().GetValue<long>());
    }

    [Fact]
    public async Task SerializesOperationsOnTheSameInstance()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var instance = CreateBlockingHost().CreateInstance(new Gate(entered, release));

        var first = instance.ExecuteAsync("import work\nwork.Wait()");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        var second = instance.ExecuteAsync("40 + 2");
        Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(100)));

        release.Set();

        Assert.True((await first).Success);
        Assert.Equal(42L, (await second).GetValue<long>());
    }

    [Fact]
    public async Task AllowsDifferentInstancesToExecuteConcurrently()
    {
        using var firstEntered = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var host = CreateBlockingHost();
        using var firstInstance = host.CreateInstance(new Gate(firstEntered, release));
        using var secondInstance = host.CreateInstance(new Gate(secondEntered, release));

        var first = firstInstance.ExecuteAsync("import work\nwork.Wait()");
        var second = secondInstance.ExecuteAsync("import work\nwork.Wait()");

        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(secondEntered.Wait(TimeSpan.FromSeconds(5)));

        release.Set();

        Assert.True((await first).Success);
        Assert.True((await second).Success);
    }

    [Fact]
    public async Task AwaitsHostTasksAndKeepsSameInstanceOperationsSerialized()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var instance = new SpellkitHost()
            .Module("work", module => module.AsyncCommand(
                "Wait",
                async _ =>
                {
                    entered.SetResult();
                    return await completion.Task.ConfigureAwait(false);
                }))
            .CreateInstance();

        var first = instance.ExecuteAsync("import work\nwork.Wait() + 1");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = instance.ExecuteAsync("40 + 2");
        Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(100)));

        completion.SetResult(41);

        Assert.Equal(42L, (await first).GetValue<long>());
        Assert.Equal(42L, (await second).GetValue<long>());
    }

    [Fact]
    public async Task PreservesAsyncHostFailuresAtTheVmCallSite()
    {
        using var instance = new SpellkitHost()
            .Module("work", module => module.AsyncCommand(
                "Fail",
                async _ =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("sensitive detail");
                }))
            .CreateInstance();

        var result = await instance.ExecuteAsync("import work\nwork.Fail()");

        Assert.False(result.Success);
        Assert.Equal(SpellkitFailureKind.Runtime, result.Failure?.Kind);
        Assert.DoesNotContain("sensitive detail", result.Failure?.Message);
        Assert.Contains("host command failed", result.Failure?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KeepsCallbacksValidUntilAnAsyncHostCommandCompletes()
    {
        using var instance = new SpellkitHost()
            .Module("work", module => module.AsyncCommand(
                "Apply",
                async context =>
                {
                    var callback = context.Callback<long, long>("callback");
                    var value = context.Argument<long>("value");
                    await Task.Yield();
                    return callback(value);
                },
                SpellkitCommandParameter.Required<long>("value"),
                SpellkitCommandParameter.Required<object>("callback")))
            .CreateInstance();

        var result = await instance.ExecuteAsync(
            "import work\nwork.Apply(41, value => value + 1)");

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public async Task ExecutesCompiledProgramsAsynchronously()
    {
        var host = new SpellkitHost();
        var program = host.Compile("40 + 2").GetValueOrThrow();
        using var instance = host.CreateInstance(program);

        var result = await instance.ExecuteAsync();

        Assert.True(result.Success);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public void DoesNotLeakCancellationIntoTheNextExecution()
    {
        using var instance = new SpellkitHost().CreateInstance();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = instance.Execute("while true {}", cancellation.Token);
        var next = instance.Execute("40 + 2");

        Assert.False(cancelled.Success);
        Assert.Equal(SpellkitFailureKind.Cancelled, cancelled.Failure?.Kind);
        Assert.True(next.Success);
        Assert.Equal(42L, next.GetValue<long>());
    }

    private static SpellkitHost CreateBlockingHost() =>
        new SpellkitHost().Module("work", module => module.Command(
            "Wait",
            context =>
            {
                context.Host<Gate>().Wait();
                return null;
            }));

    private sealed class Gate(ManualResetEventSlim entered, ManualResetEventSlim release)
    {
        internal void Wait()
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        }
    }
}
