using System.Collections.Generic;

namespace Spellkit.Compiler;

internal static class SelectControlSignal
{
    internal const string Goto = "\u0001spellkit.select.goto";
    internal const string Exit = "\u0001spellkit.select.exit";
}

internal sealed record SelectDefinition(string? Name, IReadOnlyList<SelectStateDefinition> States);

internal sealed record SelectStateDefinition(
    string Name,
    bool IsInitial,
    int? ViewFunctionSlot,
    int? EnterFunctionSlot,
    int? LeaveFunctionSlot,
    int? OtherwiseFunctionSlot,
    IReadOnlyList<SelectChoiceDefinition> Choices,
    IReadOnlyList<SelectDynamicChoiceGroupDefinition> DynamicChoices,
    IReadOnlyList<SelectEventDefinition> Events);

internal sealed record SelectParameterDefinition(string Name, string? TypeName);

internal sealed record SelectChoiceDefinition(
    string Name,
    string Label,
    int FunctionSlot,
    int? GuardFunctionSlot,
    int? ViewFunctionSlot,
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
    int? ViewFunctionSlot,
    int FunctionSlot);

internal sealed record SelectEventDefinition(
    string Name,
    int FunctionSlot,
    IReadOnlyList<SelectParameterDefinition> Parameters)
{
    internal int ParameterCount => Parameters.Count;
}
