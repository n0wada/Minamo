using System;
using System.Collections.Generic;

namespace Spellkit.Parser;

internal sealed class Lexer
{
    private readonly string source;
    private int position;
    private int line = 1;
    private int column = 1;
    private LexToken? pendingInvalidToken;

    public Lexer(SourceBuffer buffer) : this(ReadSource(buffer)) { }

    private Lexer(string source) => this.source = source;

    public LexToken Next()
    {
        var afterEol = SkipTrivia();
        if (pendingInvalidToken is { } invalid)
        {
            pendingInvalidToken = null;
            return invalid;
        }
        var start = position;
        var startLine = line;
        var startColumn = column;

        if (IsAtEnd)
        {
            return CreateToken(TokenKind.EndOfFile, start, startLine, startColumn, afterEol);
        }

        if (Current == '"' && Matches("\"\"\""))
        {
            return ScanVerbatimString(start, startLine, startColumn, afterEol);
        }

        if (Current == '"')
        {
            return ScanQuoted(TokenKind.String, '"', start, startLine, startColumn, afterEol);
        }

        if (Current == '\'')
        {
            return ScanQuoted(TokenKind.Character, '\'', start, startLine, startColumn, afterEol);
        }

        if (Current == '#' && IsIdentifierStart(Peek(1)))
        {
            return ScanIdentifier(start, startLine, startColumn, afterEol, directive: true);
        }

        if (IsIdentifierStart(Current))
        {
            return ScanIdentifier(start, startLine, startColumn, afterEol, directive: false);
        }

        if (char.IsDigit(Current) || Current == '.' && char.IsDigit(Peek(1)))
        {
            return ScanNumber(start, startLine, startColumn, afterEol);
        }

        if (TryScanOperator(out var operatorKind))
        {
            return CreateToken(operatorKind, start, startLine, startColumn, afterEol);
        }

        Advance();
        return CreateToken(TokenKind.Unknown, start, startLine, startColumn, afterEol);
    }

    private bool TryScanOperator(out TokenKind kind)
    {
        switch (Current)
        {
            case '&':
                if (Peek(1) == '&')
                {
                    return ScanOperator(TokenKind.LogicalAnd, 2, out kind);
                }

                break;
            case '|':
                if (Peek(1) == '|')
                {
                    return ScanOperator(TokenKind.LogicalOr, 2, out kind);
                }

                return ScanOperator(TokenKind.Pipe, 1, out kind);
            case '<':
                if (Peek(1) == '<')
                {
                    return ScanOperator(TokenKind.DoubleLess, 2, out kind);
                }

                return ScanOperator(Peek(1) == '=' ? TokenKind.LessOrEqual : TokenKind.Less, Peek(1) == '=' ? 2 : 1, out kind);
            case '>':
                if (Peek(1) == '>')
                {
                    return ScanOperator(TokenKind.DoubleGreater, 2, out kind);
                }

                return ScanOperator(Peek(1) == '=' ? TokenKind.GreaterOrEqual : TokenKind.Greater, Peek(1) == '=' ? 2 : 1, out kind);
            case '.':
                if (Peek(1) == '.' && Peek(2) == '.')
                {
                    return ScanOperator(TokenKind.Ellipsis, 3, out kind);
                }

                if (Peek(1) == '.')
                {
                    return ScanOperator(Peek(2) == '<' ? TokenKind.ExclusiveRange : TokenKind.Range, Peek(2) == '<' ? 3 : 2, out kind);
                }

                return ScanOperator(TokenKind.Dot, 1, out kind);
            case '?':
                if (Peek(1) == '?')
                {
                    return ScanOperator(Peek(2) == '=' ? TokenKind.CoalesceAssign : TokenKind.Coalesce, Peek(2) == '=' ? 3 : 2, out kind);
                }

                return ScanOperator(TokenKind.Question, 1, out kind);
            case '+':
                return ScanOperator(Peek(1) == '=' ? TokenKind.AddAssign : TokenKind.Plus, Peek(1) == '=' ? 2 : 1, out kind);
            case '-':
                return ScanOperator(Peek(1) == '=' ? TokenKind.SubtractAssign : TokenKind.Minus, Peek(1) == '=' ? 2 : 1, out kind);
            case '*':
                return ScanOperator(Peek(1) == '=' ? TokenKind.MultiplyAssign : TokenKind.Multiply, Peek(1) == '=' ? 2 : 1, out kind);
            case '/':
                return ScanOperator(Peek(1) == '=' ? TokenKind.DivideAssign : TokenKind.Divide, Peek(1) == '=' ? 2 : 1, out kind);
            case '%':
                return ScanOperator(Peek(1) == '=' ? TokenKind.RemainderAssign : TokenKind.Remainder, Peek(1) == '=' ? 2 : 1, out kind);
            case '=':
                if (Peek(1) == '>')
                {
                    return ScanOperator(TokenKind.Arrow, 2, out kind);
                }

                return ScanOperator(Peek(1) == '=' ? TokenKind.EqualEqual : TokenKind.Equal, Peek(1) == '=' ? 2 : 1, out kind);
            case '!':
                return ScanOperator(Peek(1) == '=' ? TokenKind.NotEqual : TokenKind.Bang, Peek(1) == '=' ? 2 : 1, out kind);
            case ',':
                return ScanOperator(TokenKind.Comma, 1, out kind);
            case ';':
                return ScanOperator(TokenKind.Semicolon, 1, out kind);
            case ':':
                return ScanOperator(TokenKind.Colon, 1, out kind);
            case '(':
                return ScanOperator(TokenKind.LeftParen, 1, out kind);
            case ')':
                return ScanOperator(TokenKind.RightParen, 1, out kind);
            case '{':
                return ScanOperator(TokenKind.LeftBrace, 1, out kind);
            case '}':
                return ScanOperator(TokenKind.RightBrace, 1, out kind);
            case '[':
                return ScanOperator(TokenKind.LeftBracket, 1, out kind);
            case ']':
                return ScanOperator(TokenKind.RightBracket, 1, out kind);
        }

        kind = TokenKind.Unknown;
        return false;
    }

    private bool ScanOperator(TokenKind operatorKind, int length, out TokenKind kind)
    {
        Advance(length);
        kind = operatorKind;
        return true;
    }

    private LexToken ScanIdentifier(int start, int startLine, int startColumn, bool afterEol, bool directive)
    {
        if (directive)
        {
            Advance();
        }

        Advance();
        while (!IsAtEnd && IsIdentifierPart(Current))
        {
            Advance();
        }

        var text = source.AsSpan(start, position - start);
        if (GetKeyword(text) is { } keyword)
        {
            return CreateToken(keyword, start, startLine, startColumn, afterEol);
        }

        var kind = directive
            ? TokenKind.DirectiveIdentifier
            : text[0] is >= 'A' and <= 'Z'
                ? TokenKind.UpperIdentifier
                : TokenKind.LowerIdentifier;
        return CreateToken(kind, start, startLine, startColumn, afterEol);
    }

    private LexToken ScanNumber(int start, int startLine, int startColumn, bool afterEol)
    {
        var isFloat = false;

        if (Current == '.')
        {
            isFloat = true;
            Advance();
            ConsumeDigits();
        }
        else
        {
            if (Current == '0' && Peek(1) is 'x' or 'X')
            {
                Advance(2);
                while (!IsAtEnd && IsHexDigitOrSeparator(Current))
                {
                    Advance();
                }

                return CreateToken(TokenKind.Integer, start, startLine, startColumn, afterEol);
            }

            ConsumeDigits();
            if (Current == '.' && IsDigitOrSeparator(Peek(1)))
            {
                isFloat = true;
                Advance();
                ConsumeDigits();
            }
        }

        if (Current is 'e' or 'E' && HasExponentDigits())
        {
            isFloat = true;
            Advance();
            if (Current is '+' or '-')
            {
                Advance();
            }

            ConsumeDigits();
        }

        if (Current is 'f' or 'F')
        {
            isFloat = true;
            Advance();
        }

        return CreateToken(
            isFloat ? TokenKind.Float : TokenKind.Integer,
            start,
            startLine,
            startColumn,
            afterEol);
    }

    private LexToken ScanQuoted(TokenKind kind, char quote, int start, int startLine, int startColumn, bool afterEol)
    {
        Advance();

        while (!IsAtEnd && Current != quote && Current is not '\r' and not '\n')
        {
            if (Current == '\\' && Peek(1) != '\0')
            {
                Advance(2);
            }
            else
            {
                Advance();
            }
        }

        if (Current != quote)
        {
            return CreateToken(TokenKind.Unknown, start, startLine, startColumn, afterEol);
        }

        Advance();
        return CreateToken(kind, start, startLine, startColumn, afterEol);
    }

    private LexToken ScanVerbatimString(int start, int startLine, int startColumn, bool afterEol)
    {
        Advance(3);

        while (!IsAtEnd && !Matches("\"\"\""))
        {
            Advance();
        }

        if (IsAtEnd)
        {
            return CreateToken(TokenKind.Unknown, start, startLine, startColumn, afterEol);
        }

        Advance(3);
        return CreateToken(TokenKind.VerbatimString, start, startLine, startColumn, afterEol);
    }

    private bool SkipTrivia()
    {
        var afterEol = false;

        while (!IsAtEnd)
        {
            if (char.IsWhiteSpace(Current))
            {
                afterEol |= Current is '\r' or '\n';
                Advance();
                continue;
            }

            if (Matches("//"))
            {
                Advance(2);
                while (!IsAtEnd && Current is not '\r' and not '\n')
                {
                    Advance();
                }

                continue;
            }

            if (Matches("/*"))
            {
                var commentStart = position;
                var commentLine = line;
                var commentColumn = column;
                Advance(2);
                var depth = 1;
                while (!IsAtEnd && depth > 0)
                {
                    if (Matches("/*"))
                    {
                        depth++;
                        Advance(2);
                    }
                    else if (Matches("*/"))
                    {
                        depth--;
                        Advance(2);
                    }
                    else
                    {
                        afterEol |= Current is '\r' or '\n';
                        Advance();
                    }
                }
                if (depth > 0)
                {
                    pendingInvalidToken = new LexToken(
                        TokenKind.Unknown,
                        source,
                        commentStart,
                        commentStart,
                        position - commentStart,
                        commentLine,
                        commentColumn,
                        afterEol);
                }

                continue;
            }

            break;
        }

        return afterEol;
    }

    private void ConsumeDigits()
    {
        while (!IsAtEnd && IsDigitOrSeparator(Current))
        {
            Advance();
        }
    }

    private bool HasExponentDigits()
    {
        var offset = 1;
        if (Peek(offset) is '+' or '-')
        {
            offset++;
        }

        return IsDigitOrSeparator(Peek(offset));
    }

    private LexToken CreateToken(TokenKind kind, int start, int startLine, int startColumn, bool afterEol) =>
        new(
            kind,
            source,
            start,
            start,
            position - start,
            startLine,
            startColumn,
            afterEol);

    private static TokenKind? GetKeyword(ReadOnlySpan<char> text) => text switch
    {
        "use" => TokenKind.Use,
        "mut" => TokenKind.Mut,
        "let" => TokenKind.Let,
        "func" => TokenKind.Func,
        "return" => TokenKind.Return,
        "continue" => TokenKind.Continue,
        "break" => TokenKind.Break,
        "yield" => TokenKind.Yield,
        "if" => TokenKind.If,
        "for" => TokenKind.For,
        "while" => TokenKind.While,
        "type" => TokenKind.Type,
        "in" => TokenKind.In,
        "do" => TokenKind.Do,
        "static" => TokenKind.Static,
        "with" => TokenKind.With,
        "#region" => TokenKind.Region,
        "#endregion" => TokenKind.EndRegion,
        "import" => TokenKind.Import,
        "private" => TokenKind.Private,
        "or" => TokenKind.Or,
        "when" => TokenKind.When,
        "true" => TokenKind.True,
        "false" => TokenKind.False,
        "and" => TokenKind.And,
        "get" => TokenKind.Get,
        "set" => TokenKind.Set,
        "as" => TokenKind.As,
        "match" => TokenKind.Match,
        "not" => TokenKind.Not,
        "nil" => TokenKind.Nil,
        "else" => TokenKind.Else,
        "throw" => TokenKind.Throw,
        "try" => TokenKind.Try,
        "catch" => TokenKind.Catch,
        "is" => TokenKind.Is,
        "from" => TokenKind.From,
        _ => null
    };

    private bool Matches(string value) =>
        position + value.Length <= source.Length
        && source.AsSpan(position, value.Length).SequenceEqual(value.AsSpan());

    private char Peek(int offset) =>
        position + offset < source.Length ? source[position + offset] : '\0';

    private char Current => Peek(0);

    private bool IsAtEnd => position >= source.Length;

    private void Advance(int count = 1)
    {
        for (var i = 0; i < count && !IsAtEnd; i++)
        {
            var current = source[position++];
            if (current == '\r')
            {
                if (Current == '\n')
                {
                    position++;
                    i++;
                }
                line++;
                column = 1;
            }
            else if (current == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
    }

    private static bool IsIdentifierStart(char value) =>
        value == '_' || value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' || value > 127 && char.IsLetter(value);

    private static bool IsIdentifierPart(char value) =>
        IsIdentifierStart(value) || char.IsDigit(value);

    private static bool IsDigitOrSeparator(char value) =>
        char.IsDigit(value) || value == '_';

    private static bool IsHexDigitOrSeparator(char value) =>
        value == '_' || value is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';

    internal static string ReadSource(SourceBuffer buffer)
    {
        var oldPosition = buffer.Pos;
        buffer.Pos = 0;
        var chars = new List<char>();
        int value;
        while ((value = buffer.Read()) != Constants.EOF)
        {
            chars.Add((char)value);
        }

        buffer.Pos = oldPosition;
        return new string(chars.ToArray());
    }
}
