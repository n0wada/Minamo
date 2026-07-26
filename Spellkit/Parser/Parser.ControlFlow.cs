using Spellkit.Parser.Model;

namespace Spellkit.Parser;

internal sealed partial class HandwrittenParser
{
    private SyntaxNode? ParseIf(bool isExpression)
    {
        var keyword = Consume();
        var condition = ParseExpression();
        var whenTrue = ParseRequiredBlock();
        if (condition is null || whenTrue is null)
        {
            return null;
        }

        if (keyword.TextSpan.SequenceEqual("guard".AsSpan()))
        {
            condition = new UnaryOperationSyntax(condition, UnaryOperator.Not, keyword.Location);
        }

        var node = new IfSyntax(keyword.Location, isExpression)
        {
            Condition = condition,
            True = whenTrue
        };

        if (Match(TokenKind.Else))
        {
            node.False = Current.Kind == TokenKind.If || IsContextualKeyword("guard")
                ? ParseIf(isExpression)
                : ParseRequiredBlock();
        }

        return node;
    }

    private SyntaxNode? ParseWhile()
    {
        var keyword = Consume();
        var condition = ParseExpression();
        var body = ParseRequiredBlock();
        if (condition is null || body is null)
        {
            return null;
        }

        return new WhileSyntax(keyword.Location) { Condition = condition, Body = body };
    }

    private SyntaxNode? ParseDoWhile()
    {
        var keyword = Consume();
        if (!Check(TokenKind.LeftBrace))
        {
            if (!IsIdentifier(Current.Kind))
            {
                Report(ParserError.InvalidExpression, Current);
                return null;
            }

            var name = Consume().Text;
            while (Match(TokenKind.Dot))
            {
                if (!IsIdentifier(Current.Kind))
                {
                    Report(ParserError.InvalidExpression, Current);
                    return null;
                }

                name += "." + Consume().Text;
            }

            return new SelectInvocationSyntax(keyword.Location) { Name = name };
        }

        var body = ParseRequiredBlock();
        if (!Expect(TokenKind.While))
        {
            return null;
        }

        var condition = ParseExpression();
        if (body is null || condition is null)
        {
            return null;
        }

        return new WhileSyntax(keyword.Location) { DoWhile = true, Condition = condition, Body = body };
    }

    private SyntaxNode? ParseFor()
    {
        var keyword = Consume();
        var pattern = ParseForPattern();
        if (!Expect(TokenKind.In))
        {
            return null;
        }

        var target = ParseExpression();
        SyntaxNode? guard = null;
        if (Match(TokenKind.When))
        {
            guard = ParseExpression();
        }

        var body = ParseRequiredBlock();
        SyntaxNode? whenEmpty = null;
        if (Match(TokenKind.Else))
        {
            whenEmpty = ParseRequiredBlock();
        }

        if (pattern is null || target is null || body is null)
        {
            return null;
        }

        return new ForSyntax(keyword.Location)
        {
            Pattern = pattern,
            Target = target,
            Guard = guard,
            Body = body,
            Else = whenEmpty
        };
    }

    private PatternSyntax? ParseForPattern()
    {
        var first = ParsePattern(allowTypeTest: false);
        if (first is null || !Match(TokenKind.Comma))
        {
            return first;
        }

        var tuple = new TuplePatternSyntax(first.Location);
        tuple.Elements.Add(first);
        do
        {
            var element = ParsePattern(allowTypeTest: false);
            if (element is not null)
            {
                tuple.Elements.Add(element);
            }
        }
        while (Match(TokenKind.Comma));
        return tuple;
    }

    private SyntaxNode? ParseTryCatch()
    {
        var keyword = Consume();
        var body = ParseRequiredBlock();
        if (body is null)
        {
            return null;
        }

        var node = new TryCatchSyntax(keyword.Location) { Expression = body };
        if (Match(TokenKind.Catch))
        {
            if (IsIdentifier(Current.Kind))
            {
                var name = Consume();
                node.BindVariable = new NameSyntax(name.Location) { Value = name.Text };
            }
            node.Catch = ParseRequiredBlock();
        }
        if (Check(TokenKind.LowerIdentifier) && Current.TextSpan.SequenceEqual("finally".AsSpan()))
        {
            Consume();
            node.Finally = ParseRequiredBlock();
        }

        return node;
    }

    private SyntaxNode? ParseMatch()
    {
        var keyword = Consume();
        var expression = ParseExpression();
        if (expression is null || !Expect(TokenKind.LeftBrace))
        {
            return null;
        }

        var node = new MatchSyntax(keyword.Location) { Expression = expression };
        while (!Check(TokenKind.RightBrace) && !IsAtEnd)
        {
            var entry = ParseMatchEntry();
            if (entry is not null)
            {
                node.Entries.Add(entry);
            }

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        Expect(TokenKind.RightBrace);
        return node;
    }

    private MatchEntrySyntax? ParseMatchEntry()
    {
        var location = Current.Location;
        var pattern = ParsePattern(allowTypeTest: false);
        SyntaxNode? guard = null;
        if (Match(TokenKind.When))
        {
            guard = ParseMatchGuard();
        }

        if (!Expect(TokenKind.Arrow))
        {
            return null;
        }

        var expression = ParseAssignment(wrapExpression: false);
        if (pattern is null || expression is null)
        {
            return null;
        }

        return new MatchEntrySyntax(location) { Pattern = pattern, Guard = guard, Expression = expression };
    }

    private SyntaxNode? ParseMatchGuard()
    {
        var previous = allowLambda;
        allowLambda = false;
        try
        {
            return ParseExpression();
        }
        finally
        {
            allowLambda = previous;
        }
    }

    private BlockSyntax? ParseRequiredBlock()
    {
        if (!Check(TokenKind.LeftBrace))
        {
            ReportExpected(TokenKind.LeftBrace);
            return null;
        }
        return ParseBlock();
    }
}

internal sealed partial class HandwrittenParser
{
    private PatternSyntax? ParsePattern(bool allowTypeTest) => ParseOrPattern(allowTypeTest);

    private PatternSyntax? ParseOrPattern(bool allowTypeTest)
    {
        var left = ParseAndPattern(allowTypeTest);
        while (left is not null && Match(TokenKind.Or))
        {
            var location = Previous.Location;
            var right = ParseAndPattern(allowTypeTest);
            if (right is null)
            {
                return left;
            }

            left = new OrPatternSyntax(location) { Left = left, Right = right };
        }
        return left;
    }

    private PatternSyntax? ParseAndPattern(bool allowTypeTest)
    {
        var left = ParseRangePattern(allowTypeTest);
        while (left is not null && Match(TokenKind.And))
        {
            var location = Previous.Location;
            var right = ParseRangePattern(allowTypeTest);
            if (right is null)
            {
                return left;
            }

            left = new AndPatternSyntax(location) { Left = left, Right = right };
        }
        return left;
    }

    private PatternSyntax? ParseRangePattern(bool allowTypeTest)
    {
        var from = ParsePrimaryPattern(allowTypeTest);
        if (from is null || !Match(TokenKind.Range))
        {
            return from;
        }

        var location = Previous.Location;
        var to = ParsePrimaryPattern(allowTypeTest);
        return to is null ? from : new RangePatternSyntax(location) { From = from, To = to };
    }

    private PatternSyntax? ParsePrimaryPattern(bool allowTypeTest)
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Not:
                Consume();
                var inner = ParsePrimaryPattern(allowTypeTest);
                return inner is null ? null : new NotPatternSyntax(token.Location) { Pattern = inner };
            case TokenKind.LowerIdentifier:
            case TokenKind.UpperIdentifier:
                return ParseNamePattern(allowTypeTest);
            case TokenKind.Integer:
                Consume();
                return new IntegerPatternSyntax(token.Location) { Value = ((IntegerLiteralSyntax)ParseInteger(token)).Value };
            case TokenKind.Float:
                Consume();
                return new FloatPatternSyntax(token.Location) { Value = ((FloatLiteralSyntax)ParseFloat(token)).Value };
            case TokenKind.Character:
                Consume();
                return new CharPatternSyntax(token.Location) { Value = ((CharLiteralSyntax)ParseCharacter(token)).Value };
            case TokenKind.String:
                var value = (StringLiteralSyntax)ParseString();
                return new StringPatternSyntax(token.Location) { Value = value };
            case TokenKind.True:
            case TokenKind.False:
                Consume();
                return new BooleanPatternSyntax(token.Location) { Value = token.Kind == TokenKind.True };
            case TokenKind.Nil:
                Consume();
                return new NilPatternSyntax(token.Location);
            case TokenKind.LeftParen:
                return ParseParenthesizedPattern(allowTypeTest);
            case TokenKind.LeftBracket:
                return ParseArrayPattern(allowTypeTest);
            default:
                Report(ParserError.InvalidPattern, token);
                return null;
        }
    }

    private PatternSyntax? ParseNamePattern(bool allowTypeTest)
    {
        var first = Consume();
        var names = new[] { first.Text, null, null };
        var count = 1;
        while (count < names.Length && Match(TokenKind.Dot))
        {
            if (!IsIdentifier(Current.Kind))
            {
                ReportExpected(TokenKind.LowerIdentifier);
                return null;
            }
            names[count++] = Consume().Text;
        }

        if (Match(TokenKind.LeftParen))
        {
            var constructor = new ConstructorPatternSyntax(first.Location);
            if (count == 1)
            {
                constructor.Constructor = names[0]!;
            }
            else if (count == 2)
            {
                constructor.TypeName = new Qualident(names[0]!);
                constructor.Constructor = names[1]!;
            }
            else
            {
                constructor.TypeName = new Qualident(names[1]!, names[0]!);
                constructor.Constructor = names[2]!;
            }

            while (!Check(TokenKind.RightParen) && !IsAtEnd)
            {
                var argument = ParsePattern(allowTypeTest);
                if (argument is not null)
                {
                    constructor.Arguments.Add(argument);
                }

                if (!Match(TokenKind.Comma))
                {
                    break;
                }
            }
            Expect(TokenKind.RightParen);
            return constructor;
        }

        if (count == 3)
        {
            Report(ParserError.InvalidPattern, first);
            return null;
        }
        if (count == 2)
        {
            return new TypeTestPatternSyntax(first.Location)
            {
                TypeName = new Qualident(names[1]!, names[0]!),
                AllowTypeCheck = allowTypeTest
            };
        }

        var firstText = first.TextSpan;
        if (firstText.SequenceEqual("_".AsSpan()))
        {
            return new WildcardPatternSyntax(first.Location);
        }

        if (char.IsUpper(firstText[0]))
        {
            return new TypeTestPatternSyntax(first.Location)
            {
                TypeName = new Qualident(first.Text),
                AllowTypeCheck = allowTypeTest
            };
        }

        return new NamePatternSyntax(first.Location) { Name = first.Text };
    }

    private PatternSyntax? ParseParenthesizedPattern(bool allowTypeTest)
    {
        var open = Consume();
        var first = ParsePattern(allowTypeTest);
        if (first is null)
        {
            return null;
        }

        if (!Match(TokenKind.Comma))
        {
            Expect(TokenKind.RightParen);
            return first;
        }

        var tuple = new TuplePatternSyntax(open.Location);
        tuple.Elements.Add(first);
        while (!Check(TokenKind.RightParen) && !IsAtEnd)
        {
            var element = ParsePattern(allowTypeTest);
            if (element is not null)
            {
                tuple.Elements.Add(element);
            }

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        Expect(TokenKind.RightParen);
        return tuple;
    }

    private PatternSyntax ParseArrayPattern(bool allowTypeTest)
    {
        var open = Consume();
        var array = new ArrayPatternSyntax(open.Location);
        while (!Check(TokenKind.RightBracket) && !IsAtEnd)
        {
            var element = ParseRangePattern(allowTypeTest);
            if (element is not null)
            {
                array.Elements.Add(element);
            }

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        Expect(TokenKind.RightBracket);
        return array;
    }
}
