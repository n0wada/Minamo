using Spellkit.Compiler;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

public sealed partial class SpellkitSelectSession : IDisposable
{
    private readonly object syncRoot = new();
    private readonly System.Threading.SemaphoreSlim actionGate = new(1, 1);
    private readonly SpellkitInstance instance;
    private readonly SelectInstance selectInstance;
    private readonly SpellkitSelectRevision revision;
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
    }

    internal void Initialize()
    {
        if (selectInstance.IsCompleted)
        {
            return;
        }

        if (selectInstance.Enter(selectInstance.State) is { } enter)
        {
            RunLifecycleHook(enter);
        }

        selectInstance.CompleteIfIdle();
        if (!selectInstance.IsCompleted)
        {
            _ = GetChoices();
        }
    }

    internal async Task InitializeAsync()
    {
        if (selectInstance.IsCompleted)
        {
            return;
        }

        if (selectInstance.Enter(selectInstance.State) is { } enter)
        {
            await RunLifecycleHookAsync(enter).ConfigureAwait(false);
        }

        selectInstance.CompleteIfIdle();
        if (!selectInstance.IsCompleted)
        {
            _ = GetChoices();
        }
    }

    public string Name => selectInstance.Name;

    public long Revision => revision.Current;

    /// <summary>Gets the current UI-facing state of this select.</summary>
    public SpellkitSelectSnapshot Snapshot
    {
        get
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();
                return GetSnapshot();
            }
        }
    }

    /// <summary>Gets the name of the current state.</summary>
    public string State
    {
        get
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();
                return selectInstance.State.Name;
            }
        }
    }

    public IReadOnlyList<SpellkitChoice> Choices
    {
        get
        {
            return Snapshot.Choices;
        }
    }

    /// <summary>Asynchronously re-evaluates and returns the current UI-facing state without changing its revision.</summary>
    public async Task<SpellkitSelectSnapshot> RefreshAsync()
    {
        await actionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return RefreshCore(invalidate: false);
        }
        finally
        {
            actionGate.Release();
        }
    }

    /// <summary>
    /// Asynchronously invalidates UI operations rendered from the current revision, then returns a refreshed snapshot.
    /// </summary>
    public async Task<SpellkitSelectSnapshot> InvalidateAsync()
    {
        await actionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return RefreshCore(invalidate: true);
        }
        finally
        {
            actionGate.Release();
        }
    }

    public bool IsCompleted
    {
        get
        {
            lock (syncRoot)
            {
                return selectInstance.IsCompleted;
            }
        }
    }

    internal SpellkitObject CompletionValue => selectInstance.Value;

    internal SpellkitSelectResult SelectSynchronously(string choiceId) =>
        SelectCore(choiceId, null, hasArgument: false, expectedRevision: null);

    internal SpellkitSelectResult SelectSynchronously(string choiceId, object? argument) =>
        SelectCore(choiceId, argument, hasArgument: true, expectedRevision: null);

    internal SpellkitSelectResult SelectAtRevisionSynchronously(string choiceId, long expectedRevision) =>
        SelectCore(choiceId, null, hasArgument: false, expectedRevision);

    internal SpellkitSelectResult SelectAtRevisionSynchronously(
        string choiceId,
        object? argument,
        long expectedRevision) =>
        SelectCore(choiceId, argument, hasArgument: true, expectedRevision);

    internal SpellkitSelectResult SendSynchronously(string eventId) =>
        SendCore(eventId, null, hasArgument: false, expectedRevision: null);

    internal SpellkitSelectResult SendSynchronously(string eventId, object? argument) =>
        SendCore(eventId, argument, hasArgument: true, expectedRevision: null);

    internal SpellkitSelectResult SendAtRevisionSynchronously(string eventId, long expectedRevision) =>
        SendCore(eventId, null, hasArgument: false, expectedRevision);

    internal SpellkitSelectResult SendAtRevisionSynchronously(
        string eventId,
        object? argument,
        long expectedRevision) =>
        SendCore(eventId, argument, hasArgument: true, expectedRevision);

    public Task<SpellkitSelectResult> SelectAsync(string choiceId) =>
        SelectCoreAsync(choiceId, null, hasArgument: false, expectedRevision: null);

    public Task<SpellkitSelectResult> SelectAsync(string choiceId, object? argument) =>
        SelectCoreAsync(choiceId, argument, hasArgument: true, expectedRevision: null);

    public Task<SpellkitSelectResult> SelectAtRevisionAsync(string choiceId, long expectedRevision) =>
        SelectCoreAsync(choiceId, null, hasArgument: false, expectedRevision);

    public Task<SpellkitSelectResult> SelectAtRevisionAsync(
        string choiceId,
        object? argument,
        long expectedRevision) =>
        SelectCoreAsync(choiceId, argument, hasArgument: true, expectedRevision);

    public Task<SpellkitSelectResult> SendAsync(string eventId) =>
        SendCoreAsync(eventId, null, hasArgument: false, expectedRevision: null);

    public Task<SpellkitSelectResult> SendAsync(string eventId, object? argument) =>
        SendCoreAsync(eventId, argument, hasArgument: true, expectedRevision: null);

    public Task<SpellkitSelectResult> SendAtRevisionAsync(string eventId, long expectedRevision) =>
        SendCoreAsync(eventId, null, hasArgument: false, expectedRevision);

    public Task<SpellkitSelectResult> SendAtRevisionAsync(
        string eventId,
        object? argument,
        long expectedRevision) =>
        SendCoreAsync(eventId, argument, hasArgument: true, expectedRevision);

    public void Cancel()
    {
        actionGate.Wait();
        try
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();
                nested?.Cancel();
                selectInstance.Cancel();
                revision.Advance();
            }
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
            lock (syncRoot)
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
        }
        finally
        {
            actionGate.Release();
        }
    }

    private SpellkitSelectResult SelectCore(
        string choiceId,
        object? argument,
        bool hasArgument,
        long? expectedRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(choiceId);
        actionGate.Wait();
        try
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();
                EnsureExpectedRevision(expectedRevision);
                if (selectInstance.IsCompleted)
                {
                    throw new InvalidOperationException($"Select session '{Name}' has already completed.");
                }

                if (nested is not null)
                {
                    return ResumeNested(choiceId, argument, hasArgument);
                }

                var choice = GetAvailableChoices().SingleOrDefault(candidate =>
                    string.Equals(candidate.Id, choiceId, StringComparison.Ordinal));
                if (choice is null)
                {
                    throw new ArgumentException(
                        $"Choice '{choiceId}' is not available in select state '{selectInstance.State.Name}'.",
                        nameof(choiceId));
                }

                var arguments = AddArguments(
                    choice.BoundArguments,
                    ConvertArguments(choice, argument, hasArgument));
                var result = instance.InvokeSelectAction(choice.Action, arguments);
                return ApplyActionExecution(result);
            }
        }
        finally
        {
            actionGate.Release();
        }
    }

    private SpellkitSelectResult SendCore(
        string eventId,
        object? argument,
        bool hasArgument,
        long? expectedRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        actionGate.Wait();
        try
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();
                EnsureExpectedRevision(expectedRevision);
                if (selectInstance.IsCompleted)
                {
                    throw new InvalidOperationException($"Select session '{Name}' has already completed.");
                }

                if (nested is not null)
                {
                    return ResumeNestedEvent(eventId, argument, hasArgument);
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
                var result = instance.InvokeSelectAction(selectInstance.Event(handler), arguments);
                return ApplyActionExecution(result);
            }
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
                var nestedResult = hasArgument
                    ? await nested.SelectAsync(choiceId, argument).ConfigureAwait(false)
                    : await nested.SelectAsync(choiceId).ConfigureAwait(false);
                return nestedResult.IsCompleted
                    ? await ResumeCompletedNestedAsync().ConfigureAwait(false)
                    : nestedResult;
            }

            var choice = GetAvailableChoices().SingleOrDefault(candidate =>
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
                var nestedResult = hasArgument
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

    private SpellkitSelectResult ResumeNested(string choiceId, object? argument, bool hasArgument)
    {
        var nestedResult = hasArgument
            ? nested!.SelectSynchronously(choiceId, argument)
            : nested!.SelectSynchronously(choiceId);
        if (!nestedResult.IsCompleted)
        {
            return nestedResult;
        }

        return ResumeCompletedNested();
    }

    private SpellkitSelectResult ResumeNestedEvent(string eventId, object? argument, bool hasArgument)
    {
        var nestedResult = hasArgument
            ? nested!.SendSynchronously(eventId, argument)
            : nested!.SendSynchronously(eventId);
        if (!nestedResult.IsCompleted)
        {
            return nestedResult;
        }

        return ResumeCompletedNested();
    }

    private SpellkitSelectResult ResumeCompletedNested()
    {
        var completedNested = nested
            ?? throw new InvalidOperationException("The nested select is unavailable.");
        var value = completedNested.CompletionValue;
        completedNested.Dispose();
        nested = null;
        var continuation = actionContinuation
            ?? throw new InvalidOperationException("The nested select has no parent continuation.");
        actionContinuation = null;
        return ApplyActionExecution(
            instance.ResumeSelectContinuation(continuation, value));
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

    private SpellkitSelectResult ApplyActionExecution(ExecutionResult result)
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
                nested = instance.CreateSelectSession(suspension.Select, revision);
                revision.Advance();
                if (!nested.IsCompleted)
                {
                    return WaitingResult();
                }

                var value = nested.CompletionValue;
                nested.Dispose();
                nested = null;
                actionContinuation = null;
                result = instance.ResumeSelectContinuation(result.Continuation, value);
                continue;
            }

            if (result.Reason is not TerminationReason.Complete)
            {
                throw new InvalidOperationException("The select action did not complete successfully.");
            }

            var outcome = selectInstance.Apply(result.Value ?? SpellkitNil.Instance);
            ApplyLifecycleHooks(outcome);
            selectInstance.CompleteIfIdle();
            revision.Advance();
            if (outcome.IsCompleted)
            {
                return CompletedResult(outcome.Value);
            }

            return selectInstance.IsCompleted
                ? CompletedResult(selectInstance.Value)
                : WaitingResult();
        }
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
                nested = await instance.CreateSelectSessionAsync(
                    suspension.Select,
                    revision).ConfigureAwait(false);
                revision.Advance();
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
            if (outcome.IsCompleted)
            {
                return CompletedResult(outcome.Value);
            }

            return selectInstance.IsCompleted
                ? CompletedResult(selectInstance.Value)
                : WaitingResult();
        }
    }

    private void ApplyLifecycleHooks(SelectActionOutcome outcome)
    {
        if (outcome.LeavingState is { } leaving
            && selectInstance.Leave(leaving) is { } leave)
        {
            RunLifecycleHook(leave);
        }

        if (outcome.EnteringState is { } entering
            && !selectInstance.IsCompleted
            && selectInstance.Enter(entering) is { } enter)
        {
            RunLifecycleHook(enter);
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

    private void RunLifecycleHook(SpellkitFunction hook)
    {
        EnsureLifecycleHookResult(instance.InvokeSelectAction(hook, Array.Empty<SpellkitObject>()));
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

    private void EnsureExpectedRevision(long? expectedRevision)
    {
        if (expectedRevision is null || expectedRevision == revision.Current)
        {
            return;
        }

        throw new SpellkitSelectRevisionMismatchException(
            expectedRevision.Value,
            GetSnapshot());
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
