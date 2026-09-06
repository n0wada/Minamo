using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CancellationToken = System.Threading.CancellationToken;
using EditorBrowsableAttribute = System.ComponentModel.EditorBrowsableAttribute;
using EditorBrowsableState = System.ComponentModel.EditorBrowsableState;

namespace Minamo.Hosting;

public sealed class MinamoCommandContext
{
    internal const string HostContextKey = "Minamo.Hosting.HostContext";

    private readonly MinamoCommandDescriptor command;
    private readonly MinamoObject[] arguments;
    private readonly MinamoCallbackScope callbackScope;

    internal MinamoCommandContext(
        ExecutionContext executionContext,
        MinamoCommandDescriptor command,
        MinamoObject[] arguments,
        MinamoCallbackScope callbackScope) =>
        (ExecutionContext, this.command, this.arguments, this.callbackScope) =
        (executionContext, command, arguments, callbackScope);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public ExecutionContext ExecutionContext { get; }

    public int ArgumentCount => arguments.Length;

    public string CommandName => command.Name;

    public Guid ExecutionId => Environment.Telemetry.ExecutionId;

    public CancellationToken CancellationToken =>
        ExecutionContext.Control?.CancellationToken ?? System.Threading.CancellationToken.None;

    public MinamoHostEnvironment Environment =>
        ExecutionContext.GetContextVariable<MinamoHostEnvironment>(MinamoHostEnvironment.ContextKey)
        ?? throw new InvalidOperationException("The command is not running in a hosted instance.");

    public T Host<T>() where T : class
    {
        var host = ExecutionContext.GetContextVariable<object>(HostContextKey);
        return host as T ?? throw new InvalidOperationException(
            $"The instance host context is not assignable to {typeof(T).FullName}.");
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public MinamoObject RawArgument(int index) => arguments[index];

    public MinamoCallback Callback(int index)
    {
        if ((uint)index >= (uint)arguments.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (arguments[index] is MinamoFunction function)
        {
            return new MinamoCallback(ExecutionContext, function, callbackScope);
        }

        ExecutionContext.InvalidCast(arguments[index].TypeName, nameof(MinamoTypeCodes.Function));
        return null!;
    }

    public MinamoCallback Callback(string name)
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

    public MinamoObject Resource(MinamoResource resource) =>
        Environment.CreateResource(resource);

    public void Log(
        MinamoLogLevel level,
        string message,
        IReadOnlyDictionary<string, object?>? properties = null) =>
        Environment.Telemetry.Write(level, message, properties);
}

public sealed class MinamoCallback
{
    private readonly ExecutionContext context;
    private readonly MinamoFunction function;
    private readonly MinamoCallbackScope scope;

    internal MinamoCallback(
        ExecutionContext context,
        MinamoFunction function,
        MinamoCallbackScope scope) =>
        (this.context, this.function, this.scope) = (context, function, scope);

    public object? Invoke(params object?[] arguments) =>
        Invoke<object?>(arguments);

    public TResult? Invoke<TResult>(params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        scope.ThrowIfInactive();

        var converted = new MinamoObject[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            converted[i] = MinamoCommandConvert.FromObject(arguments[i]);
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

internal sealed class MinamoCallbackScope : IDisposable
{
    private int active = 1;

    internal void ThrowIfInactive()
    {
        if (System.Threading.Volatile.Read(ref active) == 0)
        {
            throw new InvalidOperationException(
                "A Minamo callback cannot be invoked after its host command has completed.");
        }
    }

    public void Dispose() => System.Threading.Interlocked.Exchange(ref active, 0);
}
