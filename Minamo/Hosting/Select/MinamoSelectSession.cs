using Minamo.Compiler;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Minamo.Hosting;

internal sealed partial class MinamoSelectSession : IDisposable
{
    private readonly System.Threading.SemaphoreSlim actionGate = new(1, 1);
    private readonly MinamoInstance instance;
    private readonly Stack<SelectNavigationFrame> navigationStack = new();
    private SelectInstance selectInstance;
    private MinamoSelectSnapshot snapshot;
    private MinamoSelectDescription? description;
    private IReadOnlyList<ResolvedSelectChoice> availableChoices = Array.Empty<ResolvedSelectChoice>();
    private ExecutionResult? pendingExecution;
    private bool disposed;

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

    internal async Task InitializeAsync()
    {
        description = await CreateDescriptionAsync(
            selectInstance.Description(),
            Array.Empty<MinamoObject>()).ConfigureAwait(false);

        await PublishSnapshotAsync().ConfigureAwait(false);
    }

    internal string Name => CurrentSnapshot.Name;

    internal MinamoSelectSnapshot Snapshot => CurrentSnapshot;

    internal MinamoSelectDescription? Description => CurrentSnapshot.Description;

    internal IReadOnlyList<MinamoSelectProperty> Properties => CurrentSnapshot.Properties;

    internal IReadOnlyList<MinamoChoice> Choices => CurrentSnapshot.Choices;

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
        Array.Empty<MinamoChoice>(),
        selectInstance.IsCompleted);

    private void PublishCompletedSnapshot()
    {
        availableChoices = Array.Empty<ResolvedSelectChoice>();
        snapshot = new(
            selectInstance.Name,
            description,
            CurrentSnapshot.Properties,
            Array.Empty<MinamoChoice>(),
            isCompleted: true);
    }

    private MinamoSelectSnapshot CreateSnapshot(
        IReadOnlyList<MinamoSelectProperty> properties,
        IReadOnlyList<MinamoChoice> choices) =>
        new(
            selectInstance.Name,
            description,
            properties,
            choices,
            selectInstance.IsCompleted);

    internal Task SelectAsync(MinamoChoice choice) =>
        SelectCoreAsync(choice.Id, null, hasArgument: false, expectedChoice: choice);

    internal Task SelectAsync(MinamoChoice choice, object? argument) =>
        SelectCoreAsync(choice.Id, argument, hasArgument: true, expectedChoice: choice);

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
        actionGate.Wait();
        try
        {
            if (disposed)
            {
                return;
            }

            AbandonPendingRequest();
            CancelAllSelects();
            disposed = true;
        }
        finally
        {
            actionGate.Release();
        }
    }

    private async Task SelectCoreAsync(
        string choiceId,
        object? argument,
        bool hasArgument,
        MinamoChoice? expectedChoice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(choiceId);
        await actionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureNotWaitingForResponse();
            EnsureCurrentChoice(expectedChoice);
            if (selectInstance.IsCompleted)
            {
                throw new InvalidOperationException($"Select session '{Name}' has already completed.");
            }

            var choice = availableChoices.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, choiceId, StringComparison.Ordinal));
            if (choice is null)
            {
                throw new ArgumentException(
                    $"Choice '{choiceId}' is not currently available in select '{Name}'.",
                    nameof(choiceId));
            }

            var arguments = ConvertArguments(choice, argument, hasArgument);
            var result = await instance.InvokeSelectActionAsync(
                choice.Action ?? throw new InvalidOperationException("The select choice action is unavailable."),
                arguments,
                allowRequests: true).ConfigureAwait(false);
            await ApplyActionExecutionAsync(result).ConfigureAwait(false);
        }
        finally
        {
            actionGate.Release();
        }
    }

    private async Task SendCoreAsync(
        string eventId,
        object? argument,
        bool hasArgument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        await actionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
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
                    arguments).ConfigureAwait(false);
                await ApplyActionExecutionAsync(result).ConfigureAwait(false);
                if (eventOwner.IsCompleted
                    || !ReferenceEquals(selectInstance, eventOwner))
                {
                    return;
                }
            }
        }
        finally
        {
            actionGate.Release();
        }
    }

    private async Task ApplyActionExecutionAsync(ExecutionResult result)
    {
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
                await PublishSnapshotAsync().ConfigureAwait(false);
                break;
            case SelectActionOutcomeKind.Goto:
                await NavigateAsync(outcome.Target!).ConfigureAwait(false);
                break;
            case SelectActionOutcomeKind.Return:
                ReturnFromCurrentSelect();
                await PublishSnapshotAsync().ConfigureAwait(false);
                break;
            case SelectActionOutcomeKind.Exit:
                ExitAllSelects(outcome.Value ?? MinamoNil.Instance);
                PublishCompletedSnapshot();
                break;
            default:
                throw new InvalidOperationException("Unknown select action outcome.");
        }
    }

    private async Task NavigateAsync(MinamoSelectFactory target)
    {
        var next = instance.CreateSelectInstance(target);
        var nextDescription = await CreateDescriptionAsync(
            next.Description(),
            Array.Empty<MinamoObject>()).ConfigureAwait(false);

        var previous = new SelectNavigationFrame(selectInstance, description);
        navigationStack.Push(previous);
        selectInstance = next;
        description = nextDescription;

        try
        {
            await PublishSnapshotAsync().ConfigureAwait(false);
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
        availableChoices = Array.Empty<ResolvedSelectChoice>();
        snapshot = new(
            selectInstance.Name,
            description,
            CurrentSnapshot.Properties,
            Array.Empty<MinamoChoice>(),
            isCompleted: false,
            request);
    }

    private async Task RespondCoreAsync(
        MinamoSelectRequest request,
        object? response)
    {
        await actionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
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
                    value).ConfigureAwait(false);
                await ApplyActionExecutionAsync(result).ConfigureAwait(false);
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
            actionGate.Release();
        }
    }

    private void EnsureCurrentChoice(MinamoChoice? expectedChoice)
    {
        if (expectedChoice is null
            || CurrentSnapshot.Choices.Any(choice => ReferenceEquals(choice, expectedChoice)))
        {
            return;
        }

        throw new ArgumentException(
            $"Choice '{expectedChoice.Id}' is not currently available in select '{Name}'.",
            nameof(expectedChoice));
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
        pendingExecution?.Continuation?.Complete();
        pendingExecution = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
