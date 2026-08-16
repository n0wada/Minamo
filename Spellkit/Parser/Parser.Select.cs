using Spellkit.Parser.Model;
using System.Collections.Generic;

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

        while (Current.Kind is TokenKind.Let or TokenKind.Mut)
        {
            var local = ParseBinding() as BindingSyntax;
            if (local is not null)
            {
                declaration.Locals.Add(local);
                ExpectSeparator();
            }
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

        if (!IsContextualKeyword("state"))
        {
            ReportExpected(TokenKind.LowerIdentifier);
            if (!Check(TokenKind.RightBrace) && !IsAtEnd)
            {
                Consume();
            }
            return null;
        }
        Consume();

        if (!IsIdentifier(Current.Kind))
        {
            ReportExpected(TokenKind.LowerIdentifier);
            if (!Check(TokenKind.RightBrace) && !IsAtEnd)
            {
                Consume();
            }
            return null;
        }

        var name = Consume();
        var state = new SelectStateSyntax(name.Location)
        {
            Name = name.Text,
            IsInitial = initial
        };

        if (!Expect(TokenKind.LeftBrace))
        {
            return null;
        }

        while (!Check(TokenKind.RightBrace) && !IsAtEnd)
        {
            if (IsContextualKeyword("enter"))
            {
                Consume();
                var enter = ParseSelectStateHookBody();
                if (enter is not null)
                {
                    state.Enter = enter;
                }
                else
                {
                    SynchronizeStatement();
                }
                continue;
            }

            if (IsContextualKeyword("leave"))
            {
                Consume();
                var leave = ParseSelectStateHookBody();
                if (leave is not null)
                {
                    state.Leave = leave;
                }
                else
                {
                    SynchronizeStatement();
                }
                continue;
            }

            if (IsContextualKeyword("view"))
            {
                Consume();
                var view = ParseSelectViewBody();
                if (view is not null)
                {
                    state.View = view;
                }
                else
                {
                    SynchronizeStatement();
                }
                continue;
            }

            if (IsContextualKeyword("otherwise"))
            {
                Consume();
                var otherwise = ParseSelectActionBody();
                if (otherwise is not null)
                {
                    state.Otherwise = otherwise;
                }
                else
                {
                    SynchronizeStatement();
                }
                continue;
            }

            if (IsContextualKeyword("choose"))
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
                continue;
            }

            if (Check(TokenKind.For))
            {
                var dynamicChoices = ParseSelectDynamicChoices();
                if (dynamicChoices is not null)
                {
                    state.DynamicChoices.Add(dynamicChoices);
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
                    state.Events.Add(handler);
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
        ParseSelectParameters(choice.Parameters);

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

            if (Match(TokenKind.When))
            {
                choice.Guard = ParseGuardExpression();
                if (choice.Guard is null)
                {
                    return null;
                }
                continue;
            }

            if (IsContextualKeyword("view"))
            {
                Consume();
                var view = ParseSelectViewBody();
                if (view is null)
                {
                    return null;
                }

                choice.View = view;
                continue;
            }

            break;
        }

        var body = ParseSelectActionBody();
        if (body is null)
        {
            return null;
        }

        choice.Body = body;
        return choice;
    }

    private SelectDynamicChoiceGroupSyntax? ParseSelectDynamicChoices()
    {
        var keyword = Consume();
        if (!Check(TokenKind.LowerIdentifier))
        {
            ReportExpected(TokenKind.LowerIdentifier);
            return null;
        }

        var item = Consume();
        if (!Expect(TokenKind.In))
        {
            return null;
        }

        var source = ParseExpression();
        if (source is null || !Expect(TokenKind.LeftBrace))
        {
            return null;
        }

        var group = new SelectDynamicChoiceGroupSyntax(keyword.Location)
        {
            ItemName = item.Text,
            Source = source
        };
        while (!Check(TokenKind.RightBrace) && !IsAtEnd)
        {
            if (IsContextualKeyword("choose"))
            {
                var choice = ParseSelectDynamicChoice();
                if (choice is not null)
                {
                    group.Choices.Add(choice);
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

        Expect(TokenKind.RightBrace);
        if (group.Choices.Count == 0)
        {
            Report(ParserError.InvalidStatement, keyword);
            return null;
        }
        return group;
    }

    private SelectDynamicChoiceSyntax? ParseSelectDynamicChoice()
    {
        var keyword = Consume();
        var id = ParseGuardExpression();
        if (id is null || !ExpectSeparator())
        {
            return null;
        }

        var choice = new SelectDynamicChoiceSyntax(keyword.Location) { Id = id };
        while (!Check(TokenKind.Arrow) && !Check(TokenKind.RightBrace) && !IsAtEnd)
        {
            if (IsContextualKeyword("label"))
            {
                Consume();
                choice.Label = ParseSelectDynamicChoiceExpression();
                if (choice.Label is null)
                {
                    return null;
                }
                continue;
            }

            if (Match(TokenKind.When))
            {
                choice.Guard = ParseSelectDynamicChoiceExpression();
                if (choice.Guard is null)
                {
                    return null;
                }
                continue;
            }

            if (IsContextualKeyword("view"))
            {
                Consume();
                choice.View = ParseSelectViewBody();
                if (choice.View is null)
                {
                    return null;
                }
                continue;
            }

            Report(ParserError.InvalidStatement, Current);
            return null;
        }

        var body = ParseSelectActionBody();
        if (body is null)
        {
            return null;
        }

        choice.Body = body;
        return choice;
    }

    private SyntaxNode? ParseSelectDynamicChoiceExpression()
    {
        var expression = ParseGuardExpression();
        return expression is not null && ExpectSeparator() ? expression : null;
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

    private SyntaxNode? ParseSelectStateHookBody()
    {
        if (!Expect(TokenKind.Arrow))
        {
            return null;
        }

        if (!Check(TokenKind.LeftBrace))
        {
            ReportExpected(TokenKind.LeftBrace);
            return null;
        }

        return ParseBlock();
    }

    private SyntaxNode? ParseSelectViewBody()
    {
        if (!Expect(TokenKind.Arrow))
        {
            return null;
        }

        var view = ParseExpression();
        if (view is null)
        {
            return null;
        }

        ExpectSeparator();
        return view;
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
            return ParseBlock();
        }

        if (IsContextualKeyword("exit"))
        {
            var body = ParseExit();
            ExpectSeparator();
            return body;
        }

        if (IsContextualKeyword("goto"))
        {
            var body = ParseGoto();
            ExpectSeparator();
            return body;
        }

        ReportExpected(TokenKind.LeftBrace);
        return null;
    }

    private SyntaxNode ParseGoto()
    {
        var token = Consume();
        if (!IsIdentifier(Current.Kind))
        {
            ReportExpected(TokenKind.LowerIdentifier);
            return new GotoSyntax(token.Location) { State = string.Empty };
        }

        var state = Consume();
        return new GotoSyntax(token.Location) { State = state.Text };
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
}
