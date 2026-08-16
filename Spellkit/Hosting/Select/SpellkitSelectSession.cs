using Spellkit.Compiler;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

internal sealed partial class SpellkitSelectSession : IDisposable
{
    private readonly System.Threading.SemaphoreSlim actionGate = new(1, 1);
    private readonly SpellkitInstance instance;
    private readonly SelectInstance selectInstance;
    private readonly SpellkitSelectRevision revision;
    private SpellkitSelectSnapshot snapshot;
    private IReadOnlyList<ResolvedSelectChoice> availableChoices = Array.Empty<ResolvedSelectChoice>();
    private SpellkitSelectSession? nested;
    private SpellkitMachine.VmContinuation? actionContinuation;
    private bool otherwiseRunning;
    private bool disposed;

    internal SpellkitSelectSession(
        SpellkitInstance instance,
        SelectInstance selectInstance,
        SpellkitSelectRevision? revision = null)
    {
        this.instance = instance;
        this.selectInstance = selectInstance;
        this.revision = revision ?? new SpellkitSelectRevision();
        snapshot = CreateInitialSnapshot();
    }

    internal async Task InitializeAsync()
    {
        if (!selectInstance.IsCompleted
            && selectInstance.Enter(selectInstance.State) is { } enter)
        {
            await RunLifecycleHookAsync(enter).ConfigureAwait(false);
        }

        selectInstance.CompleteIfIdle();
        await PublishSnapshotAsync().ConfigureAwait(false);
    }

    internal string Name => CurrentSnapshot.Name;

    internal long Revision => CurrentSnapshot.Revision;

    internal SpellkitSelectSnapshot Snapshot => CurrentSnapshot;

    internal string State => CurrentSnapshot.State.Id;

    internal SpellkitSelectView? StateView => CurrentSnapshot.State.View;

    internal IReadOnlyList<SpellkitChoice> Choices => CurrentSnapshot.Choices;

    internal bool IsCompleted => CurrentSnapshot.IsCompleted;

    internal SpellkitObject CompletionValue => selectInstance.Value;

    private SpellkitSelectSnapshot CurrentSnapshot => nested?.CurrentSnapshot ?? snapshot;

    private SpellkitSelectSnapshot CreateInitialSnapshot() => new(
        selectInstance.Name,
        revision.Current,
        new SpellkitSelectState(selectInstance.State.Name, null),
        Array.Empty<SpellkitChoice>(),
        selectInstance.IsCompleted);

    private void PublishCompletedSnapshot()
    {
        availableChoices = Array.Empty<ResolvedSelectChoice>();
        snapshot = new(
            selectInstance.Name,
            revision.Current,
            new SpellkitSelectState(selectInstance.State.Name, null),
            Array.Empty<SpellkitChoice>(),
            isCompleted: true);
    }

    private SpellkitSelectSnapshot CreateSnapshot(
        SpellkitSelectView? stateView,
        IReadOnlyList<SpellkitChoice> choices) =>
        new(
            selectInstance.Name,
            revision.Current,
            new SpellkitSelectState(selectInstance.State.Name, stateView),
            choices,
            selectInstance.IsCompleted);

    internal Task<SpellkitSelectSnapshot> RefreshAsync() => RefreshCoreAsync(invalidate: false);

    internal Task<SpellkitSelectSnapshot> InvalidateAsync() => RefreshCoreAsync(invalidate: true);

    internal Task<SpellkitSelectResult> SelectAsync(string choiceId) =>
        SelectCoreAsync(choiceId, null, hasArgument: false, expectedRevision: null);

    internal Task<SpellkitSelectResult> SelectAsync(string choiceId, object? argument) =>
        SelectCoreAsync(choiceId, argument, hasArgument: true, expectedRevision: null);

    internal Task<SpellkitSelectResult> SelectAtRevisionAsync(string choiceId, long expectedRevision) =>
        SelectCoreAsync(choiceId, null, hasArgument: false, expectedRevision);

    internal Task<SpellkitSelectResult> SelectAtRevisionAsync(
        string choiceId,
        object? argument,
        long expectedRevision) =>
        SelectCoreAsync(choiceId, argument, hasArgument: true, expectedRevision);

    internal Task<SpellkitSelectResult> SendAsync(string eventId) =>
        SendCoreAsync(eventId, null, hasArgument: false, expectedRevision: null);

    internal Task<SpellkitSelectResult> SendAsync(string eventId, object? argument) =>
        SendCoreAsync(eventId, argument, hasArgument: true, expectedRevision: null);

    internal Task<SpellkitSelectResult> SendAtRevisionAsync(string eventId, long expectedRevision) =>
        SendCoreAsync(eventId, null, hasArgument: false, expectedRevision);

    internal Task<SpellkitSelectResult> SendAtRevisionAsync(
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
            nested?.Cancel();
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

            nested?.Dispose();
            nested = null;
            actionContinuation = null;
            selectInstance.Cancel();
            disposed = true;
        }
        finally
        {
            actionGate.Release();
        }
    }

    private async Task<SpellkitSelectSnapshot> RefreshCoreAsync(bool invalidate)
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

    private async Task<SpellkitSelectResult> SelectCoreAsync(
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

            if (nested is not null)
            {
                var nestedResult = expectedRevision is { } revision
                    ? hasArgument
                        ? await nested.SelectAtRevisionAsync(choiceId, argument, revision).ConfigureAwait(false)
                        : await nested.SelectAtRevisionAsync(choiceId, revision).ConfigureAwait(false)
                    : hasArgument
                        ? await nested.SelectAsync(choiceId, argument).ConfigureAwait(false)
                        : await nested.SelectAsync(choiceId).ConfigureAwait(false);
                return nestedResult.IsCompleted
                    ? await ResumeCompletedNestedAsync().ConfigureAwait(false)
                    : nestedResult;
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
                choice.Action,
                arguments).ConfigureAwait(false);
            return await ApplyActionExecutionAsync(result).ConfigureAwait(false);
        }
        finally
        {
            actionGate.Release();
        }
    }

    private async Task<SpellkitSelectResult> SendCoreAsync(
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

            if (nested is not null)
            {
                var nestedResult = expectedRevision is { } revision
                    ? hasArgument
                        ? await nested.SendAtRevisionAsync(eventId, argument, revision).ConfigureAwait(false)
                        : await nested.SendAtRevisionAsync(eventId, revision).ConfigureAwait(false)
                    : hasArgument
                        ? await nested.SendAsync(eventId, argument).ConfigureAwait(false)
                        : await nested.SendAsync(eventId).ConfigureAwait(false);
                return nestedResult.IsCompleted
                    ? await ResumeCompletedNestedAsync().ConfigureAwait(false)
                    : nestedResult;
            }

            var handler = selectInstance.State.Events.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, eventId, StringComparison.Ordinal));
            if (handler is null)
            {
                throw new ArgumentException(
                    $"Event '{eventId}' is not handled in select state '{selectInstance.State.Name}'.",
                    nameof(eventId));
            }

            var arguments = ConvertArguments(
                handler.Name,
                handler.ParameterCount,
                "Event",
                argument,
                hasArgument);
            var result = await instance.InvokeSelectActionAsync(
                selectInstance.Event(handler),
                arguments).ConfigureAwait(false);
            return await ApplyActionExecutionAsync(result).ConfigureAwait(false);
        }
        finally
        {
            actionGate.Release();
        }
    }

    private async Task<SpellkitSelectResult> ResumeCompletedNestedAsync()
    {
        var completedNested = nested
            ?? throw new InvalidOperationException("The nested select is unavailable.");
        var value = completedNested.CompletionValue;
        completedNested.Dispose();
        nested = null;
        var continuation = actionContinuation
            ?? throw new InvalidOperationException("The nested select has no parent continuation.");
        actionContinuation = null;
        return await ApplyActionExecutionAsync(
            await instance.ResumeSelectContinuationAsync(
                continuation,
                value).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async Task<SpellkitSelectResult> ApplyActionExecutionAsync(ExecutionResult result)
    {
        while (true)
        {
            if (result.Reason is TerminationReason.Suspended)
            {
                if (result.Continuation is null
                    || result.Suspension is not { Select: not null } suspension)
                {
                    throw new InvalidOperationException("A select action suspended without a select request.");
                }

                actionContinuation = result.Continuation;
                revision.Advance();
                nested = await instance.CreateSelectSessionAsync(
                    suspension.Select,
                    revision).ConfigureAwait(false);
                if (!nested.IsCompleted)
                {
                    return WaitingResult();
                }

                var value = nested.CompletionValue;
                nested.Dispose();
                nested = null;
                actionContinuation = null;
                result = await instance.ResumeSelectContinuationAsync(
                    result.Continuation,
                    value).ConfigureAwait(false);
                continue;
            }

            if (result.Reason is not TerminationReason.Complete)
            {
                throw new InvalidOperationException("The select action did not complete successfully.");
            }

            var outcome = selectInstance.Apply(result.Value ?? SpellkitNil.Instance);
            await ApplyLifecycleHooksAsync(outcome).ConfigureAwait(false);
            selectInstance.CompleteIfIdle();
            revision.Advance();
            await PublishSnapshotAsync().ConfigureAwait(false);
            return selectInstance.IsCompleted
                ? CompletedResult(selectInstance.Value)
                : WaitingResult();
        }
    }

    private async Task ApplyLifecycleHooksAsync(SelectActionOutcome outcome)
    {
        if (outcome.LeavingState is { } leaving
            && selectInstance.Leave(leaving) is { } leave)
        {
            await RunLifecycleHookAsync(leave).ConfigureAwait(false);
        }

        if (outcome.EnteringState is { } entering
            && !selectInstance.IsCompleted
            && selectInstance.Enter(entering) is { } enter)
        {
            await RunLifecycleHookAsync(enter).ConfigureAwait(false);
        }
    }

    private async Task RunLifecycleHookAsync(SpellkitFunction hook)
    {
        EnsureLifecycleHookResult(
            await instance.InvokeSelectActionAsync(
                hook,
                Array.Empty<SpellkitObject>()).ConfigureAwait(false));
    }

    private static void EnsureLifecycleHookResult(ExecutionResult result)
    {
        if (result.Reason is not TerminationReason.Complete)
        {
            throw new InvalidOperationException(
                "A select state lifecycle hook cannot suspend or fail.");
        }

        if (result.Value is SpellkitTuple { Count: 2 or 3 } tuple
            && tuple[0] is SpellkitString marker
            && (marker.Value == SelectControlSignal.Goto
                || marker.Value == SelectControlSignal.Exit))
        {
            throw new InvalidOperationException(
                "A select state lifecycle hook cannot change select state or exit the select.");
        }
    }

    private SpellkitSelectResult WaitingResult() => new(CurrentSnapshot);

    private SpellkitSelectResult CompletedResult(SpellkitObject value) =>
        new(CurrentSnapshot, value);

    private void EnsureExpectedRevision(long? expectedRevision)
    {
        if (expectedRevision is null || expectedRevision == CurrentSnapshot.Revision)
        {
            return;
        }

        throw new SpellkitSelectRevisionMismatchException(
            expectedRevision.Value,
            CurrentSnapshot);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
