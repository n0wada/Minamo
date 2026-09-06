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
    private Func<CancellationToken, ValueTask<string?>>? input;
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

    public MinamoEnvironment UseInputAsync(Func<CancellationToken, ValueTask<string?>> readLine)
    {
        input = readLine ?? throw new ArgumentNullException(nameof(readLine));
        return this;
    }

    public MinamoEnvironment UseOutput(Action<string> write)
    {
        output = write ?? throw new ArgumentNullException(nameof(write));
        return this;
    }

    public bool TryGet(string name, out object? value) =>
        bindings.TryGetValue(name, out value);

    internal async ValueTask<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var readLine = input ?? ReadConsoleLineAsync;
        return await readLine(cancellationToken).ConfigureAwait(false) ?? string.Empty;
    }

    private static async ValueTask<string?> ReadConsoleLineAsync(CancellationToken cancellationToken) =>
        await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);

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
