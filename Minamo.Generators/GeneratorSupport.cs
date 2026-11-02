using Microsoft.CodeAnalysis;

namespace Minamo.Generators;

internal static class GeneratorSupport
{
    internal static bool IsDeclaredOn(AttributeData attribute, Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax declaration) =>
        attribute.ApplicationSyntaxReference is { } syntax
        && syntax.SyntaxTree == declaration.SyntaxTree && declaration.Span.Contains(syntax.Span);

    internal static string EncodeName(string name)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var character in name)
        {
            if (character == '_') builder.Append("__");
            else if (char.IsLetterOrDigit(character)) builder.Append(character);
            else builder.Append('_').Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    internal static string HintName(INamedTypeSymbol type) =>
        EncodeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

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
