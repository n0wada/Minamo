using Microsoft.CodeAnalysis;

namespace Spellkit.Generators;

internal static class GeneratorSupport
{
    private static readonly DiagnosticDescriptor ErrorDescriptor = new(
        "Spk0001",
        "Spellkit.Generator",
        "{0}",
        "Spellkit.Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static bool IsSpkObject(ITypeSymbol type) =>
        CheckBaseType(type, Types.SpkObject);

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
