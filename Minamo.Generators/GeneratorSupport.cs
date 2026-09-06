using Microsoft.CodeAnalysis;

namespace Minamo.Generators;

internal static class GeneratorSupport
{
    private static readonly DiagnosticDescriptor ErrorDescriptor = new(
        "Minamo0001",
        "Minamo.Generator",
        "{0}",
        "Minamo.Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static bool IsMinamoObject(ITypeSymbol type) =>
        CheckBaseType(type, Types.MinamoObject);

    public static bool Error(SourceProductionContext context, string text)
    {
        context.ReportDiagnostic(Diagnostic.Create(ErrorDescriptor, Location.None, text));
        return false;
    }

    private static bool CheckBaseType(ITypeSymbol type, string fullName)
    {
        var baseType = type.BaseType;

        if (baseType is null)
        {
            return false;
        }

        return baseType.ToString() == fullName || CheckBaseType(baseType, fullName);
    }
}
