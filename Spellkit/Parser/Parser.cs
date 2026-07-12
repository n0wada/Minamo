using Spellkit.Parser.Model;
using Spellkit.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spellkit.Parser;

internal readonly record struct ParserMark(int TokenPosition, int TokensSinceError);

internal sealed partial class HandwrittenParser
{
    private const int MinimumErrorDistance = 2;
    private const int MaximumErrorCount = 100;

    private readonly string fileName;
    private readonly TokenCursor cursor;
    private readonly List<ImportSyntax> imports = new();
    private readonly BlockSyntax root = new(default);
    private readonly Stack<FunctionDeclarationSyntax> functions = new();
    private int tokensSinceError = MinimumErrorDistance;
    private bool allowLambda = true;

    public HandwrittenParser(string fileName, Lexer lexer)
    {
        this.fileName = fileName;
        cursor = new TokenCursor(lexer);
    }

    public List<BuildMessage> Errors { get; } = new();

    public LexToken Current => cursor.Current;

    public LexToken Previous => cursor.Previous;

    public bool IsAtEnd => cursor.IsAtEnd;

    public LexToken Peek(int offset = 0) => cursor.Peek(offset);

    public ParserMark Mark() => new(cursor.Mark(), tokensSinceError);

    public void Reset(ParserMark mark)
    {
        cursor.Reset(mark.TokenPosition);
        tokensSinceError = mark.TokensSinceError;
    }

    public Result<SpkCodeModel> Parse()
    {
        while (!IsAtEnd && Errors.Count < MaximumErrorCount)
        {
            var start = Mark();
            var isPrivate = Match(TokenKind.Private);
            var privateToken = Previous;

            if (Match(TokenKind.Import))
            {
                if (isPrivate)
                {
                    Report(ParserError.InvalidImport, privateToken);
                }

                var import = ParseImport(Previous);
                if (import is not null)
                {
                    imports.Add(import);
                }
            }
            else
            {
                var statement = ParseStatement();
                if (statement is not null && (!isPrivate || MarkPrivateDeclaration(statement)))
                {
                    root.Nodes.Add(statement);
                }
            }

            if (cursor.Mark() == start.TokenPosition)
            {
                Consume();
            }
        }

        var model = new SpkCodeModel(root, imports.ToArray(), fileName);
        return Errors.Count == 0 ? Result.Create(model) : Result.Create(model, Errors);
    }

    private bool MarkPrivateDeclaration(SyntaxNode node)
    {
        var valid = node switch
        {
            BindingSyntax => true,
            ConstDeclarationSyntax => true,
            TypeDeclarationSyntax => true,
            FunctionDeclarationSyntax { TypeName: null, TargetTypeName: null } => true,
            _ => false
        };

        if (!valid)
        {
            Report(ParserError.InvalidStatement, Previous);
            return false;
        }

        node.IsPrivate = true;
        return true;
    }

    private ImportSyntax? ParseImport(LexToken importToken)
    {
        var node = new ImportSyntax(importToken.Location);

        if (Match(TokenKind.Multiply))
        {
            node.Kind = ImportKind.All;
            if (!Expect(TokenKind.From) || !TryParseImportPath(node))
            {
                return null;
            }
        }
        else
        {
            var firstName = ParseImportToken();
            if (firstName is null)
            {
                return null;
            }

            if (Match(TokenKind.From))
            {
                node.Kind = ImportKind.Symbol;
                node.SymbolName = firstName;
                if (!TryParseImportPath(node))
                {
                    return null;
                }
            }
            else
            {
                node.Kind = ImportKind.Module;
                ParseImportPathRest(node, firstName);
                if (Match(TokenKind.As))
                {
                    node.Alias = ParseImportToken();
                }
            }
        }

        ExpectSeparator();
        return node;
    }

    private bool TryParseImportPath(ImportSyntax import)
    {
        var firstName = ParseImportToken();
        if (firstName is null)
        {
            return false;
        }

        ParseImportPathRest(import, firstName);
        return true;
    }

    private void ParseImportPathRest(ImportSyntax import, string lastName)
    {
        while (Match(TokenKind.Divide))
        {
            var nextName = ParseImportToken();
            if (nextName is null)
            {
                return;
            }

            import.LocalPath = import.LocalPath is null
                ? lastName
                : string.Concat(import.LocalPath, "/", lastName);
            lastName = nextName;
        }

        import.ModuleName = lastName;
    }

    private string? ParseImportToken()
    {
        if (Check(TokenKind.String))
        {
            return ParseStringValue(Consume());
        }

        if (!IsIdentifier(Current.Kind))
        {
            ReportExpected(TokenKind.LowerIdentifier);
            return null;
        }

        var name = Consume().Text;
        while (Match(TokenKind.Dot))
        {
            if (!IsIdentifier(Current.Kind))
            {
                ReportExpected(TokenKind.LowerIdentifier);
                return null;
            }
            name = string.Concat(name, ".", Consume().Text);
        }
        return name;
    }

    private SyntaxNode? ParseStatement()
    {
        if (IsContextualKeyword("guard"))
        {
            return FinishStatement(ParseIf(isExpression: false));
        }

        if (IsTypeDeclarationStart())
        {
            return FinishStatement(ParseTypeOrImplDeclaration());
        }

        if (Current.Kind == TokenKind.Region)
        {
            return ParseRegion();
        }

        if (IsContextualKeyword("const"))
        {
            return FinishStatement(ParseConstDeclaration());
        }

        if (Current.Kind is TokenKind.Func or TokenKind.Static)
        {
            return ParseFunctionDeclaration();
        }

        SyntaxNode? node = Current.Kind switch
        {
            TokenKind.Let or TokenKind.Mut or TokenKind.Use => ParseBinding(),
            TokenKind.Return => ParseReturn(),
            TokenKind.Yield => ParseYield(),
            TokenKind.Break => ParseBreak(),
            TokenKind.Continue => ParseContinue(),
            TokenKind.Throw => ParseThrow(),
            TokenKind.If => ParseIf(isExpression: false),
            TokenKind.While => ParseWhile(),
            TokenKind.Do => ParseDoWhile(),
            TokenKind.For => ParseFor(),
            TokenKind.Try => ParseTryCatch(),
            TokenKind.Match => ParseMatch(),
            TokenKind.LeftBrace => ParseBlock(),
            _ => ParseAssignment()
        };

        return FinishStatement(node);
    }

    private SyntaxNode? FinishStatement(SyntaxNode? node)
    {
        if (node is null)
        {
            SynchronizeStatement();
            return null;
        }

        if (!ExpectSeparator())
        {
            SynchronizeStatement();
        }

        return node is AssignmentSyntax or RebindingSyntax
            ? node
            : node is ExpressionStatementSyntax
                ? node
                : IsStatementNode(node)
                    ? node
                    : new ExpressionStatementSyntax(node.Location) { Expression = node };
    }

    private BlockSyntax? ParseBlock()
    {
        var open = Consume();
        var block = new BlockSyntax(open.Location);

        while (!Check(TokenKind.RightBrace) && !IsAtEnd)
        {
            var start = Mark();
            var statement = ParseStatement();
            if (statement is not null)
            {
                block.Nodes.Add(statement);
            }

            if (cursor.Mark() == start.TokenPosition)
            {
                Consume();
            }
        }

        Expect(TokenKind.RightBrace);
        return block;
    }

    private SyntaxNode? ParseBinding()
    {
        var keyword = Consume();
        var autoClose = keyword.Kind == TokenKind.Use;
        var pattern = autoClose
            ? ParseNamePattern(allowTypeTest: false)
            : ParsePattern(allowTypeTest: false);
        if (pattern is null)
        {
            return null;
        }

        var binding = new BindingSyntax(keyword.Location)
        {
            AutoClose = autoClose,
            Constant = autoClose || keyword.Kind == TokenKind.Let,
            Pattern = pattern
        };

        if (Match(TokenKind.Colon))
        {
            binding.TypeAnnotation = ParseTypeAnnotation();
        }

        if (Match(TokenKind.Equal))
        {
            binding.Init = ParseExpression()!;
        }
        else if (autoClose)
        {
            ReportExpected(TokenKind.Equal);
        }

        return binding;
    }

    private SyntaxNode ParseReturn()
    {
        var token = Consume();
        var node = new ReturnSyntax(token.Location);
        if (CanStartSameLineExpression())
        {
            node.Expression = ParseExpression();
        }

        return node;
    }

    private SyntaxNode ParseYield()
    {
        var token = Consume();
        if (functions.Count > 0)
        {
            functions.Peek().IsIterator = true;
        }

        if (Match(TokenKind.Break))
        {
            return new YieldBreakSyntax(token.Location);
        }

        var expression = ParseExpression();
        return new YieldSyntax(token.Location) { Expression = expression! };
    }

    private SyntaxNode ParseBreak()
    {
        var token = Consume();
        var node = new BreakSyntax(token.Location);
        if (CanStartSameLineExpression())
        {
            node.Expression = ParseExpression();
        }

        return node;
    }

    private SyntaxNode ParseContinue()
    {
        var token = Consume();
        return new ContinueSyntax(token.Location);
    }

    private SyntaxNode ParseThrow()
    {
        var token = Consume();
        var node = new ThrowSyntax(token.Location);
        if (CanStartSameLineExpression())
        {
            node.Expression = ParseExpression();
        }

        return node;
    }

    private bool CanStartSameLineExpression() =>
        !Current.AfterEol
        && Current.Kind is not TokenKind.Semicolon and not TokenKind.RightBrace and not TokenKind.EndOfFile;

    private SyntaxNode? ParseAssignment(bool wrapExpression = true)
    {
        var target = ParseExpression();
        if (target is null)
        {
            return null;
        }

        if (!TryGetAssignmentOperator(Current.Kind, out var autoAssign))
        {
            return wrapExpression
                ? new ExpressionStatementSyntax(target.Location) { Expression = target }
                : target;
        }

        var operatorToken = Consume();
        var value = ParseExpression();
        if (value is null)
        {
            return null;
        }

        if (autoAssign is null && TryConvertRebindingPattern(target, out var pattern))
        {
            return new RebindingSyntax(target.Location) { Pattern = pattern, Init = value };
        }

        return new AssignmentSyntax(operatorToken.Location)
        {
            Target = target,
            Value = value,
            AutoAssign = autoAssign
        };
    }

    private SyntaxNode? ParseExpression(int minimumPrecedence = 0)
    {
        var left = ParsePrefix();
        if (left is null)
        {
            return null;
        }

        while (true)
        {
            if (Current.Kind == TokenKind.Is && 4 >= minimumPrecedence)
            {
                var isToken = Consume();
                var pattern = ParsePattern(allowTypeTest: true);
                if (pattern is null)
                {
                    return left;
                }

                left = new BinaryOperationSyntax(left, pattern, BinaryOperator.Is, isToken.Location);
                continue;
            }

            if (Current.Kind == TokenKind.As && 9 >= minimumPrecedence)
            {
                var asToken = Consume();
                var typeName = ParseTypeName();
                if (typeName is null)
                {
                    return left;
                }

                left = new AsSyntax(asToken.Location) { Expression = left, TypeName = typeName };
                continue;
            }

            if (Current.Kind is TokenKind.Range or TokenKind.ExclusiveRange && 5 >= minimumPrecedence)
            {
                var rangeToken = Consume();
                var range = new RangeSyntax(rangeToken.Location)
                {
                    From = left,
                    Exclusive = rangeToken.Kind == TokenKind.ExclusiveRange
                };
                if (CanStartExpression(Current.Kind))
                {
                    range.To = ParseExpression(6);
                }

                left = range;
                continue;
            }

            if (!TryGetBinaryOperator(Current.Kind, out var op, out var precedence)
                || precedence < minimumPrecedence)
            {
                break;
            }

            var operatorToken = Consume();
            var right = ParseExpression(precedence + 1);
            if (right is null)
            {
                return left;
            }

            left = new BinaryOperationSyntax(left, right, op, operatorToken.Location);
        }

        if (minimumPrecedence == 0 && Match(TokenKind.Question))
        {
            var question = Previous;
            var whenTrue = ParseExpression();
            Expect(TokenKind.Colon);
            var whenFalse = ParseExpression();
            if (whenTrue is not null && whenFalse is not null)
            {
                left = new IfSyntax(question.Location, isExpression: true)
                {
                    Condition = left,
                    True = whenTrue,
                    False = whenFalse
                };
            }
        }

        return left;
    }

    private SyntaxNode? ParsePrefix()
    {
        if (Current.Kind is TokenKind.Range or TokenKind.ExclusiveRange)
        {
            var rangeToken = Consume();
            var range = new RangeSyntax(rangeToken.Location)
            {
                Exclusive = rangeToken.Kind == TokenKind.ExclusiveRange
            };
            if (CanStartExpression(Current.Kind))
            {
                range.To = ParseExpression(6);
            }

            return range;
        }

        if (TryGetUnaryOperator(Current.Kind, out var op))
        {
            var operatorToken = Consume();
            var operand = ParsePrefix();
            if (operand is null)
            {
                return null;
            }

            return new UnaryOperationSyntax(operand, op, operatorToken.Location);
        }

        var node = ParsePrimary();
        return node is null ? null : ParsePostfix(node);
    }

    private Qualident? ParseTypeName()
    {
        if (!IsIdentifier(Current.Kind))
        {
            ReportExpected(TokenKind.UpperIdentifier);
            return null;
        }

        var first = Consume().Text;
        if (!Match(TokenKind.Dot))
        {
            return new Qualident(first);
        }

        if (!IsIdentifier(Current.Kind))
        {
            ReportExpected(TokenKind.UpperIdentifier);
            return null;
        }
        return new Qualident(Consume().Text, first);
    }

    private SyntaxNode? ParsePrimary()
    {
        if (IsLambdaStart())
        {
            return ParseLambda();
        }

        if (IsContextualKeyword("guard"))
        {
            return ParseIf(isExpression: true);
        }

        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.LowerIdentifier:
            case TokenKind.UpperIdentifier:
                Consume();
                return new NameSyntax(token.Location) { Value = token.Text };
            case TokenKind.Integer:
                Consume();
                return ParseInteger(token);
            case TokenKind.Float:
                Consume();
                return ParseFloat(token);
            case TokenKind.String:
                return ParseString();
            case TokenKind.VerbatimString:
                Consume();
                return new StringLiteralSyntax(token.Location) { Value = token.Text[3..^3] };
            case TokenKind.Character:
                Consume();
                return ParseCharacter(token);
            case TokenKind.True:
            case TokenKind.False:
                Consume();
                return new BooleanLiteralSyntax(token.Location) { Value = token.Kind == TokenKind.True };
            case TokenKind.Nil:
                Consume();
                return new NilLiteralSyntax(token.Location);
            case TokenKind.LeftParen:
                return ParseParenthesized();
            case TokenKind.LeftBracket:
                return ParseArray();
            case TokenKind.LeftBrace:
                return ParseBlock();
            case TokenKind.If:
                return ParseIf(isExpression: true);
            case TokenKind.While:
                return ParseWhile();
            case TokenKind.Do:
                return ParseDoWhile();
            case TokenKind.For:
                return ParseFor();
            case TokenKind.Try:
                return ParseTryCatch();
            case TokenKind.Match:
                return ParseMatch();
            case TokenKind.Throw:
                return ParseThrow();
            case TokenKind.Unknown:
                Report(ParserError.InvalidLiteral, token);
                Consume();
                return null;
            default:
                Report(ParserError.InvalidExpression, token);
                return null;
        }
    }

    private SyntaxNode ParsePostfix(SyntaxNode node)
    {
        while (!Current.AfterEol)
        {
            if (Match(TokenKind.Dot))
            {
                var dot = Previous;
                if (!IsIdentifier(Current.Kind))
                {
                    ReportExpected(TokenKind.LowerIdentifier);
                    return node;
                }
                node = new AccessSyntax(dot.Location) { Target = node, Name = Consume().Text };
            }
            else if (Match(TokenKind.LeftBracket))
            {
                var open = Previous;
                var index = ParseExpression();
                Expect(TokenKind.RightBracket);
                if (index is not null)
                {
                    node = new IndexerSyntax(open.Location) { Target = node, Index = index };
                }
            }
            else if (Match(TokenKind.LeftParen))
            {
                var open = Previous;
                var application = new ApplicationSyntax(node, open.Location);
                if (IsExceptionTarget(node))
                {
                    application.Arguments.Add(new StringLiteralSyntax(open.Location) { Value = "Exception" });
                }

                ParseExpressionList(application.Arguments, TokenKind.RightParen);
                Expect(TokenKind.RightParen);
                node = application;
            }
            else if (IsExceptionTarget(node) && Match(TokenKind.Less))
            {
                var tag = Consume();
                Expect(TokenKind.Greater);
                var open = Current;
                if (!Expect(TokenKind.LeftParen))
                {
                    return node;
                }

                var application = new ApplicationSyntax(node, open.Location);
                application.Arguments.Add(new StringLiteralSyntax(tag.Location) { Value = tag.Text });
                ParseExpressionList(application.Arguments, TokenKind.RightParen);
                Expect(TokenKind.RightParen);
                node = application;
            }
            else
            {
                break;
            }
        }

        return node;
    }

    private SyntaxNode? ParseParenthesized()
    {
        var open = Consume();
        if (Match(TokenKind.RightParen))
        {
            return new TupleLiteralSyntax(open.Location);
        }

        var first = ParseArgumentExpression();
        if (first is null)
        {
            return null;
        }

        var hasComma = Match(TokenKind.Comma);
        if (!hasComma && first is not LabelLiteralSyntax)
        {
            Expect(TokenKind.RightParen);
            return first;
        }

        var tuple = new TupleLiteralSyntax(open.Location);
        tuple.Elements.Add(first);
        if (!hasComma)
        {
            Expect(TokenKind.RightParen);
            return tuple;
        }
        while (!Check(TokenKind.RightParen) && !IsAtEnd)
        {
            var element = ParseArgumentExpression();
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

    private SyntaxNode ParseArray()
    {
        var open = Consume();
        if (Match(TokenKind.RightBracket))
        {
            return new ArrayLiteralSyntax(open.Location);
        }

        SyntaxNode? first;
        SyntaxNode? dictionaryKey = null;
        if (IsLabelStart())
        {
            first = ParseArgumentExpression();
            if (first is LabelLiteralSyntax label && Check(TokenKind.For))
            {
                dictionaryKey = label.FromString
                    ? new StringLiteralSyntax(label.Location) { Value = label.Label }
                    : new NameSyntax(label.Location) { Value = label.Label };
                first = label.Expression;
            }
        }
        else
        {
            first = ParseExpression();
            if (Match(TokenKind.Colon))
            {
                dictionaryKey = first;
                first = ParseExpression();
            }
        }

        if (first is not null && Match(TokenKind.For))
        {
            var comprehension = new ComprehensionSyntax(open.Location)
            {
                Key = dictionaryKey,
                Value = first
            };
            comprehension.Pattern = ParseForPattern()!;
            Expect(TokenKind.In);
            comprehension.Target = ParseExpression()!;
            if (Match(TokenKind.When))
            {
                comprehension.Guard = ParseExpression();
            }

            Expect(TokenKind.RightBracket);
            return comprehension;
        }

        var array = new ArrayLiteralSyntax(open.Location);
        if (dictionaryKey is not null && first is not null)
        {
            if (dictionaryKey is NameSyntax name)
            {
                array.Elements.Add(new LabelLiteralSyntax(dictionaryKey.Location)
                {
                    Label = name.Value,
                    Expression = first
                });
            }
            else if (dictionaryKey is StringLiteralSyntax text)
            {
                array.Elements.Add(new LabelLiteralSyntax(dictionaryKey.Location)
                {
                    Label = text.Value ?? string.Empty,
                    FromString = true,
                    Expression = first
                });
            }
        }
        else if (first is not null)
        {
            array.Elements.Add(first);
        }

        while (Match(TokenKind.Comma) && !Check(TokenKind.RightBracket))
        {
            var element = ParseArgumentExpression();
            if (element is not null)
            {
                array.Elements.Add(element);
            }
        }
        Expect(TokenKind.RightBracket);
        return array;
    }

    private void ParseExpressionList(List<SyntaxNode> output, TokenKind closingToken)
    {
        while (!Check(closingToken) && !IsAtEnd)
        {
            var expression = ParseArgumentExpression();
            if (expression is not null)
            {
                output.Add(expression);
            }

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
    }

    private SyntaxNode? ParseArgumentExpression()
    {
        if (!IsLabelStart())
        {
            return ParseExpression();
        }

        var mutable = Match(TokenKind.Mut);
        if (!mutable)
        {
            Match(TokenKind.Let);
        }

        var name = Consume();
        var fromString = name.Kind == TokenKind.String;
        var label = fromString ? ParseStringValue(name) : name.Text;
        var colon = Consume();
        var expression = ParseExpression();
        return expression is null ? null : new LabelLiteralSyntax(colon.Location)
        {
            Mutable = mutable,
            Label = label ?? string.Empty,
            FromString = fromString,
            Expression = expression
        };
    }

    private bool IsLabelStart()
    {
        var offset = 0;
        if (Peek(offset).Kind is TokenKind.Mut or TokenKind.Let)
        {
            offset++;
        }

        if (Peek(offset).Kind is not TokenKind.LowerIdentifier
            and not TokenKind.UpperIdentifier
            and not TokenKind.String)
        {
            return false;
        }

        return Peek(offset + 1).Kind == TokenKind.Colon;
    }

    private SyntaxNode ParseString()
    {
        var first = Consume();
        var value = ParseStringValue(first);
        while (Check(TokenKind.String))
        {
            value += ParseStringValue(Consume());
        }

        return new StringLiteralSyntax(first.Location) { Value = value };
    }

    private string? ParseStringValue(LexToken token)
    {
        EscapeCodeParser.Parse(fileName, token.Location, token.Text, Errors, out var value);
        return value;
    }

    private SyntaxNode ParseCharacter(LexToken token)
    {
        var value = ParseStringValue(token) ?? string.Empty;
        if (value.Length != 1)
        {
            AddUnsuppressedError(ParserError.InvalidCharLiteral, token);
            return new CharLiteralSyntax(token.Location);
        }
        return new CharLiteralSyntax(token.Location) { Value = value[0] };
    }

    private SyntaxNode ParseInteger(LexToken token)
    {
        var text = token.Text.Replace("_", string.Empty);
        var style = NumberStyles.Integer;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            style = NumberStyles.HexNumber;
        }

        if (!long.TryParse(text, style, InvariantCulture.NumberFormat, out var value))
        {
            AddUnsuppressedError(ParserError.InvalidNumber, token);
        }

        return new IntegerLiteralSyntax(token.Location) { Value = value };
    }

    private SyntaxNode ParseFloat(LexToken token)
    {
        var text = token.Text.Replace("_", string.Empty).TrimEnd('f', 'F');
        if (!double.TryParse(text, NumberStyles.Float, InvariantCulture.NumberFormat, out var value))
        {
            AddUnsuppressedError(ParserError.InvalidNumber, token);
        }

        return new FloatLiteralSyntax(token.Location) { Value = value };
    }

    private static bool TryConvertRebindingPattern(SyntaxNode node, out PatternSyntax pattern)
    {
        switch (node)
        {
            case NameSyntax name:
                pattern = name.Value == "_"
                    ? new WildcardPatternSyntax(name.Location)
                    : new NamePatternSyntax(name.Location) { Name = name.Value };
                return true;
            case IntegerLiteralSyntax integer:
                pattern = new IntegerPatternSyntax(integer.Location) { Value = integer.Value };
                return true;
            case FloatLiteralSyntax floating:
                pattern = new FloatPatternSyntax(floating.Location) { Value = floating.Value };
                return true;
            case BooleanLiteralSyntax boolean:
                pattern = new BooleanPatternSyntax(boolean.Location) { Value = boolean.Value };
                return true;
            case CharLiteralSyntax character:
                pattern = new CharPatternSyntax(character.Location) { Value = character.Value };
                return true;
            case StringLiteralSyntax text:
                pattern = new StringPatternSyntax(text.Location) { Value = text };
                return true;
            case NilLiteralSyntax nil:
                pattern = new NilPatternSyntax(nil.Location);
                return true;
            case TupleLiteralSyntax tuple:
                return TryConvertSequencePattern(tuple.Elements, new TuplePatternSyntax(tuple.Location), out pattern);
            case ArrayLiteralSyntax array:
                return TryConvertSequencePattern(array.Elements, new ArrayPatternSyntax(array.Location), out pattern);
            case RangeSyntax range when range.From is not null && range.To is not null
                && TryConvertRebindingPattern(range.From, out var from)
                && TryConvertRebindingPattern(range.To, out var to):
                pattern = new RangePatternSyntax(range.Location) { From = from, To = to };
                return true;
            default:
                pattern = null!;
                return false;
        }
    }

    private static bool TryConvertSequencePattern(List<SyntaxNode> nodes, SequencePatternSyntax sequence, out PatternSyntax pattern)
    {
        foreach (var node in nodes)
        {
            if (node is LabelLiteralSyntax || !TryConvertRebindingPattern(node, out var element))
            {
                pattern = null!;
                return false;
            }
            sequence.Elements.Add(element);
        }
        pattern = sequence;
        return true;
    }

    private static bool TryGetAssignmentOperator(TokenKind kind, out BinaryOperator? op)
    {
        op = kind switch
        {
            TokenKind.Equal => null,
            TokenKind.CoalesceAssign => BinaryOperator.Coalesce,
            TokenKind.AddAssign => BinaryOperator.Add,
            TokenKind.SubtractAssign => BinaryOperator.Sub,
            TokenKind.MultiplyAssign => BinaryOperator.Mul,
            TokenKind.DivideAssign => BinaryOperator.Div,
            TokenKind.RemainderAssign => BinaryOperator.Rem,
            _ => null
        };
        return kind is TokenKind.Equal
            or TokenKind.CoalesceAssign
            or TokenKind.AddAssign
            or TokenKind.SubtractAssign
            or TokenKind.MultiplyAssign
            or TokenKind.DivideAssign
            or TokenKind.RemainderAssign;
    }

    private static bool TryGetBinaryOperator(TokenKind kind, out BinaryOperator op, out int precedence)
    {
        (op, precedence) = kind switch
        {
            TokenKind.Coalesce => (BinaryOperator.Coalesce, 1),
            TokenKind.LogicalOr => (BinaryOperator.Or, 2),
            TokenKind.LogicalAnd => (BinaryOperator.And, 3),
            TokenKind.In => (BinaryOperator.In, 4),
            TokenKind.EqualEqual => (BinaryOperator.Eq, 6),
            TokenKind.NotEqual => (BinaryOperator.NotEq, 6),
            TokenKind.Greater => (BinaryOperator.Gt, 6),
            TokenKind.Less => (BinaryOperator.Lt, 6),
            TokenKind.GreaterOrEqual => (BinaryOperator.GtEq, 6),
            TokenKind.LessOrEqual => (BinaryOperator.LtEq, 6),
            TokenKind.Plus => (BinaryOperator.Add, 7),
            TokenKind.Minus => (BinaryOperator.Sub, 7),
            TokenKind.Multiply => (BinaryOperator.Mul, 8),
            TokenKind.Divide => (BinaryOperator.Div, 8),
            TokenKind.Remainder => (BinaryOperator.Rem, 8),
            _ => default
        };
        return precedence != 0;
    }

    private static bool TryGetUnaryOperator(TokenKind kind, out UnaryOperator op)
    {
        op = kind switch
        {
            TokenKind.Bang => UnaryOperator.Not,
            TokenKind.Minus => UnaryOperator.Neg,
            TokenKind.Plus => UnaryOperator.Plus,
            _ => UnaryOperator.None
        };
        return op != UnaryOperator.None;
    }

    private static bool IsIdentifier(TokenKind kind) =>
        kind is TokenKind.LowerIdentifier or TokenKind.UpperIdentifier;

    private bool IsContextualKeyword(string keyword) =>
        Current.Kind == TokenKind.LowerIdentifier && Current.TextSpan.SequenceEqual(keyword.AsSpan());

    private static bool CanStartExpression(TokenKind kind) =>
        IsIdentifier(kind)
        || kind is TokenKind.Integer or TokenKind.Float or TokenKind.String
            or TokenKind.VerbatimString or TokenKind.Character
            or TokenKind.True or TokenKind.False or TokenKind.Nil
            or TokenKind.LeftParen or TokenKind.LeftBracket or TokenKind.LeftBrace
            or TokenKind.Range or TokenKind.ExclusiveRange
            or TokenKind.Bang or TokenKind.Minus or TokenKind.Plus;

    private static bool IsExceptionTarget(SyntaxNode node) =>
        node is NameSyntax { Value: "Exception" };

    private static bool IsStatementNode(SyntaxNode node) => node is
        BindingSyntax or ReturnSyntax or YieldSyntax or YieldBreakSyntax or BreakSyntax or ContinueSyntax or ThrowSyntax
        or IfSyntax or WhileSyntax or ForSyntax or TryCatchSyntax or MatchSyntax or FunctionDeclarationSyntax
        or TypeDeclarationSyntax or ImplDeclarationSyntax or RegionSyntax or ConstDeclarationSyntax;

    public LexToken Consume()
    {
        var token = cursor.Consume();
        if (token.Kind != TokenKind.EndOfFile && tokensSinceError < MinimumErrorDistance)
        {
            tokensSinceError++;
        }

        return token;
    }

    public bool Check(TokenKind kind) => cursor.Check(kind);

    public bool Match(TokenKind kind)
    {
        if (!Check(kind))
        {
            return false;
        }

        Consume();
        return true;
    }

    public bool Expect(TokenKind kind)
    {
        if (Match(kind))
        {
            return true;
        }

        ReportExpected(kind);
        return false;
    }

    public bool ExpectSeparator()
    {
        if (Match(TokenKind.Semicolon))
        {
            return true;
        }

        if (Previous.Kind == TokenKind.LeftBrace
            || Current.Kind is TokenKind.EndOfFile or TokenKind.RightBrace
            || Current.AfterEol)
        {
            return true;
        }

        ReportExpected(TokenKind.Semicolon);
        return false;
    }

    public void Synchronize(params TokenKind[] recoveryTokens)
    {
        while (!IsAtEnd)
        {
            foreach (var kind in recoveryTokens)
            {
                if (Check(kind))
                {
                    return;
                }
            }

            Consume();
        }
    }

    public void SynchronizeStatement()
    {
        while (!IsAtEnd)
        {
            if (Previous.Kind == TokenKind.Semicolon || Current.AfterEol)
            {
                return;
            }

            if (Current.Kind is TokenKind.RightBrace
                or TokenKind.Let or TokenKind.Mut or TokenKind.Use
                or TokenKind.Func or TokenKind.Return or TokenKind.If
                or TokenKind.For or TokenKind.While or TokenKind.Match
                or TokenKind.Try or TokenKind.Throw or TokenKind.Import
                or TokenKind.Private)
            {
                return;
            }

            Consume();
        }
    }

    public void ReportExpected(TokenKind kind) =>
        Report(ParserError.TokenExpected, Current, TokenFacts.GetDisplayName(kind));

    public void Report(ParserError error, LexToken token, params object[] args)
    {
        if (Errors.Count >= MaximumErrorCount || tokensSinceError < MinimumErrorDistance)
        {
            return;
        }

        AddError(error, token, args);
        tokensSinceError = 0;
    }

    private void AddUnsuppressedError(ParserError error, LexToken token, params object[] args) =>
        AddError(error, token, args);

    private void AddError(ParserError error, LexToken token, params object[] args)
    {
        var detail = MessageCatalog.Format(MessageGroup.Parser, error.ToString(), args);
        Errors.Add(new BuildMessage(detail, BuildMessageType.Error, (int)error,
            token.Line, token.Column, fileName));
    }
}
