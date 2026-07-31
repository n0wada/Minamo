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

    internal SpellkitFunction Event(SelectEventDefinition handler) => closures![handler.FunctionSlot];

    public override string TypeName => "SelectFactory";

    public override object ToObject() => this;

    public override bool Equals(SpellkitObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

internal sealed class SelectInstance
{
    private readonly SpellkitSelectFactory factory;
    private SelectStateDefinition state;
    private bool completed;

    internal SelectInstance(SpellkitSelectFactory factory)
    {
        this.factory = factory;
        state = factory.InitialState;
        CompleteIfIdle();
    }

    internal SelectStateDefinition State => state;

    internal string Name => factory.Name;

    internal bool IsCompleted => completed;

    internal SpellkitObject Value { get; private set; } = SpellkitNil.Instance;

    internal void Cancel()
    {
        completed = true;
        Value = SpellkitNil.Instance;
    }

    internal SpellkitFunction Choice(SelectChoiceDefinition choice) => factory.Choice(choice);

    internal SpellkitFunction? Guard(SelectChoiceDefinition choice) => factory.Guard(choice);

    internal SpellkitFunction Event(SelectEventDefinition handler) => factory.Event(handler);

    internal SelectActionOutcome Apply(SpellkitObject result)
    {
        if (result is SpellkitTuple { Count: 2 } tuple && tuple[0] is SpellkitString marker)
        {
            if (marker.Value == SelectControlSignal.Exit)
            {
                completed = true;
                Value = tuple[1];
                return new(IsCompleted: true, Value);
            }

            if (marker.Value == SelectControlSignal.Goto && tuple[1] is SpellkitString target)
            {
                state = factory.Definition!.States.SingleOrDefault(candidate => candidate.Name == target.Value)
                    ?? throw new InvalidOperationException($"The select has no state named '{target.Value}'.");
                CompleteIfIdle();
                return new(IsCompleted, Value);
            }
        }

        return new(IsCompleted: false, SpellkitNil.Instance);
    }

    private bool CompleteIfIdle()
    {
        if (state.Choices.Count == 0
            && state.Events.Count == 0)
        {
            completed = true;
            Value = SpellkitNil.Instance;
        }

        return completed;
    }
}

internal readonly record struct SelectActionOutcome(
    bool IsCompleted,
    SpellkitObject Value);
