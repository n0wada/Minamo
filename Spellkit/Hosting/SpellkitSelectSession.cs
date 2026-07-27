using Spellkit.Compiler;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Spellkit.Hosting;

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
    }

    public string Id { get; }

    public string Label { get; }

    public string? Description { get; }

    public int ParameterCount { get; }
}

public sealed class SpellkitSelectResult
{
    private readonly SpkObject? value;

    internal SpellkitSelectResult(
        IReadOnlyList<SpellkitChoice> choices,
        bool isCompleted,
        SpkObject? value = null)
    {
        Choices = choices;
        IsCompleted = isCompleted;
        this.value = value;
    }

    public IReadOnlyList<SpellkitChoice> Choices { get; }

    public bool IsCompleted { get; }

    public T? GetValue<T>() => SpellkitHostValueConverter.Convert<T>(value, "Select result");

    public bool TryGetValue<T>(out T? result) =>
        SpellkitHostValueConverter.TryConvert(value, out result);
}

internal readonly record struct SelectFrameOutcome(bool IsCompleted, SpkObject? Value);

internal sealed class SelectFrame
{
    private readonly SelectDefinition definition;
    private SelectStateDefinition state;
    private bool completed;

    internal SelectFrame(SelectDefinition definition)
    {
        this.definition = definition;
        state = definition.States.Single(candidate => candidate.IsInitial);
        completed = state.Choices.Count == 0;
    }

    internal string Name => definition.Name;

    internal SelectStateDefinition State => state;

    internal bool IsCompleted => completed;

    internal void Cancel() => completed = true;

    internal SelectFrameOutcome Apply(SpkObject result)
    {
        if (TryReadControlSignal(result, out var signal, out var signalValue))
        {
            if (signal == SelectControlSignal.Exit)
            {
                completed = true;
                return new(true, signalValue);
            }

            if (signalValue is not SpkString target)
            {
                throw new InvalidOperationException("A select goto must target a string state name.");
            }

            state = definition.States.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, target.Value, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"Select '{Name}' has no state named '{target.Value}'.");
        }

        if (state.Choices.Count == 0)
        {
            completed = true;
            return new(true, null);
        }

        return new(false, null);
    }

    private static bool TryReadControlSignal(SpkObject value, out string signal, out SpkObject payload)
    {
        if (value is SpkTuple { Count: 2 } tuple && tuple[0] is SpkString marker
            && (marker.Value == SelectControlSignal.Goto || marker.Value == SelectControlSignal.Exit))
        {
            signal = marker.Value;
            payload = tuple[1];
            return true;
        }

        signal = string.Empty;
        payload = SpkNil.Instance;
        return false;
    }
}

public sealed class SpellkitSelectSession : IDisposable
{
    private readonly object syncRoot = new();
    private readonly SpellkitInstance instance;
    private readonly int unitId;
    private readonly SelectFrame frame;
    private SpellkitSelectSession? nested;
    private SpkMachine.VmContinuation? choiceContinuation;
    private bool disposed;

    internal SpellkitSelectSession(SpellkitInstance instance, SelectDefinition definition, int unitId)
    {
        this.instance = instance;
        this.unitId = unitId;
        frame = new(definition);
    }

    public string Name => frame.Name;

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
                return frame.IsCompleted;
            }
        }
    }

    public SpellkitSelectResult Choose(string choiceId) => ChooseCore(choiceId, null, hasArgument: false);

    public SpellkitSelectResult Choose(string choiceId, object? argument) => ChooseCore(choiceId, argument, hasArgument: true);

    public void Cancel()
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            nested?.Cancel();
            frame.Cancel();
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            nested?.Dispose();
            nested = null;
            choiceContinuation = null;
            frame.Cancel();
            disposed = true;
        }
    }

    private SpellkitSelectResult ChooseCore(string choiceId, object? argument, bool hasArgument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(choiceId);
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (frame.IsCompleted)
            {
                throw new InvalidOperationException($"Select session '{Name}' has already completed.");
            }

            if (nested is not null)
            {
                return ResumeNested(choiceId, argument, hasArgument);
            }

            var choice = frame.State.Choices.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, choiceId, StringComparison.Ordinal));
            if (choice is null)
            {
                throw new ArgumentException(
                    $"Choice '{choiceId}' is not available in select state '{frame.State.Name}'.",
                    nameof(choiceId));
            }

            if (!IsAvailable(choice))
            {
                throw new ArgumentException(
                    $"Choice '{choiceId}' is not currently available in select state '{frame.State.Name}'.",
                    nameof(choiceId));
            }

            var arguments = ConvertArguments(choice, argument, hasArgument);
            var result = instance.InvokeSelectChoice(unitId, choice, arguments);
            return ApplyChoiceExecution(result);
        }
    }

    private SpellkitSelectResult ResumeNested(string choiceId, object? argument, bool hasArgument)
    {
        var nestedResult = hasArgument
            ? nested!.Choose(choiceId, argument)
            : nested!.Choose(choiceId);
        if (!nestedResult.IsCompleted)
        {
            return nestedResult;
        }

        nested.Dispose();
        nested = null;
        var continuation = choiceContinuation
            ?? throw new InvalidOperationException("The nested select has no parent continuation.");
        choiceContinuation = null;
        return ApplyChoiceExecution(SpkMachine.Resume(continuation));
    }

    private SpellkitSelectResult ApplyChoiceExecution(ExecutionResult result)
    {
        if (result.Reason is TerminationReason.Suspended)
        {
            if (result.Continuation is null
                || result.Suspension is not { SelectName.Length: > 0 } suspension)
            {
                throw new InvalidOperationException("A select choice suspended without a select request.");
            }

            choiceContinuation = result.Continuation;
            nested = instance.CreateSelectSession(suspension.SelectName);
            return new(nested.Choices, isCompleted: false);
        }

        if (result.Reason is not TerminationReason.Complete)
        {
            throw new InvalidOperationException("The select choice did not complete successfully.");
        }

        var outcome = frame.Apply(result.Value ?? SpkNil.Instance);
            if (outcome.IsCompleted)
            {
                return new(Array.Empty<SpellkitChoice>(), isCompleted: true, outcome.Value);
            }

            return new(GetChoices(), isCompleted: false);
    }

    private IReadOnlyList<SpellkitChoice> GetChoices() => frame.IsCompleted
        ? Array.Empty<SpellkitChoice>()
        : frame.State.Choices
            .Where(IsAvailable)
            .Select(choice => new SpellkitChoice(
                choice.Name,
                choice.ParameterCount,
                choice.Label,
                choice.Description))
            .ToArray();

    private bool IsAvailable(SelectChoiceDefinition choice) =>
        choice.GuardFunctionAddress is null || instance.EvaluateSelectGuard(unitId, choice.GuardFunctionAddress.Value);

    private static SpkObject[] ConvertArguments(
        SelectChoiceDefinition choice,
        object? argument,
        bool hasArgument)
    {
        if (choice.ParameterCount == 0)
        {
            if (hasArgument)
            {
                throw new ArgumentException($"Choice '{choice.Name}' does not accept an argument.", nameof(argument));
            }

            return Array.Empty<SpkObject>();
        }

        if (!hasArgument)
        {
            throw new ArgumentException($"Choice '{choice.Name}' requires an argument.", nameof(argument));
        }

        if (choice.ParameterCount == 1)
        {
            return [TypeConverter.ConvertFrom(argument)];
        }

        if (argument is not ITuple tuple || tuple.Length != choice.ParameterCount)
        {
            throw new ArgumentException(
                $"Choice '{choice.Name}' requires one tuple with {choice.ParameterCount} elements.",
                nameof(argument));
        }

        var values = new SpkObject[tuple.Length];
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
    private SpkMachine.VmContinuation? continuation;
    private SpkObject? value;
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

    public SpellkitSelectResult Choose(string choiceId) =>
        instance.Choose(this, choiceId, null, hasArgument: false);

    public SpellkitSelectResult Choose(string choiceId, object? argument) =>
        instance.Choose(this, choiceId, argument, hasArgument: true);

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

    internal SpkMachine.VmContinuation GetContinuation() =>
        continuation ?? throw new InvalidOperationException("The script has no suspended VM continuation.");

    internal void Advance(ExecutionResult result)
    {
        if (result.Reason is TerminationReason.Complete)
        {
            completed = true;
            continuation = null;
            select = null;
            value = result.Value;
            return;
        }

        if (result.Reason is TerminationReason.Suspended
            && result.Continuation is not null
            && result.Suspension is { SelectName.Length: > 0 } suspension)
        {
            continuation = result.Continuation;
            select = instance.CreateSelectSession(suspension.SelectName);
            return;
        }

        throw new InvalidOperationException("The VM suspended without a select request.");
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
