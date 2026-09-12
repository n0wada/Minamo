using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Minamo.Runtime.Types;

internal class MinamoAwaitable : MinamoObject
{
    private readonly Task task;
    private readonly Func<MinamoObject> getResult;
    private Func<ExecutionContext, MinamoObject, MinamoObject>? completeResult;
    private Func<ExecutionContext, Exception, MinamoObject>? completeFailure;
    private Action? completed;

    internal MinamoAwaitable(Task task, Func<MinamoObject> getResult)
        : base(MinamoTypeCodes.Nil)
    {
        this.task = task;
        this.getResult = getResult;
    }

    public override string TypeName => "Awaitable";

    public override object ToObject() => task;

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => task.GetHashCode();

    internal MinamoAwaitable Configure(
        Func<ExecutionContext, MinamoObject, MinamoObject> result,
        Func<ExecutionContext, Exception, MinamoObject> failure,
        Action completion)
    {
        completeResult = result;
        completeFailure = failure;
        completed = completion;
        return this;
    }

    internal void Wait()
    {
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch
        {
            // The VM observes the exception at the suspended call site.
        }
    }

    internal async ValueTask WaitAsync()
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The VM observes the exception at the suspended call site.
        }
    }

    internal MinamoObject Complete(ExecutionContext context)
    {
        try
        {
            var value = getResult();
            return completeResult?.Invoke(context, value) ?? value;
        }
        catch (Exception exception)
        {
            if (completeFailure is not null)
            {
                return completeFailure(context, exception);
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
        finally
        {
            completed?.Invoke();
        }
    }
}

internal sealed class MinamoSelectRequestAwaitable : MinamoAwaitable
{
    private readonly TaskCompletionSource<MinamoObject> response;

    internal MinamoSelectRequestAwaitable(string kind, MinamoObject payload)
        : this(
            kind,
            payload,
            new TaskCompletionSource<MinamoObject>(
                TaskCreationOptions.RunContinuationsAsynchronously)) { }

    private MinamoSelectRequestAwaitable(
        string kind,
        MinamoObject payload,
        TaskCompletionSource<MinamoObject> response)
        : base(response.Task, () => response.Task.GetAwaiter().GetResult())
    {
        Kind = kind;
        Payload = payload;
        this.response = response;
    }

    internal string Kind { get; }

    internal MinamoObject Payload { get; }

    internal void Respond(MinamoObject value)
    {
        if (!response.TrySetResult(value))
        {
            throw new InvalidOperationException("The select request has already been answered.");
        }
    }
}
