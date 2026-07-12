using System.Text;
using System.Collections.Generic;

namespace Spellkit.Parser.Model;

public sealed class AssignmentSyntax : SyntaxNode
{
    public AssignmentSyntax(Location loc) : base(NodeType.Assignment, loc) { }

    public BinaryOperator? AutoAssign { get; set; }

    public SyntaxNode Target { get; set; } = null!;

    public SyntaxNode Value { get; set; } = null!;

    internal override void ToString(StringBuilder sb)
    {
        Target.ToString(sb);

        if (AutoAssign is not null)
        {
            sb.Append(' ');
            sb.Append(AutoAssign.Value.ToSymbol());
            sb.Append("= ");
            Value.ToString(sb);
        }
        else
        {
            sb.Append(" = ");
            Value.ToString(sb);
        }
    }
}

public abstract class BindingBaseSyntax : SyntaxNode
{
    protected BindingBaseSyntax(NodeType type, Location loc) : base(type, loc) { }

    public PatternSyntax Pattern { get; internal set; } = null!;

    public TypeAnnotation? TypeAnnotation { get; set; }

    public SyntaxNode Init { get; set; } = null!;
}

public sealed class BindingSyntax : BindingBaseSyntax
{
    public BindingSyntax(Location loc) : base(NodeType.Binding, loc) { }

    public bool AutoClose { get; set; }

    public bool Constant { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        if (IsPrivate)
        {
            sb.Append("private ");
        }

        if (AutoClose)
        {
            sb.Append("use ");
        }
        else
        {
            sb.Append(Constant ? "let " : "mut ");
        }

        Pattern.ToString(sb);

        if (TypeAnnotation is not null)
        {
            sb.Append(": ");
            sb.Append(TypeAnnotation.ToString());
        }

        if (Init is not null)
        {
            sb.Append(" = ");
            Init.ToString(sb);
        }
    }
}

public sealed class BlockSyntax : SyntaxNode
{
    public BlockSyntax(Location loc) : base(NodeType.Block, loc) { }

    public List<SyntaxNode> Nodes { get; } = new();

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("{ ");
        Nodes.ToString(sb, "");
        sb.Append(" } ");
    }
}

public sealed class ConstDeclarationSyntax : SyntaxNode
{
    public ConstDeclarationSyntax(Location loc) : base(NodeType.ConstDeclaration, loc) { }

    public List<BindingSyntax> Declarations { get; } = new();

    internal override void ToString(StringBuilder sb)
    {
        if (IsPrivate)
        {
            sb.Append("private ");
        }

        if (Declarations.Count == 1)
        {
            sb.Append("const ");
            Declarations[0].Pattern.ToString(sb);
            if (Declarations[0].Init is not null)
            {
                sb.Append(" = ");
                Declarations[0].Init.ToString(sb);
            }
            return;
        }

        sb.Append("const { ");
        for (var i = 0; i < Declarations.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            Declarations[i].Pattern.ToString(sb);
            if (Declarations[i].Init is not null)
            {
                sb.Append(" = ");
                Declarations[i].Init.ToString(sb);
            }
        }
        sb.Append(" }");
    }
}

public sealed class ExpressionStatementSyntax : SyntaxNode
{
    public ExpressionStatementSyntax(Location loc) : base(NodeType.ExpressionStatement, loc) { }

    public SyntaxNode Expression { get; init; } = null!;

    internal override void ToString(StringBuilder sb) => Expression.ToString(sb);
}

public sealed class RebindingSyntax : BindingBaseSyntax
{
    public RebindingSyntax(Location loc) : base(NodeType.Rebinding, loc) { }

    internal override void ToString(StringBuilder sb)
    {
        Pattern.ToString(sb);
        sb.Append(" = ");
        Init.ToString(sb);
    }
}

public sealed class RegionSyntax : SyntaxNode
{
    public string? GlobalError { get; set; }

    public string? FileName { get; set; }

    public string Name { get; }

    public SpkCodeModel Body { get; }

    public RegionSyntax(string name, SpkCodeModel body, Location loc) : base(NodeType.TestBlock, loc) =>
        (Name, Body) = (name, body);

    internal override void ToString(StringBuilder sb)
    {
        sb.AppendLine($"#region {Name}");
        sb.Append(Body.ToString());
        sb.AppendLine("#endregion");
    }
}
