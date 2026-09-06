using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExecutionContext = Minamo.Runtime.ExecutionContext;

namespace Minamo.Hosting;

public sealed partial class MinamoInstance
{
    /// <summary>Asynchronously opens a named select through its basic choice-oriented API.</summary>
    public async Task<MinamoSelect> OpenSelectAsync(string name) =>
        new(await OpenSelectSessionAsync(name).ConfigureAwait(false));

    internal async Task<MinamoSelectSession> OpenSelectSessionAsync(string name)
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

    internal SelectInstance CreateSelectInstance(MinamoSelectFactory factory)
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

    internal MinamoSelectFactory? ResolveSelectFactory(string name)
    {
        if (MinamoSelectAliases.ResolveFactory(runtimeContext!, name) is { } aliasedFactory)
        {
            return aliasedFactory;
        }

        var selectName = MinamoSelectAliases.ResolveName(runtimeContext!, name);
        var matches = new List<MinamoSelectFactory>();
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
                && values[symbol.Address] is MinamoSelectFactory factory)
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

    internal async Task<MinamoSelectSession> CreateSelectSessionAsync(
        SelectInstance select)
    {
        var session = new MinamoSelectSession(this, select);
        await session.InitializeAsync().ConfigureAwait(false);
        return session;
    }

    internal async Task<ExecutionResult> InvokeSelectActionAsync(
        MinamoFunction action,
        MinamoObject[] arguments)
    {
        var ownsGate = !operationScope.Value;
        if (ownsGate)
        {
            await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            operationScope.Value = true;
        }

        var nested = false;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            nested = active;
            if (!nested)
            {
                BeginOperation();
            }

            var context = CreateExecutionContext(runtimeContext!, control: null);
            if (action is not MinamoNativeFunction function)
            {
                throw new InvalidOperationException("The select action function is unavailable.");
            }

            var result = await Task.Run(
                () => MinamoMachine.ExecuteWithArguments(function, arguments, context),
                CancellationToken.None).ConfigureAwait(false);
            return await CompleteAwaitablesAsync(result).ConfigureAwait(false);
        }
        finally
        {
            if (!nested)
            {
                active = false;
            }
            if (ownsGate)
            {
                operationScope.Value = false;
                operationGate.Release();
            }
        }
    }

    internal async Task<bool> EvaluateSelectGuardAsync(
        MinamoFunction guard,
        MinamoObject[] arguments) =>
        (await EvaluateSelectValueAsync(guard, arguments, "guard").ConfigureAwait(false)).IsTrue();

    internal Task<MinamoObject> EvaluateSelectDescriptionAsync(
        MinamoFunction description,
        MinamoObject[] arguments) =>
        EvaluateSelectValueAsync(description, arguments, "description");

    internal Task<MinamoObject> EvaluateSelectDynamicChoiceAsync(
        MinamoFunction function,
        MinamoObject[] arguments) =>
        EvaluateSelectValueAsync(function, arguments, "dynamic choice");

    internal Task<MinamoObject> EvaluateSelectChoiceSpreadAsync(
        MinamoFunction function,
        MinamoObject[] arguments) =>
        EvaluateSelectValueAsync(function, arguments, "choice spread");

    private async Task<MinamoObject> EvaluateSelectValueAsync(
        MinamoFunction function,
        MinamoObject[] arguments,
        string kind)
    {
        var ownsGate = !operationScope.Value;
        if (ownsGate)
        {
            await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            operationScope.Value = true;
        }

        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var nested = active;
            if (!nested)
            {
                BeginOperation();
            }
            try
            {
                var context = CreateExecutionContext(runtimeContext!, control: null);
                MinamoObject value;
                if (function is MinamoNativeFunction nativeFunction)
                {
                    var execution = await Task.Run(
                        () => MinamoMachine.ExecuteWithArguments(nativeFunction, arguments, context),
                        CancellationToken.None).ConfigureAwait(false);
                    execution = await CompleteAwaitablesAsync(execution).ConfigureAwait(false);
                    if (execution.Reason is TerminationReason.Suspended)
                    {
                        throw new InvalidOperationException($"A select {kind} did not complete successfully.");
                    }
                    value = execution.Value ?? MinamoNil.Instance;
                }
                else
                {
                    value = function.Call(context, arguments);
                }
                context.ThrowIf();
                return value;
            }
            finally
            {
                if (!nested)
                {
                    active = false;
                }
            }
        }
        finally
        {
            if (ownsGate)
            {
                operationScope.Value = false;
                operationGate.Release();
            }
        }
    }

}
