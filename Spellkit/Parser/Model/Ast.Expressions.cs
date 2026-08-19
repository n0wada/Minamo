using System.Text;
using System.Collections.Generic;

namespace Spellkit.Parser.Model;

public enum BinaryOperator
{
    Or,
    And,
    Gt,
    Lt,
    GtEq,
    LtEq,
    Eq,
    NotEq,
    Add,
    Sub,
    Mul,
    Div,
    Rem,
    ShiftLeft,
    ShiftRight,
    Coalesce,
    Is,
    In
}

public enum UnaryOperator
{
    None,
    Not,
    Neg,
    Plus
}

public sealed class AccessSyntax : SyntaxNode, INamedNode
{
    public AccessSyntax(Location loc) : base(NodeType.Access, loc) { }

    public SyntaxNode Target { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string NodeName => Name;

    public bool SpecialName { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        Target.ToString(sb);
        sb.Append('.');

        if (SpecialName)
        {
            sb.Append("[" + Name + "]");
        }
        else
        {
            sb.Append(Name);
        }
    }
}

public sealed class ApplicationSyntax : SyntaxNode
{
    public ApplicationSyntax(SyntaxNode target, Location loc) : base(NodeType.Application, loc) =>
        Target = target;

    public SyntaxNode Target { get; internal set; }

    public List<SyntaxNode> Arguments { get; } = new();

    internal override void ToString(StringBuilder sb)
    {
        Target.ToString(sb);
        sb.Append('(');
        Arguments.ToString(sb);
        sb.Append(')');
    }
}

public sealed class ArrayLiteralSyntax : SyntaxNode, INodeContainer
{
    public ArrayLiteralSyntax(Location loc) : base(NodeType.Array, loc) { }

    public List<SyntaxNode> Elements { get; } = new();

    internal bool IsDictionaryLiteral { get; set; }

    public int NodeCount => 
        Elements.Count == 1 && Elements[0].NodeType == NodeType.Range ? -1 : Elements.Count;

    internal override void ToString(StringBuilder sb)
    {
        sb.Append('[');
        Elements.ToString(sb);
        sb.Append(']');
    }
}

public sealed class AsSyntax : SyntaxNode
{
    public AsSyntax(Location loc) : base(NodeType.As, loc) { }

    public SyntaxNode Expression { get; set; } = null!;

    public Qualident TypeName { get; set; } = null!;

    internal override void ToString(StringBuilder sb)
    {
        Expression?.ToString(sb);
        sb.Append(" as ");
        sb.Append(TypeName);
    }
}

public sealed class BinaryOperationSyntax : SyntaxNode
{
    public BinaryOperationSyntax(Location loc) : base(NodeType.Binary, loc) { }

    public BinaryOperationSyntax(SyntaxNode left, SyntaxNode right, BinaryOperator op, Location loc) : this(loc) =>
        (Left, Right, Operator) = (left, right, op);

    public SyntaxNode Left { get; set; } = null!;

    public SyntaxNode Right { get; set; } = null!;

    public BinaryOperator Operator { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        Left.ToString(sb);
        sb.Append(' ');
        sb.Append(Operator.ToSymbol());
        sb.Append(' ');
        Right.ToString(sb);
    }
}

public sealed class BooleanLiteralSyntax : SyntaxNode
{
    public BooleanLiteralSyntax(Location loc) : base(NodeType.Boolean, loc) { }

    public bool Value { get; set; }

    internal override void ToString(StringBuilder sb) =>
        sb.Append(Value ? "true" : "false");
}

public sealed class CharLiteralSyntax : SyntaxNode
{
    public CharLiteralSyntax(Location loc) : base(NodeType.Char, loc) { }

    public char Value { get; set; }

    internal override void ToString(StringBuilder sb) =>
        sb.Append(StringUtil.Escape(Value.ToString(), "'"));
}

public sealed class ComprehensionSyntax : SyntaxNode
{
    public ComprehensionSyntax(Location loc) : base(NodeType.Comprehension, loc) { }

    public SyntaxNode? Key { get; set; }

    public SyntaxNode Value { get; set; } = null!;

    public PatternSyntax Pattern { get; set; } = null!;

    public SyntaxNode Target { get; set; } = null!;

    public SyntaxNode? Guard { get; set; }

    public bool IsDictionary => Key is not null;

    internal override void ToString(StringBuilder sb)
    {
        sb.Append('[');

        if (Key is not null)
        {
            Key.ToString(sb);
            sb.Append(": ");
        }

        Value.ToString(sb);
        sb.Append(" for ");
        Pattern.ToString(sb);
        sb.Append(" in ");
        Target.ToString(sb);

        if (Guard is not null)
        {
            sb.Append(" when ");
            Guard.ToString(sb);
        }

        sb.Append(']');
    }
}

public sealed class FloatLiteralSyntax : SyntaxNode
{
    public FloatLiteralSyntax(Location loc) : base(NodeType.Float, loc) { }

    public double Value { get; set; }

    internal override void ToString(StringBuilder sb) =>
        sb.Append(Value.ToString(InvariantCulture.NumberFormat));
}

public sealed class IndexerSyntax : SyntaxNode
{
    public IndexerSyntax(Location loc) : base(NodeType.Index, loc) { }

    public SyntaxNode Target { get; set; } = null!;

    public SyntaxNode Index { get; set; } = null!;

    internal override void ToString(StringBuilder sb)
    {
        Target.ToString(sb);
        sb.Append('[');
        Index.ToString(sb);
        sb.Append(']');
    }
}

public sealed class IntegerLiteralSyntax : SyntaxNode
{
    public IntegerLiteralSyntax(Location loc) : base(NodeType.Integer, loc) { }

    public long Value { get; set; }

    internal override void ToString(StringBuilder sb) =>
        sb.Append(Value.ToString(InvariantCulture.NumberFormat));
}

public sealed class LabelLiteralSyntax : SyntaxNode, INamedNode
{
    public LabelLiteralSyntax(Location loc) : base(NodeType.Label, loc) { }
    
    public bool Mutable { get; init; }

    public string Label { get; init; } = null!;

    public bool FromString{ get; init; }

    public SyntaxNode Expression { get; init; } = null!;

    public string NodeName => Label;

    internal override void ToString(StringBuilder sb)
    {
        if (Mutable)
        {
            sb.Append("mut ");
        }

        if (FromString)
        {
            sb.Append(StringUtil.Escape(Label));
        }
        else
        {
            sb.Append(Label);
        }

        sb.Append(": ");
        Expression?.ToString(sb);
    }
}

public sealed class NameSyntax : SyntaxNode, INamedNode
{
    public NameSyntax(Location loc) : base(NodeType.Name, loc) { }

    public string Value { get; set; } = null!;

    public string NodeName => Value;

    internal override void ToString(StringBuilder sb) => sb.Append(Value);
}

public sealed class NilLiteralSyntax : SyntaxNode
{
    public NilLiteralSyntax(Location loc) : base(NodeType.Nil, loc) { }

    internal override void ToString(StringBuilder sb) => sb.Append("nil");
}

public sealed class RangeSyntax : SyntaxNode
{
    public RangeSyntax(Location loc) : base(NodeType.Range, loc) { }

    public bool Exclusive { get; set; }

    public SyntaxNode? From { get; set; }

    public SyntaxNode? To { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        From?.ToString(sb);
        
        if (Exclusive)
        {
            sb.Append('<');
        }

        sb.Append("..");
        To?.ToString(sb);
    }
}

public sealed class StringLiteralSyntax : SyntaxNode
{
    public StringLiteralSyntax(Location loc) : base(NodeType.String, loc) { }

    public string? Value { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        if (Value is not null)
        {
            sb.Append(StringUtil.Escape(Value));
        }
    }
}

public sealed class TupleLiteralSyntax : SyntaxNode, INodeContainer
{
    public TupleLiteralSyntax(Location loc) : base(NodeType.Tuple, loc) { }

    public List<SyntaxNode> Elements { get; } = new();

    public int NodeCount => Elements.Count;

    internal override void ToString(StringBuilder sb)
    {
        sb.Append('(');
        Elements.ToString(sb);
        sb.Append(')');
    }
}

public sealed class UnaryOperationSyntax : SyntaxNode
{
    public UnaryOperationSyntax(Location loc) : base(NodeType.Unary, loc) { }

    public UnaryOperationSyntax(SyntaxNode node, UnaryOperator op, Location loc) : this(loc) =>
        (Node, Operator) = (node, op);

    public SyntaxNode Node { get; set; } = null!;

    public UnaryOperator Operator { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append(Operator.ToSymbol());
        Node.ToString(sb);
    }
}
