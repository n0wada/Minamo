using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExecutionContext = Spellkit.Runtime.ExecutionContext;

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

    internal SelectInstance CreateSelectInstance(SpellkitSelectFactory factory)
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

    private async ValueTask<ExecutionResult> CompleteExecutionAsync(ExecutionResult result)
    {
        while (result.Reason is TerminationReason.Suspended)
        {
            if (result.Continuation is null || result.Suspension is null)
            {
                throw new InvalidOperationException("The VM suspended without a continuation request.");
            }

            if (result.Suspension.Awaitable is { } awaitable)
            {
                await awaitable.WaitAsync().ConfigureAwait(false);
                result = SpellkitMachine.Resume(result.Continuation, awaitable);
                continue;
            }

            if (result.Suspension.Select is not { } selectInstance)
            {
                throw new InvalidOperationException("The VM suspended without a supported request.");
            }

            using var select = await CreateSelectSessionAsync(selectInstance).ConfigureAwait(false);
            if (!select.IsCompleted)
            {
                await SpellkitEnvironment.RunSelectAsync(select).ConfigureAwait(false);
            }
            result = SpellkitMachine.Resume(result.Continuation, select.CompletionValue);
        }

        return result;
    }

    internal Task<SpellkitSelectResult> SelectAsync(
        SpellkitRunSession run,
        string choiceId,
        object? argument,
        bool hasArgument) =>
        DispatchSelectActionAsync(
            run,
            session => hasArgument
                ? session.SelectAsync(choiceId, argument)
                : session.SelectAsync(choiceId));

    internal Task<SpellkitSelectResult> SendAsync(
        SpellkitRunSession run,
        string eventId,
        object? argument,
        bool hasArgument) =>
        DispatchSelectActionAsync(
            run,
            session => hasArgument
                ? session.SendAsync(eventId, argument)
                : session.SendAsync(eventId));

    internal Task<SpellkitSelectResult> SelectAtRevisionAsync(
        SpellkitRunSession run,
        string choiceId,
        object? argument,
        bool hasArgument,
        long revision) =>
        DispatchSelectActionAsync(
            run,
            session => hasArgument
                ? session.SelectAtRevisionAsync(choiceId, argument, revision)
                : session.SelectAtRevisionAsync(choiceId, revision));

    internal Task<SpellkitSelectResult> SendAtRevisionAsync(
        SpellkitRunSession run,
        string eventId,
        object? argument,
        bool hasArgument,
        long revision) =>
        DispatchSelectActionAsync(
            run,
            session => hasArgument
                ? session.SendAtRevisionAsync(eventId, argument, revision)
                : session.SendAtRevisionAsync(eventId, revision));

    internal Task<SpellkitSelectResult> RefreshSelectAsync(
        SpellkitRunSession run,
        bool invalidate) =>
        DispatchSelectActionAsync(
            run,
            async session => new SpellkitSelectResult(
                invalidate
                    ? await session.InvalidateAsync().ConfigureAwait(false)
                    : await session.RefreshAsync().ConfigureAwait(false)),
            failRunOnError: false);

    private async Task<SpellkitSelectResult> DispatchSelectActionAsync(
        SpellkitRunSession run,
        Func<SpellkitSelectSession, Task<SpellkitSelectResult>> dispatch,
        bool failRunOnError = true)
    {
        if (operationScope.Value)
        {
            throw new InvalidOperationException("A host instance cannot be entered recursively.");
        }

        await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        operationScope.Value = true;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BeginOperation(run);
            try
            {
                var select = run.GetSelect();
                var actionResult = await dispatch(select).ConfigureAwait(false);
                if (!actionResult.IsCompleted)
                {
                    return actionResult;
                }

                var completionValue = select.CompletionValue;
                var execution = await ResumeSelectContinuationAsync(
                    run.GetContinuation(),
                    completionValue).ConfigureAwait(false);
                await run.AdvanceAsync(execution).ConfigureAwait(false);
                if (run.IsCompleted)
                {
                    suspendedRun = null;
                    return new SpellkitSelectResult(actionResult.Snapshot, actionResult.Value);
                }

                return new SpellkitSelectResult(run.GetSelect().Snapshot);
            }
            catch (SpellkitSelectRevisionMismatchException)
            {
                throw;
            }
            catch (Exception) when (!failRunOnError)
            {
                throw;
            }
            catch (Exception ex)
            {
                run.Fail(ex);
                suspendedRun = null;
                throw;
            }
            finally
            {
                active = false;
            }
        }
        finally
        {
            operationScope.Value = false;
            operationGate.Release();
        }
    }

    internal void Cancel(SpellkitRunSession run)
    {
        EnterSynchronousOperationGate();
        try
        {
            lock (syncRoot)
            {
                if (ReferenceEquals(suspendedRun, run))
                {
                    suspendedRun = null;
                }

                run.Cancel();
            }
        }
        finally
        {
            ExitSynchronousOperationGate();
        }
    }

    internal async Task<ExecutionResult> InvokeSelectActionAsync(
        SpellkitFunction action,
        SpellkitObject[] arguments)
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
            if (action is not SpellkitNativeFunction function)
            {
                throw new InvalidOperationException("The select action function is unavailable.");
            }

            var result = await Task.Run(
                () => SpellkitMachine.ExecuteWithArguments(function, arguments, context),
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

    internal async Task<ExecutionResult> ResumeSelectContinuationAsync(
        SpellkitMachine.VmContinuation continuation,
        SpellkitObject value)
    {
        var result = SpellkitMachine.Resume(continuation, value);
        return await CompleteAwaitablesAsync(result).ConfigureAwait(false);
    }

    internal async Task<bool> EvaluateSelectGuardAsync(
        SpellkitFunction guard,
        SpellkitObject[] arguments) =>
        (await EvaluateSelectValueAsync(guard, arguments, "guard").ConfigureAwait(false)).IsTrue();

    internal Task<SpellkitObject> EvaluateSelectDescriptionAsync(
        SpellkitFunction description,
        SpellkitObject[] arguments) =>
        EvaluateSelectValueAsync(description, arguments, "description");

    internal Task<SpellkitObject> EvaluateSelectDynamicChoiceAsync(
        SpellkitFunction function,
        SpellkitObject[] arguments) =>
        EvaluateSelectValueAsync(function, arguments, "dynamic choice");

    internal Task<SpellkitObject> EvaluateSelectChoiceSpreadAsync(
        SpellkitFunction function,
        SpellkitObject[] arguments) =>
        EvaluateSelectValueAsync(function, arguments, "choice spread");

    private async Task<SpellkitObject> EvaluateSelectValueAsync(
        SpellkitFunction function,
        SpellkitObject[] arguments,
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
                if (suspendedRun is null)
                {
                    BeginOperation();
                }
                else
                {
                    active = true;
                }
            }
            try
            {
                var context = CreateExecutionContext(runtimeContext!, control: null);
                SpellkitObject value;
                if (function is SpellkitNativeFunction nativeFunction)
                {
                    var execution = await Task.Run(
                        () => SpellkitMachine.ExecuteWithArguments(nativeFunction, arguments, context),
                        CancellationToken.None).ConfigureAwait(false);
                    execution = await CompleteAwaitablesAsync(execution).ConfigureAwait(false);
                    if (execution.Reason is TerminationReason.Suspended)
                    {
                        throw new InvalidOperationException($"A select {kind} cannot start a select.");
                    }
                    value = execution.Value ?? SpellkitNil.Instance;
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

    private void SetSelectHostVariables(ExecutionContext context) =>
        context.SetContextVariable(
            SpellkitSelectFactoryResolver.ContextKey,
            new SpellkitSelectFactoryResolver(this));
}

/// <summary>Resolves legacy dotted select names while the VM evaluates <c>do</c>.</summary>
internal sealed class SpellkitSelectFactoryResolver(SpellkitInstance instance)
{
    internal const string ContextKey = "Spellkit.Hosting.SelectFactoryResolver";

    internal SpellkitSelectFactory? Resolve(string name) => instance.ResolveSelectFactory(name);
}
