using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CancellationToken = System.Threading.CancellationToken;
using EditorBrowsableAttribute = System.ComponentModel.EditorBrowsableAttribute;
using EditorBrowsableState = System.ComponentModel.EditorBrowsableState;

namespace Spellkit.Hosting;

public sealed class SpellkitCommandContext
{
    internal const string HostContextKey = "Spellkit.Hosting.HostContext";

    private readonly SpellkitCommandDescriptor command;
    private readonly SpellkitObject[] arguments;
    private readonly SpellkitCallbackScope callbackScope;

    internal SpellkitCommandContext(
        ExecutionContext executionContext,
        SpellkitCommandDescriptor command,
        SpellkitObject[] arguments,
        SpellkitCallbackScope callbackScope) =>
        (ExecutionContext, this.command, this.arguments, this.callbackScope) =
        (executionContext, command, arguments, callbackScope);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public ExecutionContext ExecutionContext { get; }

    public int ArgumentCount => arguments.Length;

    public string CommandName => command.Name;

    public Guid ExecutionId => Environment.Telemetry.ExecutionId;

    public CancellationToken CancellationToken =>
        ExecutionContext.Control?.CancellationToken ?? System.Threading.CancellationToken.None;

    public SpellkitHostEnvironment Environment =>
        ExecutionContext.GetContextVariable<SpellkitHostEnvironment>(SpellkitHostEnvironment.ContextKey)
        ?? throw new InvalidOperationException("The command is not running in a hosted instance.");

    public T Host<T>() where T : class
    {
        var host = ExecutionContext.GetContextVariable<object>(HostContextKey);
        return host as T ?? throw new InvalidOperationException(
            $"The instance host context is not assignable to {typeof(T).FullName}.");
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public SpellkitObject RawArgument(int index) => arguments[index];

    public SpellkitCallback Callback(int index)
    {
        if ((uint)index >= (uint)arguments.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (arguments[index] is SpellkitFunction function)
        {
            return new SpellkitCallback(ExecutionContext, function, callbackScope);
        }

        ExecutionContext.InvalidCast(arguments[index].TypeName, nameof(SpellkitTypeCodes.Function));
        return null!;
    }

    public SpellkitCallback Callback(string name)
    {
        for (var i = 0; i < command.Parameters.Count; i++)
        {
            if (string.Equals(command.Parameters[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return Callback(i);
            }
        }

        throw new ArgumentException($"Command '{command.Name}' has no parameter named '{name}'.", nameof(name));
    }

    public Func<TResult?> Callback<TResult>(int index)
    {
        var callback = Callback(index);
        return () => callback.Invoke<TResult>();
    }

    public Func<TResult?> Callback<TResult>(string name)
    {
        var callback = Callback(name);
        return () => callback.Invoke<TResult>();
    }

    public Func<T, TResult?> Callback<T, TResult>(int index)
    {
        var callback = Callback(index);
        return value => callback.Invoke<TResult>(value);
    }

    public Func<T, TResult?> Callback<T, TResult>(string name)
    {
        var callback = Callback(name);
        return value => callback.Invoke<TResult>(value);
    }

    public Func<T1, T2, TResult?> Callback<T1, T2, TResult>(int index)
    {
        var callback = Callback(index);
        return (first, second) => callback.Invoke<TResult>(first, second);
    }

    public Func<T1, T2, TResult?> Callback<T1, T2, TResult>(string name)
    {
        var callback = Callback(name);
        return (first, second) => callback.Invoke<TResult>(first, second);
    }

    public Func<TArgs, TResult?> CallbackTuple<TArgs, TResult>(int index)
        where TArgs : ITuple
    {
        var callback = Callback(index);
        return arguments => callback.InvokeTuple<TArgs, TResult>(arguments);
    }

    public Func<TArgs, TResult?> CallbackTuple<TArgs, TResult>(string name)
        where TArgs : ITuple
    {
        var callback = Callback(name);
        return arguments => callback.InvokeTuple<TArgs, TResult>(arguments);
    }

    public Action CallbackAction(int index)
    {
        var callback = Callback(index);
        return () => callback.Invoke();
    }

    public Action CallbackAction(string name)
    {
        var callback = Callback(name);
        return () => callback.Invoke();
    }

    public Action<T> CallbackAction<T>(int index)
    {
        var callback = Callback(index);
        return value => callback.Invoke(value);
    }

    public Action<T> CallbackAction<T>(string name)
    {
        var callback = Callback(name);
        return value => callback.Invoke(value);
    }

    public T Argument<T>(int index)
    {
        var value = Argument(index, typeof(T));
        return ExecutionContext.HasErrors ? default! : (T)value!;
    }

    public object? Argument(int index, Type type)
    {
        if ((uint)index >= (uint)arguments.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        ArgumentNullException.ThrowIfNull(type);
        return TypeConverter.ConvertTo(ExecutionContext, arguments[index], type);
    }

    public T Argument<T>(string name)
    {
        for (var i = 0; i < command.Parameters.Count; i++)
        {
            if (string.Equals(command.Parameters[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return Argument<T>(i);
            }
        }

        throw new ArgumentException($"Command '{command.Name}' has no parameter named '{name}'.", nameof(name));
    }

    public SpellkitObject Resource(SpellkitResource resource) =>
        Environment.CreateResource(resource);

    public void Log(
        SpellkitLogLevel level,
        string message,
        IReadOnlyDictionary<string, object?>? properties = null) =>
        Environment.Telemetry.Write(level, message, properties);
}

public sealed class SpellkitCallback
{
    private readonly ExecutionContext context;
    private readonly SpellkitFunction function;
    private readonly SpellkitCallbackScope scope;

    internal SpellkitCallback(
        ExecutionContext context,
        SpellkitFunction function,
        SpellkitCallbackScope scope) =>
        (this.context, this.function, this.scope) = (context, function, scope);

    public object? Invoke(params object?[] arguments) =>
        Invoke<object?>(arguments);

    public TResult? Invoke<TResult>(params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        scope.ThrowIfInactive();

        var converted = new SpellkitObject[arguments.Length];
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
