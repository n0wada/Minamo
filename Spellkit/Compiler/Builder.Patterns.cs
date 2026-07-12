using Spellkit.Compiler.Lowering;
using Spellkit.Parser.Model;

namespace Spellkit.Compiler;

partial class Builder
{
    private bool TryResolveZeroArgConstructor(Qualident typeName, out Qualident resolvedType, out string constructorName)
    {
        constructorName = typeName.Local;
        resolvedType = default!;

        if (typeName.Parent is not null)
        {
            if (types.TryGetValue(typeName.Parent, out var info)
                && info.Declaration.Style == TypeDeclarationStyle.Enum
                && HasZeroArgConstructor(info.Declaration, typeName.Local))
            {
                resolvedType = new Qualident(typeName.Parent);
                return true;
            }

            return false;
        }

        TypeInfo? match = null;

        foreach (var info in types.Values)
        {
            if (info.Declaration.Style != TypeDeclarationStyle.Enum
                || !HasZeroArgConstructor(info.Declaration, typeName.Local))
            {
                continue;
            }

            if (match is not null)
            {
                return false;
            }

            match = info;
        }

        if (match is not null)
        {
            resolvedType = new Qualident(match.Declaration.Name);
            return true;
        }

        if (typeName.Local == "None")
        {
            resolvedType = new Qualident("Option");
            return true;
        }

        return false;
    }

    private static bool HasZeroArgConstructor(LoweredNominalDeclaration declaration, string constructorName)
    {
        foreach (var ctor in declaration.Constructors)
        {
            if (ctor.Name == constructorName && ctor.Parameters.Count == 0)
            {
                return true;
            }
        }

        return false;
    }

    private void CheckPattern(LoweredPattern pattern, int matchCount)
    {
        var patternCount = pattern.Kind is LoweredPatternKind.Tuple or LoweredPatternKind.Array
            ? pattern.Children.Count
            : -1;

        if (matchCount < 0 || patternCount < 0)
        {
            return;
        }

        if (pattern.Kind == LoweredPatternKind.Tuple && matchCount != patternCount)
        {
            AddWarning(CompilerWarning.PatternNeverMatch, pattern.Location, pattern);
        }
        else if (pattern.Kind == LoweredPatternKind.Array && matchCount < patternCount)
        {
            AddWarning(CompilerWarning.PatternNeverMatch, pattern.Location, pattern);
        }
    }
}

partial class Builder
{
    private readonly record struct BareEnumResolution(
        Qualident TypeName,
        string MemberName,
        bool CallRequired);

    private enum BareEnumConstructorResolution
    {
        NotFound,
        Found,
        Ambiguous
    }

    private BareEnumConstructorResolution TryResolveBareEnumConstructor(string name, int arity, out BareEnumResolution resolution)
    {
        BareEnumResolution? match = null;
        BareEnumResolution? ambiguous = null;

        void AddCandidate(BareEnumResolution candidate)
        {
            if (match is null)
            {
                match = candidate;
            }
            else
            {
                ambiguous ??= candidate;
            }
        }

        if (name == "Some" && arity == 1)
        {
            AddCandidate(new BareEnumResolution(new Qualident("Option"), "Some", true));
        }
        else if (name == "None" && arity == 0)
        {
            AddCandidate(new BareEnumResolution(new Qualident("Option"), "None", false));
        }
        else if (name == "Ok" && arity == 1)
        {
            AddCandidate(new BareEnumResolution(new Qualident("Result"), "Ok", true));
        }
        else if (name == "Err" && arity == 1)
        {
            AddCandidate(new BareEnumResolution(new Qualident("Result"), "Err", true));
        }

        foreach (var type in types.Values)
        {
            if (type.Declaration.Style != TypeDeclarationStyle.Enum)
            {
                continue;
            }

            foreach (var ctor in type.Declaration.Constructors)
            {
                if (ctor.Name != name || ctor.Parameters.Count != arity)
                {
                    continue;
                }

                AddCandidate(new BareEnumResolution(
                    new Qualident(type.Declaration.Name),
                    name,
                    CallRequired: true));
            }
        }

        if (ambiguous is not null)
        {
            resolution = ambiguous.Value;
            return BareEnumConstructorResolution.Ambiguous;
        }

        if (match is null)
        {
            resolution = default;
            return BareEnumConstructorResolution.NotFound;
        }

        resolution = match.Value;
        return BareEnumConstructorResolution.Found;
    }
}
