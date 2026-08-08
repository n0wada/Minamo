using Spellkit.Compiler;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

public sealed record SpellkitChoiceParameter(string Name, string? TypeName);

public sealed record SpellkitChoice
{
    public SpellkitChoice(
        string id,
        int parameterCount,
        string? label = null,
        string? description = null)
    {
        Id = id;
        ParameterCount = parameterCount;
        Label = label ?? id;
        Description = description;
        Parameters = Array.Empty<SpellkitChoiceParameter>();
    }

    internal SpellkitChoice(
        string id,
        IReadOnlyList<SpellkitChoiceParameter> parameters,
        string? label = null,
        string? description = null)
    {
        Id = id;
        Parameters = parameters.ToArray();
        ParameterCount = Parameters.Count;
        Label = label ?? id;
        Description = description;
    }

    public string Id { get; }

    public string Label { get; }

    public string? Description { get; }

    public int ParameterCount { get; }

    public IReadOnlyList<SpellkitChoiceParameter> Parameters { get; }
}

public sealed class SpellkitSelectResult
{
    private readonly SpellkitObject? value;

    internal SpellkitSelectResult(
        IReadOnlyList<SpellkitChoice> choices,
        bool isCompleted,
        SpellkitObject? value = null)
    {
        Choices = choices;
        IsCompleted = isCompleted;
        this.value = value;
    }

    public IReadOnlyList<SpellkitChoice> Choices { get; }

    public bool IsCompleted { get; }

    internal SpellkitObject Value => value ?? SpellkitNil.Instance;

    public T? GetValue<T>() => SpellkitHostValueConverter.Convert<T>(value, "Select result");

    public bool TryGetValue<T>(out T? result) =>
        SpellkitHostValueConverter.TryConvert(value, out result);
}

public sealed class SpellkitSelectSession : IDisposable
{
    private readonly object syncRoot = new();
    private readonly System.Threading.SemaphoreSlim actionGate = new(1, 1);
    private readonly SpellkitInstance instance;
    private readonly SelectInstance selectInstance;
    private SpellkitSelectSession? nested;
    private SpellkitMachine.VmContinuation? actionContinuation;
    private bool otherwiseRunning;
    private bool disposed;

    internal SpellkitSelectSession(SpellkitInstance instance, SelectInstance selectInstance)
    {
        this.instance = instance;
        this.selectInstance = selectInstance;
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
            lock (syncRoot)
            {
                ThrowIfDisposed();
                return nested?.Choices ?? GetChoices();
            }
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

    public SpellkitSelectResult Select(string choiceId) => SelectCore(choiceId, null, hasArgument: false);

    public SpellkitSelectResult Select(string choiceId, object? argument) => SelectCore(choiceId, argument, hasArgument: true);

    public SpellkitSelectResult Send(string eventId) => SendCore(eventId, null, hasArgument: false);

    public SpellkitSelectResult Send(string eventId, object? argument) => SendCore(eventId, argument, hasArgument: true);

    public Task<SpellkitSelectResult> SelectAsync(string choiceId) =>
        SelectCoreAsync(choiceId, null, hasArgument: false);

    public Task<SpellkitSelectResult> SelectAsync(string choiceId, object? argument) =>
        SelectCoreAsync(choiceId, argument, hasArgument: true);

    public Task<SpellkitSelectResult> SendAsync(string eventId) =>
        SendCoreAsync(eventId, null, hasArgument: false);

    public Task<SpellkitSelectResult> SendAsync(string eventId, object? argument) =>
        SendCoreAsync(eventId, argument, hasArgument: true);

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

    private SpellkitSelectResult SelectCore(string choiceId, object? argument, bool hasArgument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(choiceId);
        actionGate.Wait();
        try
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();
                if (selectInstance.IsCompleted)
                {
                    throw new InvalidOperationException($"Select session '{Name}' has already completed.");
                }

                if (nested is not null)
                {
                    return ResumeNested(choiceId, argument, hasArgument);
                }

                var choice = selectInstance.State.Choices.SingleOrDefault(candidate =>
                    string.Equals(candidate.Name, choiceId, StringComparison.Ordinal));
                if (choice is null)
                {
                    throw new ArgumentException(
                        $"Choice '{choiceId}' is not available in select state '{selectInstance.State.Name}'.",
                        nameof(choiceId));
                }

                if (!IsAvailable(choice))
                {
                    throw new ArgumentException(
                        $"Choice '{choiceId}' is not currently available in select state '{selectInstance.State.Name}'.",
                        nameof(choiceId));
                }

                var arguments = selectInstance.AddStateArguments(
                    ConvertArguments(choice, argument, hasArgument));
                var result = instance.InvokeSelectAction(selectInstance.Choice(choice), arguments);
                return ApplyActionExecution(result);
            }
        }
        finally
        {
            actionGate.Release();
        }
    }

    private SpellkitSelectResult SendCore(string eventId, object? argument, bool hasArgument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        actionGate.Wait();
        try
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();
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
        bool hasArgument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(choiceId);
        await actionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
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

            var choice = selectInstance.State.Choices.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, choiceId, StringComparison.Ordinal));
            if (choice is null || !IsAvailable(choice))
            {
                throw new ArgumentException(
                    $"Choice '{choiceId}' is not currently available in select state '{selectInstance.State.Name}'.",
                    nameof(choiceId));
            }

            var arguments = selectInstance.AddStateArguments(
                ConvertArguments(choice, argument, hasArgument));
            var result = await instance.InvokeSelectActionAsync(
                selectInstance.Choice(choice),
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
        bool hasArgument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        await actionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
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
                nested = instance.CreateSelectSession(suspension.Select);
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
                nested = await instance.CreateSelectSessionAsync(suspension.Select).ConfigureAwait(false);
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
        var choices = nested?.Choices ?? GetChoices();
        return selectInstance.IsCompleted
            ? CompletedResult(selectInstance.Value)
            : new(choices, isCompleted: false);
    }

    private static SpellkitSelectResult CompletedResult(SpellkitObject value) =>
        new(Array.Empty<SpellkitChoice>(), isCompleted: true, value);

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
        selectInstance.State.Choices
            .Where(IsAvailable)
            .Select(choice => new SpellkitChoice(
                choice.Name,
                choice.Parameters
                    .Select(parameter => new SpellkitChoiceParameter(
                        parameter.Name,
                        parameter.TypeName))
                    .ToArray(),
                choice.Label,
                choice.Description))
            .ToArray();

    private bool IsAvailable(SelectChoiceDefinition choice) =>
        selectInstance.Guard(choice) is not { } guard
        || instance.EvaluateSelectGuard(guard, selectInstance.StateArguments);

    private static SpellkitObject[] ConvertArguments(
        SelectChoiceDefinition choice,
        object? argument,
        bool hasArgument) =>
        ConvertArguments(choice.Name, choice.ParameterCount, "Choice", argument, hasArgument);

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
