using System.Text;
using System.Collections.Generic;

namespace Spellkit.Parser.Model;

public sealed class FieldDeclarationSyntax : SyntaxNode, INamedNode
{
    public FieldDeclarationSyntax(Location loc) : base(NodeType.Field, loc) { }

    public string Name { get; set; } = null!;

    public string NodeName => Name;

    internal override void ToString(StringBuilder sb) => sb.Append(Name);
}

public sealed class FunctionDeclarationSyntax : SyntaxNode
{
    public FunctionDeclarationSyntax(Location loc) : base(NodeType.Function, loc) { }

    public Qualident? TypeName { get; set; }

    public Qualident? TargetTypeName { get; set; }

    public string? Name { get; set; }

    internal bool IsStatic { get; set; }

    internal bool IsIndexer { get; set; }

    internal bool IsConstructor { get; set; }

    public bool Getter { get; set; }

    public bool Setter { get; set; }

    public bool IsIterator { get; set; }

    internal bool IsImplInitializer { get; set; }

    public List<ParameterSyntax> Parameters { get; } = new();

    public TypeAnnotation? ReturnTypeAnnotation { get; set; }

    public SyntaxNode? Body { get; set; }

    public bool IsVariadic()
    {
        for (var i = 0; i < Parameters.Count; i++)
        {
            if (Parameters[i].IsVarArgs)
            {
                return true;
            }
        }

        return false;
    }

    internal override void ToString(StringBuilder sb)
    {
        if (IsPrivate)
        {
            sb.Append("private ");
        }

        if (Body is null)
        {
            sb.Append(Name);
            sb.Append('(');
            Parameters.ToString(sb);
            sb.Append(')');

            if (ReturnTypeAnnotation is not null)
            {
                sb.Append(": ");
                sb.Append(ReturnTypeAnnotation.ToString());
            }

            return;
        }

        if (IsStatic)
        {
            sb.Append("static ");
        }

        if (Name is not null)
        {
            sb.Append("func ");
        }

        if (Getter)
        {
            sb.Append("get ");
        }

        if (Setter)
        {
            sb.Append("set ");
        }

        if (TypeName is not null)
        {
            sb.Append(TypeName);

            if (Name is not null)
            {
                sb.Append('.');
            }
        }

        if (Name is not null)
        {
            sb.Append(Name);
        }

        if (TargetTypeName is not null)
        {
            sb.Append(" as ");
            sb.Append(TargetTypeName);
            Body?.ToString(sb);
            return;
        }

        if (IsIndexer)
        {
            sb.Append('[');
        }
        else if (Name is not null || Parameters.Count > 1)
        {
            sb.Append('(');
        }

        Parameters.ToString(sb);

        if (IsIndexer)
        {
            sb.Append(']');
        }
        else if (Name is not null || Parameters.Count > 1)
        {
            sb.Append(") ");
        }

        if (ReturnTypeAnnotation is not null)
        {
            sb.Append(": ");
            sb.Append(ReturnTypeAnnotation.ToString());
            sb.Append(' ');
        }

        if (Name is null)
        {
            sb.Append(" => ");
        }

        Body?.ToString(sb);
    }
}

public sealed class ImplDeclarationSyntax : SyntaxNode
{
    public ImplDeclarationSyntax(Location loc) : base(NodeType.Impl, loc) { }

    public string TargetName { get; set; } = null!;

    public List<Qualident> Mixins { get; } = new();

    public List<SyntaxNode> Members { get; } = new();

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("impl ");
        sb.Append(TargetName);

        if (Mixins.Count > 0)
        {
            sb.Append(" with ");

            for (var i = 0; i < Mixins.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(Mixins[i].ToString());
            }
        }

        sb.Append(" {");

        if (Members.Count > 0)
        {
            sb.Append(' ');

            for (var i = 0; i < Members.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }

                Members[i].ToString(sb);
            }

            sb.Append(' ');
        }

        sb.Append('}');
    }
}

public sealed class ImportSyntax
{
    public ImportSyntax(Location loc) => Location = loc;

    public ImportKind Kind { get; set; }

    public string? Alias { get; set; }

    public string? SymbolName { get; set; }

    public string ModuleName { get; set; } = null!;

    public string? LocalPath { get; set; }

    public Location Location { get; }

    public override string ToString()
    {
        var path = LocalPath is null ? "" : $"{LocalPath}/";
        var module = $"{path}{ModuleName}";
        return Kind switch
        {
            ImportKind.All => $"import * from {module}",
            ImportKind.Symbol => $"import {SymbolName} from {module}",
            _ => Alias is null ? $"import {module}" : $"import {module} as {Alias}"
        };
    }
}

public enum ImportKind
{
    Module,
    All,
    Symbol
}

public class ParameterSyntax : SyntaxNode, INamedNode
{
    public ParameterSyntax(Location loc) : base(NodeType.Parameter, loc) { }

    public string Name { get; set; } = null!;

    public SyntaxNode? DefaultValue { get; set; }

    public TypeAnnotation? TypeAnnotation { get; set; }

    public bool IsVarArgs { get; set; }

    public string NodeName => Name;

    internal override void ToString(StringBuilder sb)
    {
        sb.Append(Name);

        if (TypeAnnotation is not null)
        {
            sb.Append(": ");
            sb.Append(TypeAnnotation.ToString());
        }

        if (DefaultValue is not null)
        {
            sb.Append(" = ");
            DefaultValue.ToString(sb);
        }

        if (IsVarArgs)
        {
            sb.Append("...");
        }
    }
}

public sealed class TypeParameterSyntax : ParameterSyntax
{
    public TypeParameterSyntax(Location loc) : base(loc) { }
    
    public bool Mutable { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        if (Mutable)
        {
            sb.Append("mut ");
        }

        base.ToString(sb);
    }
}

public sealed class TypeDeclarationSyntax : SyntaxNode
{
    internal static readonly TypeDeclarationSyntax Default = new(default);

    public TypeDeclarationSyntax(Location loc) : base(NodeType.Type, loc) { }

    public TypeDeclarationStyle Style { get; set; }

    public string Name { get; set; } = null!;

    public List<FunctionDeclarationSyntax> Constructors { get; } = new();

    public List<FunctionDeclarationSyntax> Contracts { get; } = new();

    public List<Qualident>? Mixins { get; internal set; }

    public List<BindingSyntax> PrivateFields { get; } = new();

    public List<FunctionDeclarationSyntax> ProtectedMethods { get; } = new();

    public FunctionDeclarationSyntax? Initializer { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        if (IsPrivate)
        {
            sb.Append("private ");
        }

        sb.Append(Style switch
        {
            TypeDeclarationStyle.Enum => "enum ",
            TypeDeclarationStyle.Struct => "struct ",
            TypeDeclarationStyle.Trait => "trait ",
            _ => "type "
        });
        sb.Append(Name);

        if (Style is TypeDeclarationStyle.Struct or TypeDeclarationStyle.Type
            && Constructors.Count == 1
            && Constructors[0].Name == Name
            && Constructors[0].IsConstructor)
        {
            sb.Append(Style == TypeDeclarationStyle.Struct ? " { " : "(");
            Constructors[0].Parameters.ToString(sb);
            sb.Append(Style == TypeDeclarationStyle.Struct ? " }" : ")");
        }
        else if (Constructors.Count > 0)
        {
            sb.Append(Style == TypeDeclarationStyle.Enum ? " { " : " = ");
        }

        var fst = true;

        foreach (var c in Constructors)
        {
            if (!fst)
            {
                sb.Append(Style == TypeDeclarationStyle.Enum ? ", " : " or ");
            }

            sb.Append(c.Name);
            if (Style != TypeDeclarationStyle.Enum || c.Parameters.Count > 0)
            {
                sb.Append('(');
                c.Parameters.ToString(sb);
                sb.Append(')');
            }

            fst = false;
        }

        if (Style == TypeDeclarationStyle.Enum && Constructors.Count > 0)
        {
            sb.Append(" }");
        }

        if (Style == TypeDeclarationStyle.Trait)
        {
            sb.Append(" {");

            if (Contracts.Count > 0)
            {
                sb.Append(' ');

                for (var i = 0; i < Contracts.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(' ');
                    }

                    Contracts[i].ToString(sb);
                }

                sb.Append(' ');
            }

            sb.Append('}');
        }

        if (Mixins is not null)
        {
            sb.Append(" with ");
            for (var i = 0; i < Mixins.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(Mixins[i].ToString());
            }
        }
    }
}

public enum TypeDeclarationStyle
{
    Type,
    Struct,
    Enum,
    Trait
}
