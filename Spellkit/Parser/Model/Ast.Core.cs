using System.Text;
using System.Collections.Generic;
using System.Collections;

namespace Spellkit.Parser.Model;

public abstract class SyntaxNode
{
    public NodeType NodeType { get; }

    public Location Location { get; }

    public bool IsPrivate { get; set; }

    protected SyntaxNode(NodeType type, Location loc) => (NodeType, Location) = (type, loc);

    public override string ToString()
    {
        var sb = new StringBuilder();
        ToString(sb);
        return sb.ToString();
    }

    internal abstract void ToString(StringBuilder sb);
}

public enum NodeType
{
    None,
    Block,

    TestBlock,

    Integer,
    Float,
    String,
    Char,
    Boolean,
    Nil,

    Name,
    Binary,
    Unary,
    Assignment,
    ExpressionStatement,
    Binding,
    ConstDeclaration,
    Rebinding,
    If,
    While,
    For,
    Range,
    Break,
    Continue,
    Return,
    Yield,
    YieldBreak,
    Throw,
    As,

    Application,
    Index,
    Access,
    
    Function,
    Select,
    SelectState,
    SelectChoice,
    Tuple,
    Array,
    Comprehension,
    YieldBlock,
    Label,
    Parameter,

    Type,
    Impl,
    Field,

    Match,
    MatchEntry,
    NamePattern,
    IntegerPattern,
    FloatPattern,
    BooleanPattern,
    CharPattern,
    StringPattern,
    NilPattern,
    NotPattern,
    ArrayPattern,
    RangePattern,
    TuplePattern,
    WildcardPattern,
    TypeTestPattern,
    AndPattern,
    OrPattern,
    CtorPattern,

    TryCatch

    ,Goto
    ,Exit
    ,SelectInvocation
    ,SelectEvent
}

public interface INamedNode
{
    string NodeName { get; }
}

public interface INodeContainer
{
    int NodeCount { get; }
}

internal static class Extensions
{
    public static void ToString(this IEnumerable<SyntaxNode> nodes, StringBuilder sb, string sep = ",")
    {
        var fst = true;

        foreach (var n in nodes)
        {
            if (!fst)
            {
                sb.Append(sep + " ");
            }

            n.ToString(sb);
            fst = false;
        }
    }

    public static string ToSymbol(this BinaryOperator op) =>
        op switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.And => "&&",
            BinaryOperator.Div => "/",
            BinaryOperator.Eq => "==",
            BinaryOperator.Gt => ">",
            BinaryOperator.GtEq => ">=",
            BinaryOperator.Lt => "<",
            BinaryOperator.LtEq => "<=",
            BinaryOperator.Mul => "*",
            BinaryOperator.NotEq => "!=",
            BinaryOperator.Or => "||",
            BinaryOperator.Rem => "%",
            BinaryOperator.ShiftLeft => "<<",
            BinaryOperator.ShiftRight => ">>",
            BinaryOperator.Sub => "-",
            BinaryOperator.Coalesce => "??",
            BinaryOperator.Is => "is",
            _ => "",
        };

    public static string ToSymbol(this UnaryOperator op) =>
        op switch
        {
            UnaryOperator.Neg => "-",
            UnaryOperator.Not => "!",
            UnaryOperator.Plus => "+",
            _ => "",
        };
}

public sealed class Qualident
{
    public string? Parent { get; }

    public string Local { get; }

    internal Qualident(string local) => Local = local;

    internal Qualident(string local, string parent) : this(local) => Parent = parent;

    public bool IsPossibleEquality(Qualident qua)
    {
        if (qua.Parent is not null && Parent is not null)
        {
            return qua.Parent == Parent && qua.Local == Local;
        }

        return Local == qua.Local;
    }

    public override string ToString() => Parent is null ? Local : Parent + "." + Local;

    public override int GetHashCode() => HashCode.Combine(Parent, Local);

    public override bool Equals(object? obj) => obj is Qualident q
        && Parent == q.Parent && Local == q.Local;
}

public sealed class TypeAnnotation : IEnumerable<Qualident>
{
    public Qualident Qualident { get; }

    public IReadOnlyList<TypeAnnotation> TypeArguments { get; }

    public TypeAnnotation? Next { get; }

    public TypeAnnotation(Qualident qualident, TypeAnnotation? next) =>
        (Qualident, TypeArguments, Next) =
        (qualident, Array.Empty<TypeAnnotation>(), next);

    public TypeAnnotation(
        Qualident qualident,
        IReadOnlyList<TypeAnnotation> typeArguments,
        TypeAnnotation? next) =>
        (Qualident, TypeArguments, Next) = (qualident, typeArguments, next);

    public void ToString(StringBuilder sb)
    {
        var ta = this;
        var first = true;

        while (ta is not null)
        {
            if (!first)
            {
                sb.Append(" | ");
            }

            sb.Append(ta.Qualident.ToString());
            if (ta.TypeArguments.Count > 0)
            {
                sb.Append('<');
                for (var i = 0; i < ta.TypeArguments.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    ta.TypeArguments[i].ToString(sb);
                }
                sb.Append('>');
            }
            ta = ta.Next;
            first = false;
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        ToString(sb);
        return sb.ToString();
    }

    public IEnumerator<Qualident> GetEnumerator()
    {
        var ta = this;

        while (ta is not null)
        {
            yield return ta.Qualident;
            ta = ta.Next;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class SpellkitCodeModel
{
    public SpellkitCodeModel(BlockSyntax root, ImportSyntax[] imports, string fileName) =>
        (Root, Imports, FileName) = (root, imports, fileName);

    public ImportSyntax[] Imports { get; }

    public BlockSyntax Root { get; }

    public string FileName { get; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        foreach (var i in Imports)
        {
            sb.AppendLine(i.ToString());
        }

        sb.AppendLine();
        Root.ToString(sb);
        return sb.ToString();
    }
}
