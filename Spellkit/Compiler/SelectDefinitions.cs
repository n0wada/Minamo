using System.Collections.Generic;

namespace Spellkit.Compiler;

internal static class SelectControlSignal
{
    internal const string Goto = "\u0001spellkit.select.goto";
    internal const string Exit = "\u0001spellkit.select.exit";
}

internal sealed record SelectDefinition(
    string? Name,
    int? DescriptionFunctionSlot,
    IReadOnlyList<SelectStateDefinition> States);

internal sealed record SelectStateDefinition(
    string Name,
    bool IsInitial,
    int? EnterFunctionSlot,
    int? LeaveFunctionSlot,
    int? EmptyFunctionSlot,
    IReadOnlyList<SelectChoiceDefinition> Choices,
    IReadOnlyList<SelectDynamicChoiceGroupDefinition> DynamicChoices,
    IReadOnlyList<SelectChoiceSpreadDefinition> ChoiceSpreads,
    IReadOnlyList<SelectEventDefinition> Events);

internal sealed record SelectParameterDefinition(string Name, string? TypeName);

internal sealed record SelectChoiceDefinition(
    string Name,
    string Label,
    int FunctionSlot,
    int? GuardFunctionSlot,
    IReadOnlyList<SelectParameterDefinition> Parameters)
{
    internal int ParameterCount => Parameters.Count;
}

internal sealed record SelectDynamicChoiceGroupDefinition(
    int SourceFunctionSlot,
    IReadOnlyList<SelectDynamicChoiceDefinition> Choices);

internal sealed record SelectDynamicChoiceDefinition(
    int IdFunctionSlot,
    int? LabelFunctionSlot,
    int? GuardFunctionSlot,
    int FunctionSlot);

internal sealed record SelectChoiceSpreadDefinition(int SourceFunctionSlot);

internal sealed record SelectEventDefinition(
    string Name,
    int FunctionSlot,
    IReadOnlyList<SelectParameterDefinition> Parameters)
{
    internal int ParameterCount => Parameters.Count;
}
