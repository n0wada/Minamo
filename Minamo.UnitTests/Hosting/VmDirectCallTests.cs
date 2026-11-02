using Minamo.Hosting;
using Xunit;

namespace Minamo.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class VmDirectCallTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallsZeroOneAndTwoArgumentBuiltins(bool cached)
    {
        using var instance = new MinamoHost().CreateInstance();
        var source = cached ? """
            let xs = [1, 2]
            let zero = xs.Length
            let one = xs.Contains
            let two = xs.Swap
            assert(2, zero())
            assert(true, one(1))
            two(0, 1)
            assert([2, 1], xs)
            """ : """
            let xs = [1, 2]
            assert(2, xs.Length())
            assert(true, xs.Contains(1))
            xs.Swap(0, 1)
            assert([2, 1], xs)
            """;
        var result = instance.Execute(source);
        Assert.True(result.Success, result.Failure?.Message);
    }

    [Fact]
    public void KeepsAnOuterNamedArgumentFrameAcrossInnerDirectCalls()
    {
        using var instance = new MinamoHost().CreateInstance();
        var result = instance.Execute("""
            let xs = [1, 2]
            func collect(first, second, third) => [first, second, third]
            let result = collect(first: xs.Length(), second: xs.Contains(2), third: xs.Swap(0, 1))
            assert([2, true, nil], result)
            assert([2, 1], xs)
            assert([2, 1], xs.Slice(index: 0, size: xs.Length() - 1).ToArray())
            assert([3, 4], Array(3, 4))
            assert("2", fmt("{0}", xs.Length()))
            """);
        Assert.True(result.Success, result.Failure?.Message);
    }

    [Fact]
    public void KeepsArgumentEvaluationOrderAndNestedCallbacks()
    {
        var order = new List<long>();
        using var instance = new MinamoHost()
            .Module("probe", module => module.Command("Mark", context =>
            {
                var value = context.Argument<long>("value");
                order.Add(value);
                return value;
            }, MinamoCommandParameter.Required<long>("value")))
            .CreateInstance();
        var result = instance.Execute("""
            import probe
            let xs = [1, 2]
            xs.Swap(probe.Mark(0), probe.Mark(1))
            assert([2, 1], xs)
            """);
        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(new long[] { 0, 1 }, order);
    }

    [Fact]
    public void ConvertsExceptionsAndContinuesThroughCatchAndFinally()
    {
        using var instance = new MinamoHost().CreateInstance();
        var result = instance.Execute("""
            let xs = [1, 2]
            mut kind = ""
            mut cleanup = 0
            try {
                xs.Swap(0, 99)
            } catch e {
                kind = constructorName(e)
            } finally {
                cleanup = 1
            }
            assert("IndexOutOfRange", kind)
            assert(1, cleanup)
            assert(true, xs.Contains(1))
            xs.Swap(0, 1)
            assert([2, 1], xs)
            """);
        Assert.True(result.Success, result.Failure?.Message);
    }

    [Fact]
    public async Task SuspendsInsideAnOuterPreparedCallAndResumesIt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        long received = 0;
        using var instance = new MinamoHost()
            .Module("probe", module => module.AsyncCommand("Await", async context =>
            {
                received = context.Argument<long>("value");
                entered.SetResult();
                return await completion.Task.ConfigureAwait(false);
            }, MinamoCommandParameter.Required<long>("value")))
            .CreateInstance();
        var operation = instance.ExecuteAsync("""
            import probe
            func collect(first, second) => first + second
            collect(first: probe.Await(20), second: 2)
            """);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.IsCompleted);
        Assert.Equal(20, received);
        completion.SetResult(40);
        Assert.Equal(42L, (await operation).GetValue<long>());
        Assert.Equal(42L, instance.Execute("40 + 2").GetValue<long>());
    }
}
