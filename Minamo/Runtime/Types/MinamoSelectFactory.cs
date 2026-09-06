using Minamo.Compiler;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Minamo.Runtime.Types;

/// <summary>Static select shape plus the closure slots emitted for that shape.</summary>
internal sealed class MinamoSelectDefinitionValue(SelectDefinition definition, int closureCount) : MinamoObject(MinamoTypeCodes.Object)
{
    internal SelectDefinition Definition { get; } = definition;

    internal int ClosureCount { get; } = closureCount;

    public override string TypeName => "SelectDefinition";

    public override object ToObject() => this;

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

/// <summary>A reusable select factory. Its functions are ordinary closures and retain their captures.</summary>
internal sealed class MinamoSelectFactory : MinamoObject
{
    private readonly IReadOnlyList<MinamoFunction>? closures;
    private readonly MinamoFunction? initializer;
    private readonly string name;

    internal MinamoSelectFactory(MinamoSelectDefinitionValue definition, IReadOnlyList<MinamoFunction> closures)
        : base(MinamoTypeCodes.Object)
    {
        if (definition.ClosureCount != closures.Count)
        {
            throw new InvalidOperationException("The select factory closure count does not match its definition.");
        }

        Definition = definition.Definition;
        this.closures = closures;
        name = Definition.Name ?? "<anonymous>";
    }

    internal MinamoSelectFactory(string name, MinamoFunction initializer)
        : base(MinamoTypeCodes.Object)
    {
        this.name = name;
        this.initializer = initializer;
    }

    internal SelectDefinition? Definition { get; }

    internal SelectInstance Create(ExecutionContext context)
    {
        if (initializer is not null)
        {
            var concrete = initializer.Call(context) as MinamoSelectFactory
                ?? throw new InvalidOperationException("The select factory initializer did not return a select factory.");
            return concrete.Create(context);
        }

        return new(this);
    }

    internal SelectStateDefinition InitialState =>
        Definition!.States.Single(candidate => candidate.IsInitial);

    internal string Name => name;

    internal MinamoFunction Choice(SelectChoiceDefinition choice) => closures![choice.FunctionSlot];

    internal MinamoFunction? Guard(SelectChoiceDefinition choice) =>
        choice.GuardFunctionSlot is int slot ? closures![slot] : null;

    internal MinamoFunction DynamicChoiceSource(SelectDynamicChoiceGroupDefinition group) =>
        closures![group.SourceFunctionSlot];

    internal MinamoFunction DynamicChoiceId(SelectDynamicChoiceDefinition choice) =>
        closures![choice.IdFunctionSlot];

    internal MinamoFunction? DynamicChoiceLabel(SelectDynamicChoiceDefinition choice) =>
        choice.LabelFunctionSlot is int slot ? closures![slot] : null;

    internal MinamoFunction? DynamicChoiceGuard(SelectDynamicChoiceDefinition choice) =>
        choice.GuardFunctionSlot is int slot ? closures![slot] : null;

    internal MinamoFunction DynamicChoiceAction(SelectDynamicChoiceDefinition choice) =>
        closures![choice.FunctionSlot];

    internal MinamoFunction ChoiceSpreadSource(SelectChoiceSpreadDefinition spread) =>
        closures![spread.SourceFunctionSlot];

    internal MinamoFunction Event(SelectEventDefinition handler) => closures![handler.FunctionSlot];

    internal MinamoFunction? Enter(SelectStateDefinition state) =>
        state.EnterFunctionSlot is int slot ? closures![slot] : null;

    internal MinamoFunction? Description() =>
        Definition!.DescriptionFunctionSlot is int slot ? closures![slot] : null;

    internal MinamoFunction? Leave(SelectStateDefinition state) =>
        state.LeaveFunctionSlot is int slot ? closures![slot] : null;

    internal MinamoFunction? Empty(SelectStateDefinition state) =>
        state.EmptyFunctionSlot is int slot ? closures![slot] : null;

    public override string TypeName => "SelectFactory";

    public override object ToObject() => this;

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

internal sealed class SelectInstance
{
    private readonly MinamoSelectFactory factory;
    private SelectStateDefinition state;
    private bool completed;
    private bool emptyTriggered;

    internal SelectInstance(MinamoSelectFactory factory)
    {
        this.factory = factory;
        state = factory.InitialState;
    }

    internal SelectStateDefinition State => state;

    internal string Name => factory.Name;

    internal bool IsCompleted => completed;

    internal bool ShouldRunEmpty =>
        !emptyTriggered
        && state.EmptyFunctionSlot is not null
        && state.Events.Count == 0;

    internal MinamoObject Value { get; private set; } = MinamoNil.Instance;

    internal void Cancel()
    {
        completed = true;
        Value = MinamoNil.Instance;
    }

    internal MinamoFunction Choice(SelectChoiceDefinition choice) => factory.Choice(choice);

    internal MinamoFunction? Guard(SelectChoiceDefinition choice) => factory.Guard(choice);

    internal MinamoFunction DynamicChoiceSource(SelectDynamicChoiceGroupDefinition group) =>
        factory.DynamicChoiceSource(group);

    internal MinamoFunction DynamicChoiceId(SelectDynamicChoiceDefinition choice) =>
        factory.DynamicChoiceId(choice);

    internal MinamoFunction? DynamicChoiceLabel(SelectDynamicChoiceDefinition choice) =>
        factory.DynamicChoiceLabel(choice);

    internal MinamoFunction? DynamicChoiceGuard(SelectDynamicChoiceDefinition choice) =>
        factory.DynamicChoiceGuard(choice);

    internal MinamoFunction DynamicChoiceAction(SelectDynamicChoiceDefinition choice) =>
        factory.DynamicChoiceAction(choice);

    internal MinamoFunction ChoiceSpreadSource(SelectChoiceSpreadDefinition spread) =>
        factory.ChoiceSpreadSource(spread);

    internal MinamoFunction Event(SelectEventDefinition handler) => factory.Event(handler);

    internal MinamoFunction? Enter(SelectStateDefinition target) => factory.Enter(target);

    internal MinamoFunction? Description() => factory.Description();

    internal MinamoFunction? Leave(SelectStateDefinition target) => factory.Leave(target);

    internal MinamoFunction? Empty() => factory.Empty(state);

    internal void MarkEmptyTriggered() => emptyTriggered = true;

    internal void CompleteIfIdle()
    {
        if (state.Choices.Count == 0
            && state.DynamicChoices.Count == 0
            && state.ChoiceSpreads.Count == 0
            && state.Events.Count == 0
            && state.EmptyFunctionSlot is null)
        {
            completed = true;
            Value = MinamoNil.Instance;
        }
    }

    internal SelectActionOutcome Apply(MinamoObject result)
    {
        if (result is MinamoTuple tuple
            && tuple.Count == 2
            && tuple[0] is MinamoString marker)
        {
            if (marker.Value == SelectControlSignal.Exit && tuple.Count == 2)
            {
                var leavingState = state;
                completed = true;
                Value = tuple[1];
                return new(
                    IsCompleted: true,
                    Value: Value,
                    LeavingState: leavingState,
                    EnteringState: null);
            }

            if (marker.Value == SelectControlSignal.Goto && tuple[1] is MinamoString target)
            {
                var leavingState = state;
                var enteringState = factory.Definition!.States.SingleOrDefault(candidate => candidate.Name == target.Value)
                    ?? throw new InvalidOperationException($"The select has no state named '{target.Value}'.");
                state = enteringState;
                emptyTriggered = false;
                return new(
                    IsCompleted: false,
                    Value: Value,
                    LeavingState: leavingState,
                    EnteringState: enteringState);
            }
        }

        return new(IsCompleted: false, MinamoNil.Instance);
    }

}

internal readonly record struct SelectActionOutcome(
    bool IsCompleted,
    MinamoObject Value,
    SelectStateDefinition? LeavingState = null,
    SelectStateDefinition? EnteringState = null);
