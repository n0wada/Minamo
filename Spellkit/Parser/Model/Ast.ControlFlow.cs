using System.Text;
using System.Collections.Generic;

namespace Spellkit.Parser.Model;

public sealed class BreakSyntax : SyntaxNode
{
    public BreakSyntax(Location loc) : base(NodeType.Break, loc) { }

    public SyntaxNode? Expression { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("break");

        if (Expression is not null)
        {
            sb.Append(' ');
            Expression.ToString(sb);
        }
    }
}

public sealed class ContinueSyntax : SyntaxNode
{
    public ContinueSyntax(Location loc) : base(NodeType.Continue, loc) { }

    internal override void ToString(StringBuilder sb) => sb.Append("continue");
}

public sealed class ForSyntax : SyntaxNode
{
    public ForSyntax(Location loc) : base(NodeType.For, loc) { }

    public PatternSyntax Pattern { get; set; } = null!;

    public SyntaxNode Target { get; set; } = null!;

    public SyntaxNode? Guard { get; set; }

    public SyntaxNode Body { get; set; } = null!;

    public SyntaxNode? Else { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("for ");
        Pattern.ToString(sb);
        sb.Append(" in ");
        Target.ToString(sb);

        if (Guard is not null)
        {
            sb.Append(" when ");
            Guard.ToString(sb);
        }

        Body.ToString(sb);

        if (Else is not null)
        {
            sb.Append(" else ");
            Else.ToString(sb);
        }
    }
}

public sealed class IfSyntax : SyntaxNode
{
    public IfSyntax(Location loc, bool isExpression) : base(NodeType.If, loc) =>
        IsExpression = isExpression;

    public SyntaxNode Condition { get; set; } = null!;

    public SyntaxNode True { get; set; } = null!;

    public SyntaxNode? False { get; set; }

    public bool IsExpression { get; }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("if ");
        Condition.ToString(sb);
        sb.Append(" { ");
        True.ToString(sb);
        sb.Append(" }");

        if (False is not null)
        {
            sb.Append("else { ");
            False.ToString(sb);
            sb.Append(" }");
        }
    }
}

public sealed class MatchSyntax : SyntaxNode
{
    public MatchSyntax(Location loc) : base(NodeType.Match, loc) { }

    public SyntaxNode? Expression { get; set; }

    public List<MatchEntrySyntax> Entries { get; } = new();

    internal override void ToString(StringBuilder sb)
    {
        if (Expression is not null)
        {
            sb.Append("match ");
            Expression.ToString(sb);
            sb.Append(' ');
        }

        sb.Append('{');

        foreach (var e in Entries)
        {
            e.ToString(sb);
            sb.Append(',');
        }

        sb.Append('}');
    }
}

public sealed class MatchEntrySyntax : SyntaxNode
{
    public MatchEntrySyntax(Location loc) : base(NodeType.MatchEntry, loc) { }

    public PatternSyntax Pattern { get; set; } = null!;

    public SyntaxNode? Guard { get; set; }

    public SyntaxNode Expression { get; set; } = null!;

    internal override void ToString(StringBuilder sb)
    {
        Pattern?.ToString(sb);

        if (Guard is not null)
        {
            sb.Append(" when ");
            Guard.ToString(sb);
        }

        sb.Append(" => ");
        Expression?.ToString(sb);
    }
}

public sealed class ReturnSyntax : SyntaxNode
{
    public ReturnSyntax(Location loc) : base(NodeType.Return, loc) { }

    public SyntaxNode? Expression { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("return");

        if (Expression is not null)
        {
            sb.Append(' ');
            Expression.ToString(sb);
        }
    }
}

public sealed class GotoSyntax : SyntaxNode
{
    public GotoSyntax(Location loc) : base(NodeType.Goto, loc) { }

    public string State { get; set; } = null!;

    public List<SyntaxNode> Arguments { get; } = new();

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("goto ").Append(State);
        if (Arguments.Count > 0)
        {
            sb.Append('(');
            Arguments.ToString(sb);
            sb.Append(')');
        }
    }
}

public sealed class ExitSyntax : SyntaxNode
{
    public ExitSyntax(Location loc) : base(NodeType.Exit, loc) { }

    public SyntaxNode? Expression { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("exit");
        if (Expression is not null)
        {
            sb.Append(' ');
            Expression.ToString(sb);
        }
    }
}

public sealed class ThrowSyntax : SyntaxNode
{
    public ThrowSyntax(Location loc) : base(NodeType.Throw, loc) { }

    public SyntaxNode? Expression { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("throw ");
        Expression?.ToString(sb);
    }
}

public sealed class TryCatchSyntax : SyntaxNode
{
    public TryCatchSyntax(Location loc) : base(NodeType.TryCatch, loc) { }

    public SyntaxNode Expression { get; set; } = null!;

    public SyntaxNode? Catch { get; set; }

    public NameSyntax? BindVariable { get; set; }

    public SyntaxNode? Finally { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("try ");
        Expression.ToString(sb);

        if (Catch is not null)
        {
            sb.Append("catch ");

            if (BindVariable is not null)
            {
                BindVariable.ToString(sb);
                sb.Append(' ');
            }

            Catch.ToString(sb);
        }

        if (Finally is not null)
        {
            sb.Append(" finally ");
            Finally.ToString(sb);
        }
    }
}

public sealed class WhileSyntax : SyntaxNode
{
    public WhileSyntax(Location loc) : base(NodeType.While, loc) { }

    public SyntaxNode Condition { get; set; } = null!;

    public SyntaxNode Body { get; set; } = null!;

    public bool DoWhile { get; set; }

    internal override void ToString(StringBuilder sb)
    {
        if (DoWhile)
        {
            sb.Append("do ");
            Body.ToString(sb);
            sb.Append(" while ");
            Condition.ToString(sb);
        }
        else
        {
            sb.Append("while ");
            Condition.ToString(sb);
            Body.ToString(sb);
        }
    }
}

public sealed class SelectInvocationSyntax : SyntaxNode
{
    public SelectInvocationSyntax(Location loc) : base(NodeType.SelectInvocation, loc) { }

    public SyntaxNode Target { get; set; } = null!;

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("do ");
        Target.ToString(sb);
    }
}

public sealed class YieldSyntax : SyntaxNode
{
    public YieldSyntax(Location loc) : base(NodeType.Yield, loc) { }

    public SyntaxNode Expression { get; set; } = null!;

    internal override void ToString(StringBuilder sb)
    {
        sb.Append("yield ");
        Expression.ToString(sb);
    }
}

public sealed class YieldBlockSyntax : SyntaxNode
{
    public YieldBlockSyntax(Location loc) : base(NodeType.YieldBlock, loc) { }

    public List<SyntaxNode> Elements { get; } = new();

    internal override void ToString(StringBuilder sb) => Elements.ToString(sb);
}

public sealed class YieldBreakSyntax : SyntaxNode
{
    public YieldBreakSyntax(Location loc) : base(NodeType.YieldBreak, loc) { }

    internal override void ToString(StringBuilder sb) => sb.Append("yield break");
}
