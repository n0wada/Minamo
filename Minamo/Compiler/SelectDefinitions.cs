using System.Collections.Generic;

namespace Minamo.Compiler;

internal static class SelectControlSignal
{
    internal const string Exit = "\u0001minamo.select.exit";
    internal const string Goto = "\u0001minamo.select.goto";
    internal const string Return = "\u0001minamo.select.return";
}

internal sealed record SelectDefinition(
    string? Name,
    int? DescriptionFunctionSlot,
    IReadOnlyList<SelectPropertyDefinition> Properties,
    IReadOnlyList<SelectChoiceDefinition> Choices,
    IReadOnlyList<SelectEventDefinition> Events);

internal sealed record SelectParameterDefinition(string Name, string? TypeName);

internal sealed record SelectPropertyDefinition(
    string Name,
    int FunctionSlot,
    int? MetadataFunctionSlot);

internal sealed record SelectChoiceDefinition(
    string Name,
    int FunctionSlot,
    int? GuardFunctionSlot,
    int? MetadataFunctionSlot,
    IReadOnlyList<SelectParameterDefinition> Parameters)
{
    internal int ParameterCount => Parameters.Count;
}

internal sealed record SelectEventDefinition(
    string Name,
    int FunctionSlot,
    IReadOnlyList<SelectParameterDefinition> Parameters)
{
    internal int ParameterCount => Parameters.Count;
}
