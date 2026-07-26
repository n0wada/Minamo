using System.Collections.Generic;

namespace Spellkit.Compiler;

internal static class SelectControlSignal
{
    internal const string Goto = "\u0001spellkit.select.goto";
    internal const string Exit = "\u0001spellkit.select.exit";
}

internal sealed record SelectDefinition(string Name, IReadOnlyList<SelectStateDefinition> States);

internal sealed record SelectStateDefinition(
    string Name,
    bool IsInitial,
    IReadOnlyList<SelectChoiceDefinition> Choices);

internal sealed record SelectChoiceDefinition(
    string Name,
    string Label,
    string? Description,
    int FunctionAddress,
    int? GuardFunctionAddress,
    int ParameterCount);
