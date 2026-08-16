using Spellkit.Compiler;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

public sealed class SpellkitProgram
{
    internal SpellkitProgram(
        UnitComposition composition,
        IReadOnlyList<BuildMessage> diagnostics,
        object owner)
    {
        Composition = composition ?? throw new ArgumentNullException(nameof(composition));
        Diagnostics = diagnostics.Select(SpellkitDiagnostic.From).ToArray();
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    internal UnitComposition Composition { get; }

    internal object Owner { get; }

    public IReadOnlyList<SpellkitDiagnostic> Diagnostics { get; }
}

public sealed class SpellkitEnvironment
{
    internal const string ContextKey = "Spellkit.Hosting.SpellkitEnvironment";

    private readonly Dictionary<string, object?> bindings = new(StringComparer.OrdinalIgnoreCase);
    private Func<CancellationToken, string?>? input;
    private Action<string>? output;
    private Func<SpellkitSelect, ValueTask>? asyncSelectRunner;

    public SpellkitEnvironment(object? hostContext = null) => HostContext = hostContext;

    public object? HostContext { get; }

    public IReadOnlyDictionary<string, object?> Bindings =>
        new ReadOnlyDictionary<string, object?>(bindings);

    public SpellkitEnvironment Expose(string name, object? value)
    {
        HostNames.ValidateIdentifier(name, nameof(name), "environment binding");
        bindings[name] = value;
        return this;
    }

    public SpellkitEnvironment Set(string name, object? value) =>
        Expose(name, value);

    public SpellkitEnvironment UseInput(Func<CancellationToken, string?> readLine)
    {
        input = readLine ?? throw new ArgumentNullException(nameof(readLine));
        return this;
    }

    public SpellkitEnvironment UseOutput(Action<string> write)
    {
        output = write ?? throw new ArgumentNullException(nameof(write));
        return this;
    }

    public SpellkitEnvironment UseSelectAsync(
        Func<SpellkitSelect, ValueTask> run)
    {
        asyncSelectRunner = run ?? throw new ArgumentNullException(nameof(run));
        return this;
    }

    public bool TryGet(string name, out object? value) =>
        bindings.TryGetValue(name, out value);

    internal string ReadLine(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var readLine = input ?? (_ => Console.ReadLine());
        return readLine(cancellationToken) ?? string.Empty;
    }

    internal void Write(string value)
    {
        var write = output ?? Console.Write;
        write(value);
    }

    internal async ValueTask RunSelectAsync(SpellkitSelectSession session)
    {
        var runAsync = asyncSelectRunner
            ?? throw new InvalidOperationException(
                "No asynchronous select runner is configured for this Spellkit environment.");
        await runAsync(new SpellkitSelect(session)).ConfigureAwait(false);

        EnsureSelectCompleted(session);
    }

    private static void EnsureSelectCompleted(SpellkitSelectSession session)
    {
        if (!session.IsCompleted)
        {
            throw new InvalidOperationException(
                $"The select runner returned before select '{session.Name}' completed or was cancelled.");
        }
    }

    internal bool TryResolve(string name, out SpellkitObject value)
    {
        if (!bindings.TryGetValue(name, out var raw))
        {
            value = SpellkitNil.Instance;
            return false;
        }

        value = TypeConverter.ConvertFrom(raw);
        return true;
    }
}
