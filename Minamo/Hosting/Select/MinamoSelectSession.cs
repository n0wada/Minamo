using Minamo.Compiler;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Minamo.Hosting;

internal sealed partial class MinamoSelectSession : IDisposable
{
    private readonly System.Threading.SemaphoreSlim actionGate = new(1, 1);
    private readonly CancellationTokenSource sessionCancellation = new();
    private readonly MinamoInstance instance;
    private readonly Stack<SelectNavigationFrame> navigationStack = new();
    private SelectInstance selectInstance;
    private MinamoSelectSnapshot snapshot;
    private MinamoSelectDescription? description;
    private IReadOnlyList<ResolvedSelectAction> availableActions = Array.Empty<ResolvedSelectAction>();
    private ExecutionResult? pendingExecution;
    private int disposeRequested;
    private bool cleanupComplete;

    private sealed record SelectNavigationFrame(
        SelectInstance Instance,
        MinamoSelectDescription? Description);

    internal MinamoSelectSession(
        MinamoInstance instance,
        SelectInstance selectInstance)
    {
        this.instance = instance;
        this.selectInstance = selectInstance;
        snapshot = CreateInitialSnapshot();
    }

    internal async Task InitializeAsync(
        ExecutionControl? control = null,
        CancellationToken cancellationToken = default)
    {
        description = await CreateDescriptionAsync(
            selectInstance.Description(),
            Array.Empty<MinamoObject>(),
            cancellationToken,
            control).ConfigureAwait(false);

        await PublishSnapshotAsync(cancellationToken, control).ConfigureAwait(false);
    }

    internal string Name => CurrentSnapshot.Name;

    internal MinamoSelectSnapshot Snapshot => CurrentSnapshot;

    internal MinamoSelectDescription? Description => CurrentSnapshot.Description;

    internal IReadOnlyList<MinamoSelectProperty> Properties => CurrentSnapshot.Properties;

    internal IReadOnlyList<MinamoSelectAction> Actions => CurrentSnapshot.Actions;

    internal bool IsCompleted => CurrentSnapshot.IsCompleted;

    internal MinamoSelectRequest? Request => CurrentSnapshot.Request;

    internal MinamoObject CompletionValue => selectInstance.Value;

    internal T? GetValue<T>()
    {
        EnsureCompleted();
        return MinamoHostValueConverter.Convert<T>(CompletionValue, "Select result");
    }

    internal bool TryGetValue<T>(out T? result)
    {
        if (!IsCompleted)
        {
            result = default;
            return false;
        }

        return MinamoHostValueConverter.TryConvert(CompletionValue, out result);
    }

    private MinamoSelectSnapshot CurrentSnapshot => snapshot;

    private MinamoSelectSnapshot CreateInitialSnapshot() => new(
        selectInstance.Name,
        description,
        Array.Empty<MinamoSelectProperty>(),
        Array.Empty<MinamoSelectAction>(),
        selectInstance.IsCompleted);

    private void PublishCompletedSnapshot()
    {
        availableActions = Array.Empty<ResolvedSelectAction>();
        snapshot = new(
            selectInstance.Name,
            description,
            CurrentSnapshot.Properties,
            Array.Empty<MinamoSelectAction>(),
            isCompleted: true);
    }

    private MinamoSelectSnapshot CreateSnapshot(
        IReadOnlyList<MinamoSelectProperty> properties,
        IReadOnlyList<MinamoSelectAction> actions) =>
        new(
            selectInstance.Name,
            description,
            properties,
            actions,
            selectInstance.IsCompleted);

    internal Task SelectAsync(MinamoSelectAction action) =>
        SelectCoreAsync(action.Id, null, hasArgument: false, expectedAction: action);

    internal Task SelectAsync(MinamoSelectAction action, object? argument) =>
        SelectCoreAsync(action.Id, argument, hasArgument: true, expectedAction: action);

    internal Task SendAsync(string eventId) =>
        SendCoreAsync(eventId, null, hasArgument: false);

    internal Task SendAsync(string eventId, object? argument) =>
        SendCoreAsync(eventId, argument, hasArgument: true);

    internal Task RespondAsync(
        MinamoSelectRequest request,
        object? response) =>
        RespondCoreAsync(request, response);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeRequested, 1) != 0)
        {
            return;
        }

        sessionCancellation.Cancel();
        if (!actionGate.Wait(0))
        {
            return;
        }

        try
        {
            CompleteDisposal();
        }
        finally
        {
            actionGate.Release();
        }
    }

    private async Task SelectCoreAsync(
        string actionId,
        object? argument,
        bool hasArgument,
        MinamoSelectAction? expectedAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        await EnterActionGateAsync().ConfigureAwait(false);
        try
        {
            EnsureNotWaitingForResponse();
            EnsureCurrentAction(expectedAction);
            if (selectInstance.IsCompleted)
            {
                throw new InvalidOperationException($"Select session '{Name}' has already completed.");
            }

            var action = availableActions.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, actionId, StringComparison.Ordinal));
            if (action is null)
            {
                throw new ArgumentException(
                    $"Action '{actionId}' is not currently available in select '{Name}'.",
                    nameof(actionId));
            }

            var arguments = ConvertArguments(action, argument, hasArgument);
            var result = await instance.InvokeSelectActionAsync(
                action.Handler ?? throw new InvalidOperationException("The select action handler is unavailable."),
                arguments,
                allowRequests: true,
                cancellationToken: sessionCancellation.Token).ConfigureAwait(false);
            sessionCancellation.Token.ThrowIfCancellationRequested();
            await ApplyActionExecutionAsync(result, sessionCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            CompleteDisposalIfRequested();
            actionGate.Release();
        }
    }

    private async Task SendCoreAsync(
        string eventId,
        object? argument,
        bool hasArgument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        await EnterActionGateAsync().ConfigureAwait(false);
        try
        {
            EnsureNotWaitingForResponse();
            if (selectInstance.IsCompleted)
            {
                throw new InvalidOperationException($"Select session '{Name}' has already completed.");
            }

            var eventOwner = selectInstance;
            var handlers = GetEventHandlers(eventId);
            if (handlers.Count == 0)
            {
                throw new ArgumentException(
                    $"Event '{eventId}' is not handled in select '{Name}'.",
                    nameof(eventId));
            }

            foreach (var handler in handlers)
            {
                var arguments = ConvertArguments(
                    handler.Name,
                    handler.ParameterCount,
                    "Event",
                    argument,
                    hasArgument);
                var result = await instance.InvokeSelectActionAsync(
                    eventOwner.Event(handler),
                    arguments,
                    cancellationToken: sessionCancellation.Token).ConfigureAwait(false);
                sessionCancellation.Token.ThrowIfCancellationRequested();
                await ApplyActionExecutionAsync(result, sessionCancellation.Token).ConfigureAwait(false);
                if (eventOwner.IsCompleted
                    || !ReferenceEquals(selectInstance, eventOwner))
                {
                    return;
                }
            }
        }
        finally
        {
            CompleteDisposalIfRequested();
            actionGate.Release();
        }
    }

    private async Task ApplyActionExecutionAsync(
        ExecutionResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Reason is TerminationReason.Suspended
            && result.Suspension?.Awaitable is MinamoSelectRequestAwaitable request)
        {
            PublishRequest(result, request);
            return;
        }
        if (result.Reason is not TerminationReason.Complete)
        {
            throw new InvalidOperationException("The select action did not complete successfully.");
        }

        var outcome = selectInstance.Apply(result.Value ?? MinamoNil.Instance);
        switch (outcome.Kind)
        {
            case SelectActionOutcomeKind.Continue:
                await PublishSnapshotAsync(cancellationToken).ConfigureAwait(false);
                break;
            case SelectActionOutcomeKind.Goto:
                await NavigateAsync(outcome.Target!, cancellationToken).ConfigureAwait(false);
                break;
            case SelectActionOutcomeKind.Return:
                ReturnFromCurrentSelect();
                await PublishSnapshotAsync(cancellationToken).ConfigureAwait(false);
                break;
            case SelectActionOutcomeKind.Exit:
                ExitAllSelects(outcome.Value ?? MinamoNil.Instance);
                PublishCompletedSnapshot();
                break;
            default:
                throw new InvalidOperationException("Unknown select action outcome.");
        }
    }

    private async Task NavigateAsync(
        MinamoSelectFactory target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var next = instance.CreateSelectInstance(target);
        var nextDescription = await CreateDescriptionAsync(
            next.Description(),
            Array.Empty<MinamoObject>(),
            cancellationToken).ConfigureAwait(false);

        var previous = new SelectNavigationFrame(selectInstance, description);
        navigationStack.Push(previous);
        selectInstance = next;
        description = nextDescription;

        try
        {
            await PublishSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (ReferenceEquals(selectInstance, next)
                && navigationStack.TryPeek(out var current)
                && ReferenceEquals(current, previous))
            {
                next.Cancel();
                navigationStack.Pop();
                selectInstance = previous.Instance;
                description = previous.Description;
            }

            throw;
        }
    }

    private bool ReturnFromCurrentSelect()
    {
        selectInstance.Complete();
        if (!navigationStack.TryPop(out var previous))
        {
            return false;
        }

        selectInstance = previous.Instance;
        description = previous.Description;
        return true;
    }

    private void ExitAllSelects(MinamoObject value)
    {
        selectInstance.Complete(value);
        while (navigationStack.TryPop(out var previous))
        {
            previous.Instance.Cancel();
        }
    }

    private void CancelAllSelects()
    {
        selectInstance.Cancel();
        while (navigationStack.TryPop(out var previous))
        {
            previous.Instance.Cancel();
        }
    }

    private void PublishRequest(
        ExecutionResult execution,
        MinamoSelectRequestAwaitable awaitable)
    {
        if (pendingExecution is not null)
        {
            throw new InvalidOperationException("The select is already waiting for a host response.");
        }

        var request = new MinamoSelectRequest(awaitable);
        pendingExecution = execution;
        availableActions = Array.Empty<ResolvedSelectAction>();
        snapshot = new(
            selectInstance.Name,
            description,
            CurrentSnapshot.Properties,
            Array.Empty<MinamoSelectAction>(),
            isCompleted: false,
            request);
    }

    private async Task RespondCoreAsync(
        MinamoSelectRequest request,
        object? response)
    {
        await EnterActionGateAsync().ConfigureAwait(false);
        try
        {
            if (pendingExecution is not { } execution
                || CurrentSnapshot.Request is not { } current
                || !ReferenceEquals(current, request)
                || !ReferenceEquals(current.Awaitable, execution.Suspension?.Awaitable))
            {
                throw new InvalidOperationException("The select is not waiting for this request.");
            }

            var value = MinamoCommandConvert.FromObject(response);
            pendingExecution = null;
            try
            {
                var result = await instance.ResumeSelectActionAsync(
                    execution,
                    current.Awaitable,
                    value,
                    sessionCancellation.Token).ConfigureAwait(false);
                sessionCancellation.Token.ThrowIfCancellationRequested();
                await ApplyActionExecutionAsync(result, sessionCancellation.Token).ConfigureAwait(false);
            }
            catch
            {
                CancelAllSelects();
                PublishCompletedSnapshot();
                throw;
            }
        }
        finally
        {
            CompleteDisposalIfRequested();
            actionGate.Release();
        }
    }

    private void EnsureCurrentAction(MinamoSelectAction? expectedAction)
    {
        if (expectedAction is null
            || CurrentSnapshot.Actions.Any(action => ReferenceEquals(action, expectedAction)))
        {
            return;
        }

        throw new ArgumentException(
            $"Action '{expectedAction.Id}' is not currently available in select '{Name}'.",
            nameof(expectedAction));
    }

    private void EnsureNotWaitingForResponse()
    {
        if (pendingExecution is not null)
        {
            throw new InvalidOperationException(
                $"Select session '{Name}' is waiting for a host response.");
        }
    }

    private void EnsureCompleted()
    {
        if (!IsCompleted)
        {
            throw new InvalidOperationException("The select has not completed.");
        }
    }

    private void AbandonPendingRequest()
    {
        pendingExecution?.Suspension?.Awaitable?.Abandon();
        pendingExecution?.Continuation?.Complete();
        pendingExecution = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposeRequested) != 0,
            this);
    }

    private async Task EnterActionGateAsync()
    {
        ThrowIfDisposed();
        try
        {
            await actionGate.WaitAsync(sessionCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref disposeRequested) != 0)
        {
            ThrowIfDisposed();
            throw;
        }

        try
        {
            ThrowIfDisposed();
        }
        catch
        {
            actionGate.Release();
            throw;
        }
    }

    private void CompleteDisposalIfRequested()
    {
        if (Volatile.Read(ref disposeRequested) != 0)
        {
            CompleteDisposal();
        }
    }

    private void CompleteDisposal()
    {
        if (cleanupComplete)
        {
            return;
        }

        AbandonPendingRequest();
        CancelAllSelects();
        cleanupComplete = true;
        sessionCancellation.Dispose();
    }
}
