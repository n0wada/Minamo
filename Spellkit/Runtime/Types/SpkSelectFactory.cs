using Spellkit.Compiler;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Spellkit.Runtime.Types;

/// <summary>Static select shape plus the closure slots emitted for that shape.</summary>
internal sealed class SpkSelectDefinitionValue(SelectDefinition definition, int closureCount) : SpkObject(Spk.Object)
{
    internal SelectDefinition Definition { get; } = definition;

    internal int ClosureCount { get; } = closureCount;

    public override string TypeName => "SelectDefinition";

    public override object ToObject() => this;

    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

/// <summary>A reusable select factory. Its functions are ordinary closures and retain their captures.</summary>
internal sealed class SpkSelectFactory : SpkObject
{
    private readonly IReadOnlyList<SpkFunction>? closures;
    private readonly SpkFunction? initializer;
    private readonly string name;

    internal SpkSelectFactory(SpkSelectDefinitionValue definition, IReadOnlyList<SpkFunction> closures)
        : base(Spk.Object)
    {
        if (definition.ClosureCount != closures.Count)
        {
            throw new InvalidOperationException("The select factory closure count does not match its definition.");
        }

        Definition = definition.Definition;
        this.closures = closures;
        name = Definition.Name ?? "<anonymous>";
    }

    internal SpkSelectFactory(string name, SpkFunction initializer)
        : base(Spk.Object)
    {
        this.name = name;
        this.initializer = initializer;
    }

    internal SelectDefinition? Definition { get; }

    internal SelectInstance Create(ExecutionContext context)
    {
        if (initializer is not null)
        {
            var concrete = initializer.Call(context) as SpkSelectFactory
                ?? throw new InvalidOperationException("The select factory initializer did not return a select factory.");
            return concrete.Create(context);
        }

        return new(this);
    }

    internal SelectStateDefinition InitialState =>
        Definition!.States.Single(candidate => candidate.IsInitial);

    internal string Name => name;

    internal SpkFunction Choice(SelectChoiceDefinition choice) => closures![choice.FunctionSlot];

    internal SpkFunction? Guard(SelectChoiceDefinition choice) =>
        choice.GuardFunctionSlot is int slot ? closures![slot] : null;

    public override string TypeName => "SelectFactory";

    public override object ToObject() => this;

    public override bool Equals(SpkObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

internal sealed class SelectInstance
{
    private readonly SpkSelectFactory factory;
    private SelectStateDefinition state;
    private bool completed;

    internal SelectInstance(SpkSelectFactory factory)
    {
        this.factory = factory;
        state = factory.InitialState;
        completed = state.Choices.Count == 0;
    }

    internal SelectStateDefinition State => state;

    internal string Name => factory.Name;

    internal bool IsCompleted => completed;

    internal void Cancel() => completed = true;

    internal SpkFunction Choice(SelectChoiceDefinition choice) => factory.Choice(choice);

    internal SpkFunction? Guard(SelectChoiceDefinition choice) => factory.Guard(choice);

    internal (bool IsCompleted, SpkObject? Value) Apply(SpkObject result)
    {
        if (result is SpkTuple { Count: 2 } tuple && tuple[0] is SpkString marker)
        {
            if (marker.Value == SelectControlSignal.Exit)
            {
                completed = true;
                return (true, tuple[1]);
            }

            if (marker.Value == SelectControlSignal.Goto && tuple[1] is SpkString target)
            {
                state = factory.Definition!.States.SingleOrDefault(candidate => candidate.Name == target.Value)
                    ?? throw new InvalidOperationException($"The select has no state named '{target.Value}'.");
            }
        }

        if (state.Choices.Count == 0)
        {
            completed = true;
            return (true, null);
        }

        return (false, null);
    }
}
