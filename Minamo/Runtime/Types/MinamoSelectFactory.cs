using Minamo.Compiler;
using System;
using System.Collections.Generic;
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

    internal string Name => name;

    internal MinamoFunction Choice(SelectChoiceDefinition choice) => closures![choice.FunctionSlot];

    internal MinamoFunction? ChoiceMetadata(SelectChoiceDefinition choice) =>
        choice.MetadataFunctionSlot is int slot ? closures![slot] : null;

    internal MinamoFunction? Guard(SelectChoiceDefinition choice) =>
        choice.GuardFunctionSlot is int slot ? closures![slot] : null;

    internal MinamoFunction Property(SelectPropertyDefinition property) => closures![property.FunctionSlot];

    internal MinamoFunction? PropertyMetadata(SelectPropertyDefinition property) =>
        property.MetadataFunctionSlot is int slot ? closures![slot] : null;

    internal MinamoFunction Event(SelectEventDefinition handler) => closures![handler.FunctionSlot];

    internal MinamoFunction? Description() =>
        Definition!.DescriptionFunctionSlot is int slot ? closures![slot] : null;

    public override string TypeName => "SelectFactory";

    public override object ToObject() => this;

    public override bool Equals(MinamoObject? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

internal sealed class SelectInstance
{
    private readonly MinamoSelectFactory factory;
    private bool completed;
    internal SelectInstance(MinamoSelectFactory factory) => this.factory = factory;

    internal string Name => factory.Name;

    internal SelectDefinition Definition => factory.Definition!;

    internal IReadOnlyList<SelectChoiceDefinition> Choices => Definition.Choices;

    internal IReadOnlyList<SelectPropertyDefinition> Properties => Definition.Properties;

    internal IReadOnlyList<SelectEventDefinition> Events => Definition.Events;

    internal bool IsCompleted => completed;

    internal MinamoObject Value { get; private set; } = MinamoNil.Instance;

    internal void Cancel()
    {
        completed = true;
        Value = MinamoNil.Instance;
    }

    internal MinamoFunction Choice(SelectChoiceDefinition choice) => factory.Choice(choice);

    internal MinamoFunction? ChoiceMetadata(SelectChoiceDefinition choice) =>
        factory.ChoiceMetadata(choice);

    internal MinamoFunction? Guard(SelectChoiceDefinition choice) => factory.Guard(choice);

    internal MinamoFunction Property(SelectPropertyDefinition property) => factory.Property(property);

    internal MinamoFunction? PropertyMetadata(SelectPropertyDefinition property) =>
        factory.PropertyMetadata(property);

    internal MinamoFunction Event(SelectEventDefinition handler) => factory.Event(handler);

    internal MinamoFunction? Description() => factory.Description();

    internal void Complete()
    {
        Complete(MinamoNil.Instance);
    }

    internal void Complete(MinamoObject value)
    {
        completed = true;
        Value = value;
    }

    internal SelectActionOutcome Apply(MinamoObject result)
    {
        if (result is MinamoTuple tuple
            && tuple.Count == 2
            && tuple[0] is MinamoString marker)
        {
            if (marker.Value == SelectControlSignal.Exit)
            {
                return new(SelectActionOutcomeKind.Exit, tuple[1]);
            }

            if (marker.Value == SelectControlSignal.Goto)
            {
                if (tuple[1] is not MinamoSelectFactory target)
                {
                    throw new InvalidOperationException(
                        "A select goto target must evaluate to a select.");
                }

                return new(SelectActionOutcomeKind.Goto, Target: target);
            }

            if (marker.Value == SelectControlSignal.Return)
            {
                return new(SelectActionOutcomeKind.Return);
            }
        }

        return new(SelectActionOutcomeKind.Continue);
    }

}

internal readonly record struct SelectActionOutcome(
    SelectActionOutcomeKind Kind,
    MinamoObject? Value = null,
    MinamoSelectFactory? Target = null);

internal enum SelectActionOutcomeKind
{
    Continue,
    Goto,
    Return,
    Exit
}
