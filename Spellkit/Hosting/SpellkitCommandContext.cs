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
    private readonly SpkObject[] arguments;
    private readonly SpellkitCallbackScope callbackScope;

    internal SpellkitCommandContext(
        ExecutionContext executionContext,
        SpellkitCommandDescriptor command,
        SpkObject[] arguments,
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
    public SpkObject RawArgument(int index) => arguments[index];

    public SpellkitCallback Callback(int index)
    {
        if ((uint)index >= (uint)arguments.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (arguments[index] is SpkFunction function)
        {
            return new SpellkitCallback(ExecutionContext, function, callbackScope);
        }

        ExecutionContext.InvalidCast(arguments[index].TypeName, nameof(Spk.Function));
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

    public SpkObject Resource(SpellkitResource resource) =>
        Environment.CreateResource(resource);

    public void Log(
        SpellkitLogLevel level,
        string message,
        IReadOnlyDictionary<string, object?>? properties = null) =>
        Environment.Telemetry.Write(level, message, properties);
}
