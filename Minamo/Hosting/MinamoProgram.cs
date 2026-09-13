using Minamo.Compiler;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Minamo.Hosting;

public sealed class MinamoProgram
{
    internal MinamoProgram(
        UnitComposition composition,
        IReadOnlyList<BuildMessage> diagnostics,
        object owner)
    {
        Composition = composition ?? throw new ArgumentNullException(nameof(composition));
        Diagnostics = diagnostics.Select(MinamoDiagnostic.From).ToArray();
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    internal UnitComposition Composition { get; }

    internal object Owner { get; }

    public IReadOnlyList<MinamoDiagnostic> Diagnostics { get; }
}

public sealed class MinamoEnvironment
{
    internal const string ContextKey = "Minamo.Hosting.MinamoEnvironment";

    private readonly Dictionary<string, object?> bindings = new(StringComparer.OrdinalIgnoreCase);
    private Func<CancellationToken, ValueTask<object?>>? input;
    private Action<string>? output;

    public MinamoEnvironment(object? hostContext = null) => HostContext = hostContext;

    public object? HostContext { get; }

    public IReadOnlyDictionary<string, object?> Bindings =>
        new ReadOnlyDictionary<string, object?>(bindings);

    public MinamoEnvironment Expose(string name, object? value)
    {
        HostNames.ValidateIdentifier(name, nameof(name), "environment binding");
        bindings[name] = value;
        return this;
    }

    public MinamoEnvironment Set(string name, object? value) =>
        Expose(name, value);

    public MinamoEnvironment UseInputAsync<T>(Func<CancellationToken, ValueTask<T>> receive)
    {
        ArgumentNullException.ThrowIfNull(receive);
        input = async cancellationToken =>
            await receive(cancellationToken).ConfigureAwait(false);
        return this;
    }

    public MinamoEnvironment UseOutput(Action<string> write)
    {
        output = write ?? throw new ArgumentNullException(nameof(write));
        return this;
    }

    public bool TryGet(string name, out object? value) =>
        bindings.TryGetValue(name, out value);

    internal ValueTask<MinamoObject> ReadInputAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var receive = input ?? throw new InvalidOperationException(
            "Host input is not configured for this instance.");
        return ConvertInputAsync(receive, cancellationToken);
    }

    private static async ValueTask<MinamoObject> ConvertInputAsync(
        Func<CancellationToken, ValueTask<object?>> receive,
        CancellationToken cancellationToken) =>
        TypeConverter.ConvertFrom(await receive(cancellationToken).ConfigureAwait(false));

    internal void Write(string value)
    {
        var write = output ?? Console.Write;
        write(value);
    }

    internal bool TryResolve(string name, out MinamoObject value)
    {
        if (!bindings.TryGetValue(name, out var raw))
        {
            value = MinamoNil.Instance;
            return false;
        }

        value = TypeConverter.ConvertFrom(raw);
        return true;
    }
}
