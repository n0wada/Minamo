using Spellkit.Compiler;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Spellkit.Runtime.Types;

/// <summary>Static select shape plus the closure slots emitted for that shape.</summary>
internal sealed class SpellkitSelectDefinitionValue(SelectDefinition definition, int closureCount) : SpellkitObject(SpellkitTypeCodes.Object)
{
    internal SelectDefinition Definition { get; } = definition;

    internal int ClosureCount { get; } = closureCount;

    public override string TypeName => "SelectDefinition";

    public override object ToObject() => this;

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

/// <summary>A reusable select factory. Its functions are ordinary closures and retain their captures.</summary>
internal sealed class SpellkitSelectFactory : SpellkitObject
{
    private readonly IReadOnlyList<SpellkitFunction>? closures;
    private readonly SpellkitFunction? initializer;
    private readonly string name;

    internal SpellkitSelectFactory(SpellkitSelectDefinitionValue definition, IReadOnlyList<SpellkitFunction> closures)
        : base(SpellkitTypeCodes.Object)
    {
        if (definition.ClosureCount != closures.Count)
        {
            throw new InvalidOperationException("The select factory closure count does not match its definition.");
        }

        Definition = definition.Definition;
        this.closures = closures;
        name = Definition.Name ?? "<anonymous>";
    }

    internal SpellkitSelectFactory(string name, SpellkitFunction initializer)
        : base(SpellkitTypeCodes.Object)
    {
        this.name = name;
        this.initializer = initializer;
    }

    internal SelectDefinition? Definition { get; }

    internal SelectInstance Create(ExecutionContext context)
    {
        if (initializer is not null)
        {
            var concrete = initializer.Call(context) as SpellkitSelectFactory
                ?? throw new InvalidOperationException("The select factory initializer did not return a select factory.");
            return concrete.Create(context);
        }

        return new(this);
    }

    internal SelectStateDefinition InitialState =>
        Definition!.States.Single(candidate => candidate.IsInitial);

    internal string Name => name;

    internal SpellkitFunction Choice(SelectChoiceDefinition choice) => closures![choice.FunctionSlot];

    internal SpellkitFunction? Guard(SelectChoiceDefinition choice) =>
        choice.GuardFunctionSlot is int slot ? closures![slot] : null;

    internal SpellkitFunction? View(SelectChoiceDefinition choice) =>
        choice.ViewFunctionSlot is int slot ? closures![slot] : null;

    internal SpellkitFunction DynamicChoiceSource(SelectDynamicChoiceGroupDefinition group) =>
        closures![group.SourceFunctionSlot];

    internal SpellkitFunction DynamicChoiceId(SelectDynamicChoiceDefinition choice) =>
        closures![choice.IdFunctionSlot];

    internal SpellkitFunction? DynamicChoiceLabel(SelectDynamicChoiceDefinition choice) =>
        choice.LabelFunctionSlot is int slot ? closures![slot] : null;

    internal SpellkitFunction? DynamicChoiceDescription(SelectDynamicChoiceDefinition choice) =>
        choice.DescriptionFunctionSlot is int slot ? closures![slot] : null;

    internal SpellkitFunction? DynamicChoiceGuard(SelectDynamicChoiceDefinition choice) =>
        choice.GuardFunctionSlot is int slot ? closures![slot] : null;

    internal SpellkitFunction? DynamicChoiceView(SelectDynamicChoiceDefinition choice) =>
        choice.ViewFunctionSlot is int slot ? closures![slot] : null;

    internal SpellkitFunction DynamicChoiceAction(SelectDynamicChoiceDefinition choice) =>
        closures![choice.FunctionSlot];

    internal SpellkitFunction Event(SelectEventDefinition handler) => closures![handler.FunctionSlot];

    internal SpellkitFunction? Enter(SelectStateDefinition state) =>
        state.EnterFunctionSlot is int slot ? closures![slot] : null;

    internal SpellkitFunction? View(SelectStateDefinition state) =>
        state.ViewFunctionSlot is int slot ? closures![slot] : null;

    internal SpellkitFunction? Leave(SelectStateDefinition state) =>
        state.LeaveFunctionSlot is int slot ? closures![slot] : null;

    internal SpellkitFunction? Otherwise(SelectStateDefinition state) =>
        state.OtherwiseFunctionSlot is int slot ? closures![slot] : null;

    public override string TypeName => "SelectFactory";

    public override object ToObject() => this;

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

internal sealed class SelectInstance
{
    private readonly SpellkitSelectFactory factory;
    private SelectStateDefinition state;
    private SpellkitObject[] stateArguments;
    private bool completed;
    private bool otherwiseTriggered;

    internal SelectInstance(SpellkitSelectFactory factory)
    {
        this.factory = factory;
        state = factory.InitialState;
        stateArguments = CreateInitialStateArguments(state);
    }

    internal SelectStateDefinition State => state;

    internal string Name => factory.Name;

    internal bool IsCompleted => completed;

    internal SpellkitObject[] StateArguments => stateArguments;

    internal bool ShouldRunOtherwise =>
        !otherwiseTriggered
        && state.OtherwiseFunctionSlot is not null
        && state.Events.Count == 0;

    internal SpellkitObject Value { get; private set; } = SpellkitNil.Instance;

    internal void Cancel()
    {
        completed = true;
        Value = SpellkitNil.Instance;
    }

    internal SpellkitFunction Choice(SelectChoiceDefinition choice) => factory.Choice(choice);

    internal SpellkitFunction? Guard(SelectChoiceDefinition choice) => factory.Guard(choice);

    internal SpellkitFunction? View(SelectChoiceDefinition choice) => factory.View(choice);

    internal SpellkitFunction DynamicChoiceSource(SelectDynamicChoiceGroupDefinition group) =>
        factory.DynamicChoiceSource(group);

    internal SpellkitFunction DynamicChoiceId(SelectDynamicChoiceDefinition choice) =>
        factory.DynamicChoiceId(choice);

    internal SpellkitFunction? DynamicChoiceLabel(SelectDynamicChoiceDefinition choice) =>
        factory.DynamicChoiceLabel(choice);

    internal SpellkitFunction? DynamicChoiceDescription(SelectDynamicChoiceDefinition choice) =>
        factory.DynamicChoiceDescription(choice);

    internal SpellkitFunction? DynamicChoiceGuard(SelectDynamicChoiceDefinition choice) =>
        factory.DynamicChoiceGuard(choice);

    internal SpellkitFunction? DynamicChoiceView(SelectDynamicChoiceDefinition choice) =>
        factory.DynamicChoiceView(choice);

    internal SpellkitFunction DynamicChoiceAction(SelectDynamicChoiceDefinition choice) =>
        factory.DynamicChoiceAction(choice);

    internal SpellkitFunction Event(SelectEventDefinition handler) => factory.Event(handler);

    internal SpellkitFunction? Enter(SelectStateDefinition target) => factory.Enter(target);

    internal SpellkitFunction? View(SelectStateDefinition target) => factory.View(target);

    internal SpellkitFunction? Leave(SelectStateDefinition target) => factory.Leave(target);

    internal SpellkitFunction? Otherwise() => factory.Otherwise(state);

    internal SpellkitObject[] AddStateArguments(IReadOnlyList<SpellkitObject> arguments)
    {
        var result = new SpellkitObject[stateArguments.Length + arguments.Count];
        stateArguments.CopyTo(result, 0);
        for (var i = 0; i < arguments.Count; i++)
        {
            result[stateArguments.Length + i] = arguments[i];
        }

        return result;
    }

    internal void MarkOtherwiseTriggered() => otherwiseTriggered = true;

    internal void CompleteIfIdle()
    {
        if (state.Choices.Count == 0
            && state.DynamicChoices.Count == 0
            && state.Events.Count == 0
            && state.OtherwiseFunctionSlot is null)
        {
            completed = true;
            Value = SpellkitNil.Instance;
        }
    }

    internal SelectActionOutcome Apply(SpellkitObject result)
    {
        if (result is SpellkitTuple tuple
            && tuple.Count is 2 or 3
            && tuple[0] is SpellkitString marker)
        {
            if (marker.Value == SelectControlSignal.Exit && tuple.Count == 2)
            {
                var leavingState = state;
                var leavingArguments = stateArguments;
                completed = true;
                Value = tuple[1];
                return new(
                    IsCompleted: true,
                    Value: Value,
                    LeavingState: leavingState,
                    EnteringState: null,
                    LeavingStateArguments: leavingArguments);
            }

            if (marker.Value == SelectControlSignal.Goto && tuple[1] is SpellkitString target)
            {
                var leavingState = state;
                var leavingArguments = stateArguments;
                var enteringState = factory.Definition!.States.SingleOrDefault(candidate => candidate.Name == target.Value)
                    ?? throw new InvalidOperationException($"The select has no state named '{target.Value}'.");
                var enteringArguments = ReadTransitionArguments(tuple, enteringState);
                state = enteringState;
                stateArguments = enteringArguments;
                otherwiseTriggered = false;
                return new(
                    IsCompleted: false,
                    Value: Value,
                    LeavingState: leavingState,
                    EnteringState: enteringState,
                    LeavingStateArguments: leavingArguments);
            }
        }

        return new(IsCompleted: false, SpellkitNil.Instance);
    }

    private static SpellkitObject[] CreateInitialStateArguments(SelectStateDefinition state)
    {
        var arguments = new SpellkitObject[state.Parameters.Count];
        Array.Fill(arguments, SpellkitNil.Instance);
        return arguments;
    }

    private static SpellkitObject[] ReadTransitionArguments(
        SpellkitTuple transition,
        SelectStateDefinition target)
    {
        var arguments = transition.Count == 2
            ? Array.Empty<SpellkitObject>()
            : transition[2] is SpellkitTuple values
                ? ToArray(values)
                : throw new InvalidOperationException("A select state transition payload must be a tuple.");
        if (arguments.Length != target.Parameters.Count)
        {
            throw new InvalidOperationException(
                $"The select state '{target.Name}' expects {target.Parameters.Count} transition argument(s), but {arguments.Length} were provided.");
        }

        return arguments;
    }

    private static SpellkitObject[] ToArray(SpellkitTuple values)
    {
        var result = new SpellkitObject[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            result[i] = values[i];
        }

        return result;
    }

}

internal readonly record struct SelectActionOutcome(
    bool IsCompleted,
    SpellkitObject Value,
    SelectStateDefinition? LeavingState = null,
    SelectStateDefinition? EnteringState = null,
    SpellkitObject[]? LeavingStateArguments = null);
