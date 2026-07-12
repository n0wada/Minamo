using System;
using System.Collections.Generic;

namespace Spellkit.Parser;

internal class Constants
{
    public const int EOF = char.MaxValue + 1;
}

public record struct Location(int Line, int Column)
{
    public bool IsEmpty => Line == 0 && Column == 0;
}

public enum ParserError
{
    None = 0,

    Undefined = 1,

    TokenExpected = 2,

    InvalidStandardOperators = 3,

    InvalidFunctionName = 4,

    InvalidStatement = 5,

    InvalidBinding = 6,

    InvalidExpression = 7,

    InvalidIf = 8,

    InvalidFunctionExpression = 9,

    InvalidUnary = 10,

    InvalidLiteral = 11,

    InvalidEscapeCode = 14,

    SemanticError = 15,

    InvalidPattern = 16,

    InvalidSyntax = 17,

    InvalidCharLiteral = 18,

    InvalidTryCatch = 19,

    InvalidLabel = 20,

    InvalidImport = 21,

    InvalidApplicationArguments = 22,
    
    Deprecated = 23,

    InvalidTypeName = 34,

    InvalidRegion = 35,

    InvalidFunction = 36,

    InvalidYield = 37,

    InvalidRange = 38,

    InvalidIdentifier = 39,

    InvalidApplicationOperator = 40,

    InvalidIndex = 41,

    ExpectedFunction = 42,

    InvalidGuardedStatement = 43,

    InvalidNumber = 44
}

internal sealed class TokenCursor
{
    private readonly Lexer lexer;
    private readonly List<LexToken> tokens = new();
    private int position;

    public TokenCursor(Lexer lexer) => this.lexer = lexer;

    public LexToken Current => Peek();

    public LexToken Previous => position == 0 ? default : tokens[position - 1];

    public bool IsAtEnd => Current.Kind == TokenKind.EndOfFile;

    public int Mark() => position;

    public void Reset(int mark)
    {
        if (mark < 0 || mark > tokens.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(mark));
        }

        position = mark;
    }

    public LexToken Peek(int offset = 0)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var index = position + offset;
        while (tokens.Count <= index)
        {
            var token = lexer.Next();
            tokens.Add(token);
            if (token.Kind == TokenKind.EndOfFile)
            {
                break;
            }
        }

        return index < tokens.Count ? tokens[index] : tokens[^1];
    }

    public LexToken Consume()
    {
        var token = Current;
        if (token.Kind != TokenKind.EndOfFile)
        {
            position++;
        }

        return token;
    }

    public bool Check(TokenKind kind) => Current.Kind == kind;

    public bool Match(TokenKind kind)
    {
        if (!Check(kind))
        {
            return false;
        }

        Consume();
        return true;
    }
}

internal static class TokenFacts
{
    public static string GetDisplayName(TokenKind kind) => kind switch
    {
        TokenKind.EndOfFile => "end of file",
        TokenKind.UpperIdentifier => "upper-case identifier",
        TokenKind.LowerIdentifier => "identifier",
        TokenKind.DirectiveIdentifier => "directive identifier",
        TokenKind.Integer => "integer literal",
        TokenKind.Float => "floating-point literal",
        TokenKind.String => "string literal",
        TokenKind.Character => "character literal",
        TokenKind.VerbatimString => "verbatim string literal",
        TokenKind.Arrow => "=>",
        TokenKind.Dot => ".",
        TokenKind.Comma => ",",
        TokenKind.Semicolon => ";",
        TokenKind.Colon => ":",
        TokenKind.Equal => "=",
        TokenKind.LeftParen => "(",
        TokenKind.RightParen => ")",
        TokenKind.LeftBrace => "{",
        TokenKind.RightBrace => "}",
        TokenKind.LeftBracket => "[",
        TokenKind.RightBracket => "]",
        _ => kind.ToString().ToLowerInvariant()
    };
}

public enum TokenKind
{
    EndOfFile,
    UpperIdentifier,
    LowerIdentifier,
    DirectiveIdentifier,
    Integer,
    Float,
    String,
    Character,
    VerbatimString,
    Use,
    Mut,
    Let,
    Func,
    Return,
    Continue,
    Break,
    Yield,
    If,
    For,
    While,
    Type,
    In,
    Do,
    Arrow,
    Dot,
    Comma,
    Semicolon,
    Colon,
    Equal,
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    LeftBracket,
    RightBracket,
    CoalesceAssign,
    AddAssign,
    SubtractAssign,
    MultiplyAssign,
    DivideAssign,
    RemainderAssign,
    Minus,
    Plus,
    Bang,
    Coalesce,
    Multiply,
    Divide,
    Remainder,
    EqualEqual,
    NotEqual,
    Greater,
    Less,
    GreaterOrEqual,
    LessOrEqual,
    DoubleLess,
    DoubleGreater,
    Static,
    With,
    Region,
    EndRegion,
    Import,
    Private,
    Or,
    Pipe,
    Ellipsis,
    When,
    True,
    False,
    And,
    Get,
    Set,
    As,
    Match,
    Range,
    Not,
    Nil,
    Else,
    Many,
    Throw,
    Question,
    Try,
    Catch,
    Backslash,
    LogicalOr,
    LogicalAnd,
    Is,
    ExclusiveRange,
    Unknown,
    From
}

internal readonly record struct LexToken
{
    private readonly string? source;
    private readonly string? text;
    private readonly int length;

    public LexToken(
        TokenKind kind,
        string source,
        int position,
        int characterPosition,
        int length,
        int line,
        int column,
        bool afterEol)
    {
        Kind = kind;
        this.source = source;
        text = null;
        Position = position;
        CharacterPosition = characterPosition;
        this.length = length;
        Line = line;
        Column = column;
        AfterEol = afterEol;
    }

    public LexToken(
        TokenKind kind,
        string text,
        int position,
        int characterPosition,
        int line,
        int column,
        bool afterEol)
    {
        Kind = kind;
        source = null;
        this.text = text;
        Position = position;
        CharacterPosition = characterPosition;
        length = text.Length;
        Line = line;
        Column = column;
        AfterEol = afterEol;
    }

    public TokenKind Kind { get; }

    public string Text => text ?? (source is null ? string.Empty : source.Substring(Position, length));

    public ReadOnlySpan<char> TextSpan => text is not null
        ? text.AsSpan()
        : source is null
            ? ReadOnlySpan<char>.Empty
            : source.AsSpan(Position, length);

    public int Position { get; }

    public int CharacterPosition { get; }

    public int Line { get; }

    public int Column { get; }

    public bool AfterEol { get; }

    public Location Location => new(Line, Column);
}
