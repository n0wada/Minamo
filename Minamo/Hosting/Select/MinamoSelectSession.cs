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
    private readonly SelectInstance selectInstance;
    private readonly MinamoSelectRevision revision;
    private MinamoSelectSnapshot snapshot;
    private MinamoSelectDescription? description;
    private IReadOnlyList<ResolvedSelectChoice> availableChoices = Array.Empty<ResolvedSelectChoice>();
    private readonly Dictionary<SelectChoiceSpreadDefinition, SelectInstance> expandedSelects = new();
    private bool actionChangedControl;
    private bool disposed;

    internal MinamoSelectSession(
        MinamoInstance instance,
        SelectInstance selectInstance)
    {
        this.instance = instance;
        this.selectInstance = selectInstance;
        revision = new MinamoSelectRevision();
        snapshot = CreateInitialSnapshot();
    }

    internal async Task InitializeAsync()
    {
        description = await CreateDescriptionAsync(
            selectInstance.Description(),
            Array.Empty<MinamoObject>()).ConfigureAwait(false);

        await EnterCurrentStateAsync().ConfigureAwait(false);

        selectInstance.CompleteIfIdle();
        await PublishSnapshotAsync().ConfigureAwait(false);
    }

    internal string Name => CurrentSnapshot.Name;

    internal long Revision => CurrentSnapshot.Revision;

    internal MinamoSelectSnapshot Snapshot => CurrentSnapshot;

    internal string State => CurrentSnapshot.State.Id;

    internal MinamoSelectDescription? Description => CurrentSnapshot.Description;

    internal IReadOnlyList<MinamoChoice> Choices => CurrentSnapshot.Choices;

    internal bool IsCompleted => CurrentSnapshot.IsCompleted;

    internal MinamoObject CompletionValue => selectInstance.Value;

    private MinamoSelectSnapshot CurrentSnapshot => snapshot;

    private MinamoSelectSnapshot CreateInitialSnapshot() => new(
        selectInstance.Name,
        revision.Current,
        new MinamoSelectState(selectInstance.State.Name),
        description,
        Array.Empty<MinamoChoice>(),
        selectInstance.IsCompleted);

    private void PublishCompletedSnapshot()
    {
        availableChoices = Array.Empty<ResolvedSelectChoice>();
        snapshot = new(
            selectInstance.Name,
            revision.Current,
            new MinamoSelectState(selectInstance.State.Name),
            description,
            Array.Empty<MinamoChoice>(),
            isCompleted: true);
    }

    private MinamoSelectSnapshot CreateSnapshot(
        IReadOnlyList<MinamoChoice> choices) =>
        new(
            selectInstance.Name,
            revision.Current,
            new MinamoSelectState(selectInstance.State.Name),
            description,
            choices,
            selectInstance.IsCompleted);

    internal Task<MinamoSelectSnapshot> RefreshAsync() => RefreshCoreAsync(invalidate: false);

    internal Task<MinamoSelectSnapshot> InvalidateAsync() => RefreshCoreAsync(invalidate: true);

    internal Task<MinamoSelectResult> SelectAsync(string choiceId) =>
        SelectCoreAsync(choiceId, null, hasArgument: false, expectedRevision: null);

    internal Task<MinamoSelectResult> SelectAsync(string choiceId, object? argument) =>
        SelectCoreAsync(choiceId, argument, hasArgument: true, expectedRevision: null);

    internal Task<MinamoSelectResult> SelectAtRevisionAsync(string choiceId, long expectedRevision) =>
        SelectCoreAsync(choiceId, null, hasArgument: false, expectedRevision);

    internal Task<MinamoSelectResult> SelectAtRevisionAsync(
        string choiceId,
        object? argument,
        long expectedRevision) =>
        SelectCoreAsync(choiceId, argument, hasArgument: true, expectedRevision);

    internal Task<MinamoSelectResult> SendAsync(string eventId) =>
        SendCoreAsync(eventId, null, hasArgument: false, expectedRevision: null);

    internal Task<MinamoSelectResult> SendAsync(string eventId, object? argument) =>
        SendCoreAsync(eventId, argument, hasArgument: true, expectedRevision: null);

    internal Task<MinamoSelectResult> SendAtRevisionAsync(string eventId, long expectedRevision) =>
        SendCoreAsync(eventId, null, hasArgument: false, expectedRevision);

    internal Task<MinamoSelectResult> SendAtRevisionAsync(
        string eventId,
        object? argument,
        long expectedRevision) =>
        SendCoreAsync(eventId, argument, hasArgument: true, expectedRevision);

    internal void Cancel()
    {
        actionGate.Wait();
        try
        {
            ThrowIfDisposed();
            expandedSelects.Clear();
            selectInstance.Cancel();
            revision.Advance();
            PublishCompletedSnapshot();
        }
        finally
        {
            actionGate.Release();
        }
    }

    public void Dispose()
    {
        actionGate.Wait();
        try
        {
            if (disposed)
            {
                return;
            }

            expandedSelects.Clear();
            selectInstance.Cancel();
            disposed = true;
        }
        finally
        {
            actionGate.Release();
        }
    }

    private async Task<MinamoSelectSnapshot> RefreshCoreAsync(bool invalidate)
    {
        await actionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (invalidate)
            {
                revision.Advance();
            }

            await PublishSnapshotAsync().ConfigureAwait(false);
            return CurrentSnapshot;
        }
        finally
        {
            actionGate.Release();
        }
    }

    private async Task<MinamoSelectResult> SelectCoreAsync(
        string choiceId,
        object? argument,
        bool hasArgument,
        long? expectedRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(choiceId);
        await actionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureExpectedRevision(expectedRevision);
            if (selectInstance.IsCompleted)
            {
                throw new InvalidOperationException($"Select session '{Name}' has already completed.");
            }

            var choice = availableChoices.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, choiceId, StringComparison.Ordinal));
            if (choice is null)
            {
                throw new ArgumentException(
                    $"Choice '{choiceId}' is not currently available in select state '{selectInstance.State.Name}'.",
                    nameof(choiceId));
            }

            var arguments = AddArguments(
                choice.BoundArguments,
                ConvertArguments(choice, argument, hasArgument));
            var result = await instance.InvokeSelectActionAsync(
                choice.Action ?? throw new InvalidOperationException("The select choice action is unavailable."),
                arguments).ConfigureAwait(false);
            return await ApplyActionExecutionAsync(result).ConfigureAwait(false);
        }
        finally
        {
            actionGate.Release();
        }
    }

    private async Task<MinamoSelectResult> SendCoreAsync(
        string eventId,
        object? argument,
        bool hasArgument,
        long? expectedRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        await actionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureExpectedRevision(expectedRevision);
            if (selectInstance.IsCompleted)
            {
                throw new InvalidOperationException($"Select session '{Name}' has already completed.");
            }

            var handlers = await GetEventHandlersAsync(eventId).ConfigureAwait(false);
            if (handlers.Count == 0)
            {
                throw new ArgumentException(
                    $"Event '{eventId}' is not handled in select state '{selectInstance.State.Name}'.",
                    nameof(eventId));
            }

            MinamoSelectResult? applied = null;
            foreach (var handler in handlers)
            {
                var arguments = ConvertArguments(
                    handler.Handler.Name,
                    handler.Handler.ParameterCount,
                    "Event",
                    argument,
                    hasArgument);
                var result = await instance.InvokeSelectActionAsync(
                    handler.Owner.Event(handler.Handler),
                    arguments).ConfigureAwait(false);
                applied = await ApplyActionExecutionAsync(result).ConfigureAwait(false);
                if (selectInstance.IsCompleted
                    || actionChangedControl)
                {
                    return applied;
                }
            }

            return applied ?? WaitingResult();
        }
        finally
        {
            actionGate.Release();
        }
    }

    private async Task<MinamoSelectResult> ApplyActionExecutionAsync(ExecutionResult result)
    {
        actionChangedControl = false;
        if (result.Reason is not TerminationReason.Complete)
        {
            throw new InvalidOperationException("The select action did not complete successfully.");
        }

        var outcome = selectInstance.Apply(result.Value ?? MinamoNil.Instance);
        actionChangedControl = outcome.LeavingState is not null;
        await ApplyLifecycleHooksAsync(outcome).ConfigureAwait(false);
        selectInstance.CompleteIfIdle();
        revision.Advance();
        await PublishSnapshotAsync().ConfigureAwait(false);
        return selectInstance.IsCompleted
            ? CompletedResult(selectInstance.Value)
            : WaitingResult();
    }

    private async Task ApplyLifecycleHooksAsync(SelectActionOutcome outcome)
    {
        if (outcome.LeavingState is { } leaving)
        {
            await RunExpandedLifecycleHooksAsync(leaving, entering: false).ConfigureAwait(false);
            if (selectInstance.Leave(leaving) is { } leave)
            {
                await RunLifecycleHookAsync(leave).ConfigureAwait(false);
            }
        }

        if (outcome.EnteringState is not null)
        {
            expandedSelects.Clear();
            await EnterCurrentStateAsync().ConfigureAwait(false);
        }
    }

    private async Task RunLifecycleHookAsync(MinamoFunction hook)
    {
        EnsureLifecycleHookResult(
            await instance.InvokeSelectActionAsync(
                hook,
                Array.Empty<MinamoObject>()).ConfigureAwait(false));
    }

    private static void EnsureLifecycleHookResult(ExecutionResult result)
    {
        if (result.Reason is not TerminationReason.Complete)
        {
            throw new InvalidOperationException(
                "A select state lifecycle hook cannot suspend or fail.");
        }

        if (result.Value is MinamoTuple { Count: 2 or 3 } tuple
            && tuple[0] is MinamoString marker
            && (marker.Value == SelectControlSignal.Goto
                || marker.Value == SelectControlSignal.Exit))
        {
            throw new InvalidOperationException(
                "A select state lifecycle hook cannot change select state or exit the select.");
        }
    }

    private MinamoSelectResult WaitingResult() => new(CurrentSnapshot);

    private MinamoSelectResult CompletedResult(MinamoObject value) =>
        new(CurrentSnapshot, value);

    private void EnsureExpectedRevision(long? expectedRevision)
    {
        if (expectedRevision is null || expectedRevision == CurrentSnapshot.Revision)
        {
            return;
        }

        throw new MinamoSelectRevisionMismatchException(
            expectedRevision.Value,
            CurrentSnapshot);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
