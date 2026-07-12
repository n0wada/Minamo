using System.Collections.Generic;
using System.Text;

namespace Spellkit.Parser.Model;

public abstract class PatternSyntax : SyntaxNode
{
    protected PatternSyntax(Location loc, NodeType type) : base(type, loc) { }
}

public sealed class NamePatternSyntax : PatternSyntax, INamedNode
{
    public NamePatternSyntax(Location loc) : base(loc, NodeType.NamePattern) { }

    public string Name { get; set; } = null!;

    public string NodeName => Name;

    internal bool IsConstructor { get; set; }

    internal override void ToString(StringBuilder sb) => sb.Append(Name);
}

public sealed class IntegerPatternSyntax : PatternSyntax
{
    public IntegerPatternSyntax(Location loc) : base(loc, NodeType.IntegerPattern) { }

    public long Value { get; set; }

    internal override void ToString(StringBuilder sb) =>
        sb.Append(Value.ToString(InvariantCulture.NumberFormat));

    public override int GetHashCode() => Value.GetHashCode();

    public override bool Equals(object? obj) => obj is IntegerPatternSyntax i && i.Value == Value;
}

public sealed class FloatPatternSyntax : PatternSyntax
{
    public FloatPatternSyntax(Location loc) : base(loc, NodeType.FloatPattern) { }

    public double Value { get; set; }

    internal override void ToString(StringBuilder sb) =>
        sb.Append(Value.ToString(InvariantCulture.NumberFormat));

    public override int GetHashCode() => Value.GetHashCode();

    public override bool Equals(object? obj) => obj is FloatPatternSyntax f && f.Value == Value;
}

public sealed class BooleanPatternSyntax : PatternSyntax
{
    public BooleanPatternSyntax(Location loc) : base(loc, NodeType.BooleanPattern) { }

    public bool Value { get; set; }

    internal override void ToString(StringBuilder sb) => sb.Append(Value ? "true" : "false");

    public override int GetHashCode() => Value.GetHashCode();

    public override bool Equals(object? obj) => obj is BooleanPatternSyntax b && b.Value == Value;
}

public sealed class CharPatternSyntax : PatternSyntax
{
    public CharPatternSyntax(Location loc) : base(loc, NodeType.CharPattern) { }

    public char Value { get; set; }

    internal override void ToString(StringBuilder sb) => sb.Append(StringUtil.Escape(Value.ToString(), quote: "'"));

    public override int GetHashCode() => Value.GetHashCode();

    public override bool Equals(object? obj) => obj is CharPatternSyntax c && c.Value == Value;
}

public sealed class StringPatternSyntax : PatternSyntax
{
    public StringPatternSyntax(Location loc) : base(loc, NodeType.StringPattern) { }

    public StringLiteralSyntax Value { get; set; } = null!;

    internal override void ToString(StringBuilder sb) => Value.ToString(sb);

    public override int GetHashCode() => Value.GetHashCode();

    public override bool Equals(object? obj) => obj is StringPatternSyntax s && s.Value.Value == Value.Value;
}

public sealed class NotPatternSyntax : PatternSyntax
{
    public NotPatternSyntax(Location loc) : base(loc, NodeType.NotPattern) { }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("not ");
        Pattern.ToString(sb);
    }

    public PatternSyntax Pattern { get; set; } = null!;

    public override int GetHashCode() => HashCode.Combine("not", Pattern);

    public override bool Equals(object? obj) => obj is NotPatternSyntax p && Pattern.Equals(p.Pattern);
}

public sealed class NilPatternSyntax : PatternSyntax
{
    public NilPatternSyntax(Location loc) : base(loc, NodeType.NilPattern) { }

    internal override void ToString(StringBuilder sb) => sb.Append("nil");

    public override int GetHashCode() => 0;

    public override bool Equals(object? obj) => obj is NilPatternSyntax;
}

public abstract class SequencePatternSyntax : PatternSyntax, INodeContainer
{
    protected SequencePatternSyntax(Location loc, NodeType nodeType) : base(loc, nodeType) { }

    public List<SyntaxNode> Elements { get; } = new List<SyntaxNode>();

    public int NodeCount => Elements.Count;
}

public sealed class TuplePatternSyntax : SequencePatternSyntax
{
    public TuplePatternSyntax(Location loc) : base(loc, NodeType.TuplePattern) { }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append('(');
        Elements.ToString(sb);
        sb.Append(')');
    }
}

public sealed class ArrayPatternSyntax : SequencePatternSyntax
{
    public ArrayPatternSyntax(Location loc) : base(loc, NodeType.ArrayPattern) { }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append('[');
        Elements.ToString(sb);
        sb.Append(']');
    }
}

public sealed class RangePatternSyntax : PatternSyntax
{
    public RangePatternSyntax(Location loc) : base(loc, NodeType.RangePattern) { }

    public PatternSyntax From { get; set; } = null!;

    public PatternSyntax To { get; set; } = null!;

    internal override void ToString(StringBuilder sb)
    {
        From.ToString(sb);
        sb.Append("..");
        To.ToString(sb);
    }
}

public sealed class WildcardPatternSyntax : PatternSyntax
{
    public WildcardPatternSyntax(Location loc) : base(loc, NodeType.WildcardPattern) { }

    internal override void ToString(StringBuilder sb) => sb.Append('_');
}

public sealed class TypeTestPatternSyntax : PatternSyntax
{
    public TypeTestPatternSyntax(Location loc) : base(loc, NodeType.TypeTestPattern) { }

    public Qualident TypeName { get; set; } = null!;

    public bool AllowTypeCheck { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append(TypeName?.ToString());
    }
}

public sealed class AndPatternSyntax : PatternSyntax
{
    public AndPatternSyntax(Location loc) : base(loc, NodeType.AndPattern) { }

    public PatternSyntax Left { get; set; } = null!;

    public PatternSyntax Right { get; set; } = null!;

    internal override void ToString(StringBuilder sb)
    {
        Left.ToString(sb);
        sb.Append(" and ");
        Right.ToString(sb);
    }
}

public sealed class OrPatternSyntax : PatternSyntax
{
    public OrPatternSyntax(Location loc) : base(loc, NodeType.OrPattern) { }

    public PatternSyntax Left { get; set; } = null!;

    public PatternSyntax Right { get; set; } = null!;

    internal override void ToString(StringBuilder sb)
    {
        Left.ToString(sb);
        sb.Append(" or ");
        Right.ToString(sb);
    }
}

public sealed class ConstructorPatternSyntax : PatternSyntax
{
    public ConstructorPatternSyntax(Location loc) : base(loc, NodeType.CtorPattern) { }

    public string Constructor { get; set; } = null!;

    public Qualident? TypeName { get; set; }

    public List<SyntaxNode> Arguments { get; } = new();

    internal override void ToString(StringBuilder sb)
    {
        if (TypeName is not null)
        {
            sb.Append(TypeName);
            sb.Append('.');
        }

        sb.Append(Constructor);
        sb.Append('(');
        if (Arguments != null && Arguments.Count > 0)
        {
            Arguments.ToString(sb);
        }

        sb.Append(')');
    }
}
