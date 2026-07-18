using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Runtime.CompilerServices;

namespace Spellkit.Hosting;

public sealed class SpellkitCallback
{
    private readonly ExecutionContext context;
    private readonly SpkFunction function;
    private readonly SpellkitCallbackScope scope;

    internal SpellkitCallback(
        ExecutionContext context,
        SpkFunction function,
        SpellkitCallbackScope scope) =>
        (this.context, this.function, this.scope) = (context, function, scope);

    public object? Invoke(params object?[] arguments) =>
        Invoke<object?>(arguments);

    public TResult? Invoke<TResult>(params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        scope.ThrowIfInactive();

        var converted = new SpkObject[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            converted[i] = SpellkitCommandConvert.FromObject(arguments[i]);
        }

        var value = function.Call(context, converted);
        return context.HasErrors
            ? default
            : Runtime.TypeConverter.ConvertTo<TResult>(context, value);
    }

    public TResult? InvokeTuple<TArgs, TResult>(TArgs arguments)
        where TArgs : ITuple =>
        Invoke<TResult>(ExpandTuple(arguments));

    private static object?[] ExpandTuple(ITuple tuple)
    {
        ArgumentNullException.ThrowIfNull(tuple);

        var values = new object?[tuple.Length];
        for (var i = 0; i < tuple.Length; i++)
        {
            values[i] = tuple[i];
        }

        return values;
    }
}

internal sealed class SpellkitCallbackScope : IDisposable
{
    private int active = 1;

    internal void ThrowIfInactive()
    {
        if (System.Threading.Volatile.Read(ref active) == 0)
        {
            throw new InvalidOperationException(
                "A Spellkit callback cannot be invoked after its host command has completed.");
        }
    }

    public void Dispose() => System.Threading.Interlocked.Exchange(ref active, 0);
}
