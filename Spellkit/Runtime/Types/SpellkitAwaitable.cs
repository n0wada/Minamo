using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Spellkit.Runtime.Types;

internal sealed class SpellkitAwaitable : SpellkitObject
{
    private readonly Task task;
    private readonly Func<SpellkitObject> getResult;
    private Func<ExecutionContext, SpellkitObject, SpellkitObject>? completeResult;
    private Func<ExecutionContext, Exception, SpellkitObject>? completeFailure;
    private Action? completed;

    internal SpellkitAwaitable(Task task, Func<SpellkitObject> getResult)
        : base(SpellkitTypeCodes.Nil)
    {
        this.task = task;
        this.getResult = getResult;
    }

    public override string TypeName => "Awaitable";

    public override object ToObject() => task;

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => task.GetHashCode();

    internal SpellkitAwaitable Configure(
        Func<ExecutionContext, SpellkitObject, SpellkitObject> result,
        Func<ExecutionContext, Exception, SpellkitObject> failure,
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

    internal SpellkitObject Complete(ExecutionContext context)
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
