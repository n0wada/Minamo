using Spellkit.Compiler;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

public sealed record SpellkitChoiceParameter(string Name, string? TypeName);

/// <summary>Display data evaluated by a select state or choice.</summary>
public sealed class SpellkitSelectView
{
    private readonly SpellkitObject value;

    internal SpellkitSelectView(SpellkitObject value) => this.value = value;

    /// <summary>Converts the display data to a host type.</summary>
    public T? GetValue<T>() => SpellkitHostValueConverter.Convert<T>(value, "Select view");

    /// <summary>Attempts to convert the display data to a host type.</summary>
    public bool TryGetValue<T>(out T? result) =>
        SpellkitHostValueConverter.TryConvert(value, out result);
}

/// <summary>The currently active state and its display data.</summary>
public sealed record SpellkitSelectState(string Id, SpellkitSelectView? View);

/// <summary>An immutable UI-facing view of a select session.</summary>
public sealed class SpellkitSelectSnapshot
{
    internal SpellkitSelectSnapshot(
        string name,
        long revision,
        SpellkitSelectState state,
        IReadOnlyList<SpellkitChoice> choices,
        bool isCompleted)
    {
        Name = name;
        Revision = revision;
        State = state;
        Choices = choices;
        IsCompleted = isCompleted;
    }

    public string Name { get; }

    /// <summary>Monotonically increases after a successful select action, cancellation, or invalidation.</summary>
    public long Revision { get; }

    public SpellkitSelectState State { get; }

    public IReadOnlyList<SpellkitChoice> Choices { get; }

    public bool IsCompleted { get; }
}

public sealed record SpellkitChoice
{
    public SpellkitChoice(
        string id,
        int parameterCount,
        string? label = null,
        string? description = null,
        SpellkitSelectView? view = null)
    {
        Id = id;
        ParameterCount = parameterCount;
        Label = label ?? id;
        Description = description;
        View = view;
        Parameters = Array.Empty<SpellkitChoiceParameter>();
    }

    internal SpellkitChoice(
        string id,
        IReadOnlyList<SpellkitChoiceParameter> parameters,
        string? label = null,
        string? description = null,
        SpellkitSelectView? view = null)
    {
        Id = id;
        Parameters = parameters.ToArray();
        ParameterCount = Parameters.Count;
        Label = label ?? id;
        Description = description;
        View = view;
    }

    public string Id { get; }

    public string Label { get; }

    public string? Description { get; }

    public SpellkitSelectView? View { get; }

    public int ParameterCount { get; }

    public IReadOnlyList<SpellkitChoiceParameter> Parameters { get; }
}

public sealed class SpellkitSelectResult
{
    private readonly SpellkitObject? value;

    internal SpellkitSelectResult(
        SpellkitSelectSnapshot snapshot,
        SpellkitObject? value = null)
    {
        Snapshot = snapshot;
        this.value = value;
    }

    public SpellkitSelectSnapshot Snapshot { get; }

    public IReadOnlyList<SpellkitChoice> Choices => Snapshot.Choices;

    public bool IsCompleted => Snapshot.IsCompleted;

    internal SpellkitObject Value => value ?? SpellkitNil.Instance;

    public T? GetValue<T>() => SpellkitHostValueConverter.Convert<T>(value, "Select result");

    public bool TryGetValue<T>(out T? result) =>
        SpellkitHostValueConverter.TryConvert(value, out result);
}

internal sealed class SpellkitSelectRevision
{
    private long value;

    internal long Current => System.Threading.Interlocked.Read(ref value);

    internal void Advance() => System.Threading.Interlocked.Increment(ref value);
}

/// <summary>Thrown when an action was rendered from an older select snapshot.</summary>
public sealed class SpellkitSelectRevisionMismatchException : InvalidOperationException
{
    internal SpellkitSelectRevisionMismatchException(
        long expectedRevision,
        SpellkitSelectSnapshot snapshot)
        : base(
            $"Select revision {expectedRevision} does not match current revision {snapshot.Revision}.")
    {
        ExpectedRevision = expectedRevision;
        Snapshot = snapshot;
    }

    public long ExpectedRevision { get; }

    /// <summary>Gets the current snapshot that supersedes the rejected action.</summary>
    public SpellkitSelectSnapshot Snapshot { get; }
}

public sealed class SpellkitSelectSession : IDisposable
{
    private sealed record ResolvedSelectChoice(
        string Id,
        string Label,
        string? Description,
        IReadOnlyList<SpellkitChoiceParameter> Parameters,
        SpellkitFunction Action,
        SpellkitFunction? Guard,
        SpellkitFunction? View,
        SpellkitObject[] BoundArguments)
    {
        internal int ParameterCount => Parameters.Count;
    }

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
            RunLifecycleHook(enter, selectInstance.StateArguments);
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
            await RunLifecycleHookAsync(enter, selectInstance.StateArguments).ConfigureAwait(false);
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

    /// <summary>Re-evaluates and returns the current UI-facing state without changing its revision.</summary>
    public SpellkitSelectSnapshot Refresh()
    {
        actionGate.Wait();
        try
        {
            return RefreshCore(invalidate: false);
        }
        finally
        {
            actionGate.Release();
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
    /// Invalidates UI operations rendered from the current revision, then returns a refreshed snapshot.
    /// </summary>
    public SpellkitSelectSnapshot Invalidate()
    {
        actionGate.Wait();
        try
        {
            return RefreshCore(invalidate: true);
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

    public SpellkitSelectResult Select(string choiceId) =>
        SelectCore(choiceId, null, hasArgument: false, expectedRevision: null);

    public SpellkitSelectResult Select(string choiceId, object? argument) =>
        SelectCore(choiceId, argument, hasArgument: true, expectedRevision: null);

    public SpellkitSelectResult SelectAtRevision(string choiceId, long expectedRevision) =>
        SelectCore(choiceId, null, hasArgument: false, expectedRevision);

    public SpellkitSelectResult SelectAtRevision(
        string choiceId,
        object? argument,
        long expectedRevision) =>
        SelectCore(choiceId, argument, hasArgument: true, expectedRevision);

    public SpellkitSelectResult Send(string eventId) =>
        SendCore(eventId, null, hasArgument: false, expectedRevision: null);

    public SpellkitSelectResult Send(string eventId, object? argument) =>
        SendCore(eventId, argument, hasArgument: true, expectedRevision: null);

    public SpellkitSelectResult SendAtRevision(string eventId, long expectedRevision) =>
        SendCore(eventId, null, hasArgument: false, expectedRevision);

    public SpellkitSelectResult SendAtRevision(
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

                var arguments = selectInstance.AddStateArguments(
                    ConvertArguments(
                        handler.Name,
                        handler.ParameterCount,
                        "Event",
                        argument,
                        hasArgument));
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

            var arguments = selectInstance.AddStateArguments(
                ConvertArguments(
                    handler.Name,
                    handler.ParameterCount,
                    "Event",
                    argument,
                    hasArgument));
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
            ? nested!.Select(choiceId, argument)
            : nested!.Select(choiceId);
        if (!nestedResult.IsCompleted)
        {
            return nestedResult;
        }

        return ResumeCompletedNested();
    }

    private SpellkitSelectResult ResumeNestedEvent(string eventId, object? argument, bool hasArgument)
    {
        var nestedResult = hasArgument
            ? nested!.Send(eventId, argument)
            : nested!.Send(eventId);
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
            RunLifecycleHook(
                leave,
                outcome.LeavingStateArguments ?? Array.Empty<SpellkitObject>());
        }

        if (outcome.EnteringState is { } entering
            && !selectInstance.IsCompleted
            && selectInstance.Enter(entering) is { } enter)
        {
            RunLifecycleHook(enter, selectInstance.StateArguments);
        }
    }

    private async Task ApplyLifecycleHooksAsync(SelectActionOutcome outcome)
    {
        if (outcome.LeavingState is { } leaving
            && selectInstance.Leave(leaving) is { } leave)
        {
            await RunLifecycleHookAsync(
                leave,
                outcome.LeavingStateArguments ?? Array.Empty<SpellkitObject>()).ConfigureAwait(false);
        }

        if (outcome.EnteringState is { } entering
            && !selectInstance.IsCompleted
            && selectInstance.Enter(entering) is { } enter)
        {
            await RunLifecycleHookAsync(enter, selectInstance.StateArguments).ConfigureAwait(false);
        }
    }

    private void RunLifecycleHook(
        SpellkitFunction hook,
        SpellkitObject[] arguments)
    {
        EnsureLifecycleHookResult(instance.InvokeSelectAction(hook, arguments));
    }

    private async Task RunLifecycleHookAsync(
        SpellkitFunction hook,
        SpellkitObject[] arguments)
    {
        EnsureLifecycleHookResult(
            await instance.InvokeSelectActionAsync(
                hook,
                arguments).ConfigureAwait(false));
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

    private SpellkitSelectResult WaitingResult()
    {
        return selectInstance.IsCompleted
            ? CompletedResult(selectInstance.Value)
            : new(GetSnapshot());
    }

    private SpellkitSelectResult CompletedResult(SpellkitObject value) =>
        new(GetSnapshot(), value);

    private SpellkitSelectSnapshot GetSnapshot()
    {
        if (nested is not null)
        {
            return nested.Snapshot;
        }

        if (selectInstance.IsCompleted)
        {
            return CreateSnapshot(Array.Empty<SpellkitChoice>());
        }

        var choices = GetChoices();
        if (nested is not null)
        {
            return nested.Snapshot;
        }

        return CreateSnapshot(
            selectInstance.IsCompleted ? Array.Empty<SpellkitChoice>() : choices);
    }

    private SpellkitSelectSnapshot RefreshCore(bool invalidate)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (invalidate)
            {
                revision.Advance();
            }

            return GetSnapshot();
        }
    }

    private SpellkitSelectSnapshot CreateSnapshot(IReadOnlyList<SpellkitChoice> choices)
    {
        var state = selectInstance.State;
        var stateView = selectInstance.IsCompleted
            ? null
            : CreateView(selectInstance.View(state));
        return new(
            selectInstance.Name,
            revision.Current,
            new SpellkitSelectState(state.Name, stateView),
            choices,
            selectInstance.IsCompleted);
    }

    private IReadOnlyList<SpellkitChoice> GetChoices()
    {
        if (selectInstance.IsCompleted)
        {
            return Array.Empty<SpellkitChoice>();
        }

        var choices = GetVisibleChoices();
        if (choices.Count == 0
            && selectInstance.ShouldRunOtherwise
            && !otherwiseRunning)
        {
            selectInstance.MarkOtherwiseTriggered();
            otherwiseRunning = true;
            try
            {
                var otherwise = selectInstance.Otherwise()
                    ?? throw new InvalidOperationException("The select otherwise handler is unavailable.");
                var result = instance.InvokeSelectAction(
                    otherwise,
                    selectInstance.AddStateArguments(Array.Empty<SpellkitObject>()));
                return ApplyActionExecution(result).Choices;
            }
            finally
            {
                otherwiseRunning = false;
            }
        }

        return choices;
    }

    private IReadOnlyList<SpellkitChoice> GetVisibleChoices() =>
        GetAvailableChoices()
            .Select(choice => new SpellkitChoice(
                choice.Id,
                choice.Parameters,
                choice.Label,
                choice.Description,
                CreateView(choice.View, choice.BoundArguments)))
            .ToArray();

    private IReadOnlyList<ResolvedSelectChoice> GetAvailableChoices()
    {
        var candidates = new List<ResolvedSelectChoice>();
        foreach (var choice in selectInstance.State.Choices)
        {
            candidates.Add(new(
                choice.Name,
                choice.Label,
                choice.Description,
                choice.Parameters
                    .Select(parameter => new SpellkitChoiceParameter(
                        parameter.Name,
                        parameter.TypeName))
                    .ToArray(),
                selectInstance.Choice(choice),
                selectInstance.Guard(choice),
                selectInstance.View(choice),
                selectInstance.StateArguments));
        }

        foreach (var group in selectInstance.State.DynamicChoices)
        {
            var source = instance.EvaluateSelectDynamicChoice(
                selectInstance.DynamicChoiceSource(group),
                selectInstance.StateArguments);
            if (source is not IEnumerable<SpellkitObject> items)
            {
                throw new InvalidOperationException(
                    $"The dynamic choices in select state '{selectInstance.State.Name}' must be a collection.");
            }

            foreach (var item in items)
            {
                var arguments = selectInstance.AddStateArguments([item]);
                foreach (var template in group.Choices)
                {
                    var id = RequireDynamicChoiceText(
                        selectInstance.DynamicChoiceId(template),
                        arguments,
                        "ID");
                    var label = selectInstance.DynamicChoiceLabel(template) is { } labelFunction
                        ? EvaluateDynamicChoiceText(labelFunction, arguments, "label") ?? id
                        : id;
                    var description = selectInstance.DynamicChoiceDescription(template) is { } descriptionFunction
                        ? EvaluateDynamicChoiceText(descriptionFunction, arguments, "description")
                        : null;
                    candidates.Add(new(
                        id,
                        label,
                        description,
                        Array.Empty<SpellkitChoiceParameter>(),
                        selectInstance.DynamicChoiceAction(template),
                        selectInstance.DynamicChoiceGuard(template),
                        selectInstance.DynamicChoiceView(template),
                        arguments));
                }
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!ids.Add(candidate.Id))
            {
                throw new InvalidOperationException(
                    $"The select state '{selectInstance.State.Name}' generated duplicate choice ID '{candidate.Id}'.");
            }
        }

        return candidates.Where(IsAvailable).ToArray();
    }

    private string RequireDynamicChoiceText(
        SpellkitFunction function,
        SpellkitObject[] arguments,
        string part)
    {
        var value = EvaluateDynamicChoiceText(function, arguments, part);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"A dynamic select choice {part} in state '{selectInstance.State.Name}' cannot be empty.");
        }

        return value;
    }

    private string? EvaluateDynamicChoiceText(
        SpellkitFunction function,
        SpellkitObject[] arguments,
        string part) =>
        SpellkitHostValueConverter.Convert<string>(
            instance.EvaluateSelectDynamicChoice(function, arguments),
            $"Dynamic select choice {part}");

    private SpellkitSelectView? CreateView(SpellkitFunction? view) =>
        CreateView(view, selectInstance.StateArguments);

    private SpellkitSelectView? CreateView(
        SpellkitFunction? view,
        SpellkitObject[] arguments) =>
        view is null
            ? null
            : new SpellkitSelectView(
                instance.EvaluateSelectView(view, arguments));

    private bool IsAvailable(ResolvedSelectChoice choice) =>
        choice.Guard is null
        || instance.EvaluateSelectGuard(choice.Guard, choice.BoundArguments);

    private static SpellkitObject[] ConvertArguments(
        ResolvedSelectChoice choice,
        object? argument,
        bool hasArgument) =>
        ConvertArguments(choice.Id, choice.ParameterCount, "Choice", argument, hasArgument);

    private static SpellkitObject[] AddArguments(
        IReadOnlyList<SpellkitObject> boundArguments,
        IReadOnlyList<SpellkitObject> arguments)
    {
        var result = new SpellkitObject[boundArguments.Count + arguments.Count];
        for (var i = 0; i < boundArguments.Count; i++)
        {
            result[i] = boundArguments[i];
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            result[boundArguments.Count + i] = arguments[i];
        }

        return result;
    }

    private static SpellkitObject[] ConvertArguments(
        string name,
        int parameterCount,
        string actionKind,
        object? argument,
        bool hasArgument)
    {
        if (parameterCount == 0)
        {
            if (hasArgument)
            {
                throw new ArgumentException($"{actionKind} '{name}' does not accept an argument.", nameof(argument));
            }

            return Array.Empty<SpellkitObject>();
        }

        if (!hasArgument)
        {
            throw new ArgumentException($"{actionKind} '{name}' requires an argument.", nameof(argument));
        }

        if (parameterCount == 1)
        {
            return [TypeConverter.ConvertFrom(argument)];
        }

        if (argument is not ITuple tuple || tuple.Length != parameterCount)
        {
            throw new ArgumentException(
                $"{actionKind} '{name}' requires one tuple with {parameterCount} elements.",
                nameof(argument));
        }

        var values = new SpellkitObject[tuple.Length];
        for (var i = 0; i < tuple.Length; i++)
        {
            values[i] = TypeConverter.ConvertFrom(tuple[i]);
        }
        return values;
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

public sealed class SpellkitRunSession : IDisposable
{
    private readonly SpellkitInstance instance;
    private SpellkitSelectSession? select;
    private SpellkitMachine.VmContinuation? continuation;
    private SpellkitObject? value;
    private Exception? failure;
    private bool completed;
    private bool disposed;

    internal SpellkitRunSession(SpellkitInstance instance, ExecutionResult result) =>
        (this.instance, continuation) = (instance, result.Continuation);

    internal SpellkitRunSession(SpellkitInstance instance, Exception failure) =>
        (this.instance, this.failure, completed) = (instance, failure, true);

    public bool IsCompleted => completed;

    public bool IsWaitingForSelect => select is not null && !completed;

    public Exception? Failure => failure;

    public IReadOnlyList<SpellkitChoice> Choices
    {
        get
        {
            ThrowIfDisposed();
            return select?.Choices ?? Array.Empty<SpellkitChoice>();
        }
    }

    public SpellkitSelectResult Select(string choiceId) =>
        instance.Select(this, choiceId, null, hasArgument: false);

    public SpellkitSelectResult Select(string choiceId, object? argument) =>
        instance.Select(this, choiceId, argument, hasArgument: true);

    public SpellkitSelectResult Send(string eventId) =>
        instance.Send(this, eventId, null, hasArgument: false);

    public SpellkitSelectResult Send(string eventId, object? argument) =>
        instance.Send(this, eventId, argument, hasArgument: true);

    public Task<SpellkitSelectResult> SelectAsync(string choiceId) =>
        instance.SelectAsync(this, choiceId, null, hasArgument: false);

    public Task<SpellkitSelectResult> SelectAsync(string choiceId, object? argument) =>
        instance.SelectAsync(this, choiceId, argument, hasArgument: true);

    public Task<SpellkitSelectResult> SendAsync(string eventId) =>
        instance.SendAsync(this, eventId, null, hasArgument: false);

    public Task<SpellkitSelectResult> SendAsync(string eventId, object? argument) =>
        instance.SendAsync(this, eventId, argument, hasArgument: true);

    public T? GetValue<T>() => SpellkitHostValueConverter.Convert<T>(value, "Run result");

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        instance.Cancel(this);
        disposed = true;
    }

    internal SpellkitSelectSession GetSelect()
    {
        ThrowIfDisposed();
        return select ?? throw new InvalidOperationException("The script is not waiting for a select.");
    }

    internal SpellkitMachine.VmContinuation GetContinuation() =>
        continuation ?? throw new InvalidOperationException("The script has no suspended VM continuation.");

    internal void Advance(ExecutionResult result)
    {
        select?.Dispose();
        select = null;

        while (true)
        {
            if (result.Reason is TerminationReason.Complete)
            {
                completed = true;
                continuation = null;
                value = result.Value;
                return;
            }

            if (result.Reason is TerminationReason.Suspended
                && result.Continuation is not null
                && result.Suspension is { Select: not null } suspension)
            {
                continuation = result.Continuation;
                select = instance.CreateSelectSession(suspension.Select);
                if (!select.IsCompleted)
                {
                    return;
                }

                var selectValue = select.CompletionValue;
                select.Dispose();
                select = null;
                result = instance.ResumeSelectContinuation(continuation, selectValue);
                continue;
            }

            throw new InvalidOperationException("The VM suspended without a select request.");
        }
    }

    internal async Task AdvanceAsync(ExecutionResult result)
    {
        select?.Dispose();
        select = null;

        while (true)
        {
            if (result.Reason is TerminationReason.Complete)
            {
                completed = true;
                continuation = null;
                value = result.Value;
                return;
            }

            if (result.Reason is TerminationReason.Suspended
                && result.Continuation is not null
                && result.Suspension is { Select: not null } suspension)
            {
                continuation = result.Continuation;
                select = await instance.CreateSelectSessionAsync(
                    suspension.Select).ConfigureAwait(false);
                if (!select.IsCompleted)
                {
                    return;
                }

                var selectValue = select.CompletionValue;
                select.Dispose();
                select = null;
                result = await instance.ResumeSelectContinuationAsync(
                    continuation,
                    selectValue).ConfigureAwait(false);
                continue;
            }

            throw new InvalidOperationException("The VM suspended without a select request.");
        }
    }

    internal void Fail(Exception exception)
    {
        failure = exception;
        completed = true;
        continuation = null;
        select?.Dispose();
        select = null;
    }

    internal void Cancel()
    {
        completed = true;
        continuation = null;
        select?.Dispose();
        select = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
