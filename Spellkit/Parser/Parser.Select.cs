using Spellkit.Parser.Model;

namespace Spellkit.Parser;

internal sealed partial class HandwrittenParser
{
    private int selectDepth;

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

        selectDepth++;
        while (!Check(TokenKind.RightBrace) && !IsAtEnd)
        {
            var state = ParseSelectState();
            if (state is not null)
            {
                declaration.States.Add(state);
            }
            else
            {
                SynchronizeStatement();
            }
        }
        selectDepth--;
        Expect(TokenKind.RightBrace);
        return declaration;
    }

    private SelectStateSyntax? ParseSelectState()
    {
        var initial = IsContextualKeyword("initial");
        if (initial)
        {
            Consume();
        }

        if (!IsContextualKeyword("state") || Peek(1).Kind != TokenKind.String)
        {
            ReportExpected(TokenKind.String);
            return null;
        }
        Consume();

        var name = (StringLiteralSyntax)ParseString();
        var state = new SelectStateSyntax(name.Location)
        {
            Name = name.Value ?? string.Empty,
            IsInitial = initial
        };

        if (!Expect(TokenKind.LeftBrace))
        {
            return null;
        }

        while (!Check(TokenKind.RightBrace) && !IsAtEnd)
        {
            var choice = ParseSelectChoice();
            if (choice is not null)
            {
                state.Choices.Add(choice);
            }
            else
            {
                SynchronizeStatement();
            }
        }

        Expect(TokenKind.RightBrace);
        return state;
    }

    private SelectChoiceSyntax? ParseSelectChoice()
    {
        if (!IsContextualKeyword("choose") || Peek(1).Kind != TokenKind.String)
        {
            ReportExpected(TokenKind.String);
            return null;
        }
        Consume();

        var name = (StringLiteralSyntax)ParseString();
        var choice = new SelectChoiceSyntax(name.Location) { Name = name.Value ?? string.Empty };
        if (Match(TokenKind.LeftParen))
        {
            while (!Check(TokenKind.RightParen) && !IsAtEnd)
            {
                var parameter = ParseSelectParameter();
                if (parameter is null)
                {
                    return null;
                }
                choice.Parameters.Add(parameter);
                if (!Match(TokenKind.Comma))
                {
                    break;
                }
            }
            Expect(TokenKind.RightParen);
        }

        while (true)
        {
            if (IsContextualKeyword("label"))
            {
                Consume();
                if (!Check(TokenKind.String))
                {
                    ReportExpected(TokenKind.String);
                    return null;
                }

                choice.Label = ((StringLiteralSyntax)ParseString()).Value;
                continue;
            }

            if (IsContextualKeyword("description"))
            {
                Consume();
                if (!Check(TokenKind.String))
                {
                    ReportExpected(TokenKind.String);
                    return null;
                }

                choice.Description = ((StringLiteralSyntax)ParseString()).Value;
                continue;
            }

            if (Match(TokenKind.When))
            {
                choice.Guard = ParseGuardExpression();
                if (choice.Guard is null)
                {
                    return null;
                }
                continue;
            }

            break;
        }

        if (!Expect(TokenKind.Arrow))
        {
            return null;
        }

        if (Check(TokenKind.LeftBrace))
        {
            choice.Body = ParseBlock()!;
            return choice;
        }

        if (IsContextualKeyword("exit"))
        {
            choice.Body = ParseExit();
            ExpectSeparator();
            return choice;
        }

        if (IsContextualKeyword("goto"))
        {
            choice.Body = ParseGoto();
            ExpectSeparator();
            return choice;
        }

        ReportExpected(TokenKind.LeftBrace);
        return null;
    }

    private SyntaxNode ParseGoto()
    {
        var token = Consume();
        if (!Check(TokenKind.String))
        {
            ReportExpected(TokenKind.String);
            return new GotoSyntax(token.Location) { State = string.Empty };
        }

        var state = (StringLiteralSyntax)ParseString();
        return new GotoSyntax(token.Location) { State = state.Value ?? string.Empty };
    }

    private ParameterSyntax? ParseSelectParameter()
    {
        if (!Check(TokenKind.LowerIdentifier))
        {
            ReportExpected(TokenKind.LowerIdentifier);
            return null;
        }

        var name = Consume();
        return new ParameterSyntax(name.Location) { Name = name.Text };
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
}
