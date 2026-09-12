using Minamo.Parser.Model;
using System.Collections.Generic;

namespace Minamo.Parser;

internal sealed partial class HandwrittenParser
{
    private readonly Stack<int> selectActionFunctionDepths = new();
    private bool stopAtSelectMetadata;

    private bool IsParsingSelectAction =>
        selectActionFunctionDepths.Count != 0
        && functions.Count == selectActionFunctionDepths.Peek();

    private SelectDeclarationSyntax? ParseSelectDeclaration()
    {
        var keyword = Consume();
        var declaration = new SelectDeclarationSyntax(keyword.Location);
        if (Check(TokenKind.LowerIdentifier))
        {
            declaration.Name = Consume().Text;
        }
        if (!Expect(TokenKind.LeftBrace))
        {
            return null;
        }

        if (IsContextualKeyword("desc"))
        {
            Consume();
            var description = ParseSelectDescription();
            if (description is not null)
            {
                declaration.Description = description;
            }
            else
            {
                SynchronizeStatement();
            }
        }

        while (Current.Kind is TokenKind.Let or TokenKind.Mut)
        {
            var local = ParseBinding() as BindingSyntax;
            if (local is not null)
            {
                declaration.Locals.Add(local);
                ExpectSeparator();
            }
        }

        ParseSelectContents(declaration);
        Expect(TokenKind.RightBrace);
        return declaration;
    }

    private void ParseSelectContents(SelectDeclarationSyntax declaration)
    {
        while (!Check(TokenKind.RightBrace) && !IsAtEnd)
        {
            if (IsContextualKeyword("prop"))
            {
                var property = ParseSelectProperty();
                if (property is not null)
                {
                    declaration.Properties.Add(property);
                }
                else
                {
                    SynchronizeStatement();
                }
                continue;
            }

            if (IsContextualKeyword("case"))
            {
                var choice = ParseSelectChoice();
                if (choice is not null)
                {
                    declaration.Choices.Add(choice);
                }
                else
                {
                    SynchronizeStatement();
                }
                continue;
            }

            if (IsContextualKeyword("on"))
            {
                var handler = ParseSelectEvent();
                if (handler is not null)
                {
                    declaration.Events.Add(handler);
                }
                else
                {
                    SynchronizeStatement();
                }
                continue;
            }

            Report(ParserError.InvalidStatement, Current);
            Consume();
            SynchronizeStatement();
        }
    }

    private SelectChoiceSyntax? ParseSelectChoice()
    {
        if (!IsContextualKeyword("case"))
        {
            return null;
        }

        Consume();
        if (!Check(TokenKind.String))
        {
            ReportExpected(TokenKind.String);
            return null;
        }

        var name = (StringLiteralSyntax)ParseString();
        var choice = new SelectChoiceSyntax(name.Location)
        {
            Name = name.Value ?? string.Empty
        };
        ParseSelectParameters(choice.Parameters);

        if (Match(TokenKind.When))
        {
            choice.Guard = ParseSelectGuardExpression();
            if (choice.Guard is null)
            {
                return null;
            }
        }

        if (Check(TokenKind.LeftBracket))
        {
            choice.Metadata = ParseSelectDictionary();
            if (choice.Metadata is null)
            {
                return null;
            }
        }

        var body = ParseSelectActionBody();
        if (body is null)
        {
            return null;
        }

        choice.Body = body;
        return choice;
    }

    private SyntaxNode? ParseSelectGuardExpression()
    {
        var previous = stopAtSelectMetadata;
        stopAtSelectMetadata = true;
        try
        {
            return ParseGuardExpression();
        }
        finally
        {
            stopAtSelectMetadata = previous;
        }
    }

    private bool IsSelectMetadataStart() =>
        stopAtSelectMetadata
        && Check(TokenKind.LeftBracket)
        && (Peek(1).Kind == TokenKind.RightBracket
            || Peek(1).Kind == TokenKind.String && Peek(2).Kind == TokenKind.Colon);

    private SelectPropertySyntax? ParseSelectProperty()
    {
        var keyword = Consume();
        string name;
        if (Check(TokenKind.LowerIdentifier))
        {
            name = Consume().Text;
        }
        else if (Check(TokenKind.String))
        {
            name = ((StringLiteralSyntax)ParseString()).Value ?? string.Empty;
        }
        else
        {
            ReportExpected(TokenKind.LowerIdentifier);
            return null;
        }

        ArrayLiteralSyntax? metadata = null;
        if (Check(TokenKind.LeftBracket))
        {
            metadata = ParseSelectDictionary();
            if (metadata is null)
            {
                return null;
            }
        }

        if (!Expect(TokenKind.Arrow))
        {
            return null;
        }

        var expression = ParseExpression();
        if (expression is null)
        {
            return null;
        }

        ExpectSeparator();
        return new SelectPropertySyntax(keyword.Location)
        {
            Name = name,
            Metadata = metadata,
            Expression = expression
        };
    }

    private SelectEventSyntax? ParseSelectEvent()
    {
        Consume();
        if (!Check(TokenKind.String))
        {
            ReportExpected(TokenKind.String);
            return null;
        }

        var name = (StringLiteralSyntax)ParseString();
        var handler = new SelectEventSyntax(name.Location) { Name = name.Value ?? string.Empty };
        ParseSelectParameters(handler.Parameters);
        var body = ParseSelectActionBody();
        if (body is null)
        {
            return null;
        }

        handler.Body = body;
        return handler;
    }

    private ArrayLiteralSyntax? ParseSelectDescription()
    {
        if (!Check(TokenKind.LeftBracket))
        {
            ReportExpected(TokenKind.LeftBracket);
            return null;
        }

        var description = ParseSelectDictionary();
        if (description is not null)
        {
            ExpectSeparator();
        }
        return description;
    }

    private ArrayLiteralSyntax? ParseSelectDictionary()
    {
        var dictionary = (ArrayLiteralSyntax)ParseArray();
        if (dictionary.Elements.Count == 0)
        {
            dictionary.IsDictionaryLiteral = true;
        }

        if (!dictionary.IsDictionaryLiteral)
        {
            Report(ParserError.InvalidExpression, Current);
            return null;
        }

        foreach (var element in dictionary.Elements)
        {
            if (element is not LabelLiteralSyntax { FromString: true })
            {
                Report(ParserError.InvalidExpression, Current);
                return null;
            }
        }

        return dictionary;
    }

    private void ParseSelectParameters(List<ParameterSyntax> parameters)
    {
        if (!Match(TokenKind.LeftParen))
        {
            return;
        }

        while (!Check(TokenKind.RightParen) && !IsAtEnd)
        {
            var parameter = ParseSelectParameter();
            if (parameter is null)
            {
                break;
            }

            parameters.Add(parameter);
            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        Expect(TokenKind.RightParen);
    }

    private SyntaxNode? ParseSelectActionBody()
    {
        if (!Expect(TokenKind.Arrow))
        {
            return null;
        }

        if (Check(TokenKind.LeftBrace))
        {
            selectActionFunctionDepths.Push(functions.Count);
            try
            {
                return ParseBlock();
            }
            finally
            {
                selectActionFunctionDepths.Pop();
            }
        }

        if (IsContextualKeyword("exit"))
        {
            var body = ParseExit();
            ExpectSeparator();
            return body;
        }

        if (IsContextualKeyword("goto"))
        {
            var body = ParseSelectGoto();
            ExpectSeparator();
            return body;
        }

        if (Check(TokenKind.Return))
        {
            var body = ParseSelectReturn();
            ExpectSeparator();
            return body;
        }

        ReportExpected(TokenKind.LeftBrace);
        return null;
    }

    private ParameterSyntax? ParseSelectParameter()
    {
        if (!Check(TokenKind.LowerIdentifier))
        {
            ReportExpected(TokenKind.LowerIdentifier);
            return null;
        }

        var name = Consume();
        var parameter = new ParameterSyntax(name.Location) { Name = name.Text };
        if (Match(TokenKind.Colon))
        {
            parameter.TypeAnnotation = ParseTypeAnnotation();
        }

        return parameter;
    }

    private SyntaxNode ParseExit()
    {
        var token = Consume();
        var node = new ExitSyntax(token.Location);
        if (CanStartSameLineExpression())
        {
            node.Expression = ParseExpression();
        }
        return node;
    }

    private SelectGotoSyntax ParseSelectGoto()
    {
        var token = Consume();
        var node = new SelectGotoSyntax(token.Location);
        if (!CanStartSameLineExpression())
        {
            Report(ParserError.InvalidExpression, Current);
            node.Target = new NilLiteralSyntax(token.Location);
            return node;
        }

        node.Target = ParseExpression() ?? new NilLiteralSyntax(token.Location);
        return node;
    }

    private SelectReturnSyntax ParseSelectReturn()
    {
        var token = Consume();
        var node = new SelectReturnSyntax(token.Location);
        if (CanStartSameLineExpression())
        {
            Report(ParserError.InvalidStatement, Current);
            ParseExpression();
        }

        return node;
    }
}
