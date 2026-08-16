using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

public sealed partial class SpellkitInstance
{
    /// <summary>Asynchronously opens a named select through its basic choice-oriented API.</summary>
    public async Task<SpellkitSelect> OpenSelectAsync(string name) =>
        new(await OpenSelectSessionAsync(name).ConfigureAwait(false));

    internal async Task<SpellkitSelectSession> OpenSelectSessionAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (runtimeContext is null)
        {
            if (program is null)
            {
                throw new InvalidOperationException(
                    "Execute source containing the select before opening a select session.");
            }

            var initialization = await ExecuteAsync().ConfigureAwait(false);
            if (!initialization.Success)
            {
                throw new InvalidOperationException(
                    "The select program could not be initialized.", initialization.Failure?.Exception);
            }
        }

        if (operationScope.Value)
        {
            throw new InvalidOperationException("A host instance cannot be entered recursively.");
        }

        await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        operationScope.Value = true;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (suspendedRun is not null)
            {
                throw new InvalidOperationException("A script run is already waiting for a select.");
            }

            var factory = ResolveSelectFactory(name)
                ?? throw new ArgumentException($"No select named '{name}' is available.", nameof(name));
            return await CreateSelectSessionAsync(
                CreateSelectInstance(factory)).ConfigureAwait(false);
        }
        finally
        {
            operationScope.Value = false;
            operationGate.Release();
        }
    }

    private SelectInstance CreateSelectInstance(SpellkitSelectFactory factory)
    {
        var nested = active;
        if (!nested)
        {
            BeginOperation();
        }

        try
        {
            var context = CreateExecutionContext(runtimeContext!, control: null);
            var select = factory.Create(context);
            context.ThrowIf();
            return select;
        }
        finally
        {
            if (!nested)
            {
                active = false;
            }
        }
    }

    internal SpellkitSelectFactory? ResolveSelectFactory(string name)
    {
        if (SpellkitSelectAliases.ResolveFactory(runtimeContext!, name) is { } aliasedFactory)
        {
            return aliasedFactory;
        }

        var selectName = SpellkitSelectAliases.ResolveName(runtimeContext!, name);
        var matches = new List<SpellkitSelectFactory>();
        for (var unitId = 0; unitId < runtimeContext!.Composition.Units.Length; unitId++)
        {
            var scope = runtimeContext.Composition.Units[unitId].GlobalScope;
            if (scope is null)
            {
                continue;
            }

            var symbol = scope.GetVariable(selectName);
            if (!symbol.IsEmpty()
                && runtimeContext.Units[unitId] is { } values
                && values[symbol.Address] is SpellkitSelectFactory factory)
            {
                matches.Add(factory);
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }
        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"The select name '{name}' is ambiguous.");
        }

        return matches[0];
    }

    internal async Task<SpellkitSelectSession> CreateSelectSessionAsync(
        SelectInstance select,
        SpellkitSelectRevision? revision = null)
    {
        var session = new SpellkitSelectSession(this, select, revision);
        await session.InitializeAsync().ConfigureAwait(false);
        return session;
    }
}

/// <summary>Resolves legacy dotted select names while the VM evaluates <c>do</c>.</summary>
internal sealed class SpellkitSelectFactoryResolver(SpellkitInstance instance)
{
    internal const string ContextKey = "Spellkit.Hosting.SelectFactoryResolver";

    internal SpellkitSelectFactory? Resolve(string name) => instance.ResolveSelectFactory(name);
}
