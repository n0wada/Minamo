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

public sealed class SpellkitSelectSession : IDisposable
{
    private readonly object syncRoot = new();
    private readonly SpellkitInstance instance;
    private readonly SelectDefinition definition;
    private readonly int unitId;
    private SelectStateDefinition state;
    private bool completed;
    private bool disposed;

    internal SpellkitSelectSession(SpellkitInstance instance, SelectDefinition definition, int unitId)
    {
        this.instance = instance;
        this.definition = definition;
        this.unitId = unitId;
        state = definition.States.Single(candidate => candidate.IsInitial);
        completed = state.Choices.Count == 0;
    }

    public string Name => definition.Name;

    public IReadOnlyList<SpellkitChoice> Choices
    {
        get
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();
                return GetChoices();
            }
        }
    }

    public bool IsCompleted
    {
        get
        {
            lock (syncRoot)
            {
                return completed;
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
            completed = true;
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

            completed = true;
            disposed = true;
        }
    }

    private SpellkitSelectResult ChooseCore(string choiceId, object? argument, bool hasArgument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(choiceId);
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (completed)
            {
                throw new InvalidOperationException($"Select session '{Name}' has already completed.");
            }

            var choice = state.Choices.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, choiceId, StringComparison.Ordinal));
            if (choice is null)
            {
                throw new ArgumentException(
                    $"Choice '{choiceId}' is not available in select state '{state.Name}'.",
                    nameof(choiceId));
            }

            if (!IsAvailable(choice))
            {
                throw new ArgumentException(
                    $"Choice '{choiceId}' is not currently available in select state '{state.Name}'.",
                    nameof(choiceId));
            }

            var arguments = ConvertArguments(choice, argument, hasArgument);
            var result = instance.InvokeSelectChoice(unitId, choice, arguments);
            if (TryReadControlSignal(result, out var signal, out var signalValue))
            {
                if (signal == SelectControlSignal.Exit)
                {
                    completed = true;
                    return new(Array.Empty<SpellkitChoice>(), isCompleted: true, signalValue);
                }

                if (signal == SelectControlSignal.Goto)
                {
                    if (signalValue is not SpkString target)
                    {
                        throw new InvalidOperationException("A select goto must target a string state name.");
                    }

                    state = definition.States.SingleOrDefault(candidate =>
                        string.Equals(candidate.Name, target.Value, StringComparison.Ordinal))
                        ?? throw new InvalidOperationException(
                            $"Select '{Name}' has no state named '{target.Value}'.");
                }
            }

            if (state.Choices.Count == 0)
            {
                completed = true;
                return new(Array.Empty<SpellkitChoice>(), isCompleted: true);
            }

            return new(GetChoices(), isCompleted: false);
        }
    }

    private IReadOnlyList<SpellkitChoice> GetChoices() => completed
        ? Array.Empty<SpellkitChoice>()
        : state.Choices
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
