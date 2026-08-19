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

        selectDepth++;
        if (IsContextualKeyword("initial") || IsContextualKeyword("state"))
        {
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
        }
        else
        {
            var state = new SelectStateSyntax(keyword.Location)
            {
                Name = string.Empty,
                IsInitial = true,
                IsImplicit = true
            };
            ParseSelectStateContents(state);
            declaration.States.Add(state);
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

        ParseSelectStateContents(state);
        Expect(TokenKind.RightBrace);
        return state;
    }

    private void ParseSelectStateContents(SelectStateSyntax state)
    {
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

            if (IsContextualKeyword("on")
                && Peek(1).Kind == TokenKind.LowerIdentifier
                && Peek(1).TextSpan.SequenceEqual("empty".AsSpan()))
            {
                Consume();
                Consume();
                var empty = ParseSelectActionBody();
                if (empty is not null)
                {
                    state.Empty = empty;
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
                if (choice is SelectChoiceSyntax staticChoice)
                {
                    state.Choices.Add(staticChoice);
                }
                else if (choice is SelectDynamicChoiceGroupSyntax dynamicChoice)
                {
                    state.DynamicChoices.Add(dynamicChoice);
                }
                else if (choice is SelectChoiceSpreadSyntax spread)
                {
                    state.ChoiceSpreads.Add(spread);
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
    }

    private SyntaxNode? ParseSelectChoice()
    {
        if (!IsContextualKeyword("choose"))
        {
            return null;
        }

        if (Peek(1).Kind == TokenKind.Ellipsis)
        {
            var spreadKeyword = Consume();
            Consume();
            var target = ParseGuardExpression();
            if (target is null)
            {
                return null;
            }

            ExpectSeparator();
            return new SelectChoiceSpreadSyntax(spreadKeyword.Location) { Target = target };
        }

        // A string immediately followed by a parameter list remains the static-choice form.
        // Dynamic choices do not accept host-supplied parameters.
        if (Peek(1).Kind == TokenKind.String && Peek(2).Kind == TokenKind.LeftParen)
        {
            return ParseSelectStaticChoiceWithParameters();
        }

        var keyword = Consume();
        var id = ParseGuardExpression();
        if (id is null)
        {
            return null;
        }

        SyntaxNode? label = null;
        SyntaxNode? source = null;
        SyntaxNode? guard = null;
        string? itemName = null;

        while (true)
        {
            if (IsContextualKeyword("label"))
            {
                Consume();
                label = ParseGuardExpression();
                if (label is null)
                {
                    return null;
                }
                continue;
            }

            if (Check(TokenKind.For))
            {
                if (itemName is not null)
                {
                    Report(ParserError.InvalidStatement, Current);
                    return null;
                }

                Consume();
                if (!Check(TokenKind.LowerIdentifier))
                {
                    ReportExpected(TokenKind.LowerIdentifier);
                    return null;
                }

                itemName = Consume().Text;
                if (!Expect(TokenKind.In))
                {
                    return null;
                }

                source = ParseGuardExpression();
                if (source is null)
                {
                    return null;
                }
                continue;
            }

            if (Match(TokenKind.When))
            {
                guard = ParseGuardExpression();
                if (guard is null)
                {
                    return null;
                }
                continue;
            }

            break;
        }

        var body = ParseSelectActionBody();
        if (body is null)
        {
            return null;
        }

        if (itemName is not null)
        {
            var group = new SelectDynamicChoiceGroupSyntax(keyword.Location)
            {
                ItemName = itemName,
                Source = source!
            };
            group.Choices.Add(new SelectDynamicChoiceSyntax(keyword.Location)
            {
                Id = id,
                Label = label,
                Guard = guard,
                Body = body
            });
            return group;
        }

        if (id is not StringLiteralSyntax name)
        {
            ReportExpected(TokenKind.String);
            return null;
        }

        if (label is not null && label is not StringLiteralSyntax)
        {
            ReportExpected(TokenKind.String);
            return null;
        }

        return new SelectChoiceSyntax(name.Location)
        {
            Name = name.Value ?? string.Empty,
            Label = (label as StringLiteralSyntax)?.Value,
            Guard = guard,
            Body = body
        };
    }

    private SelectChoiceSyntax? ParseSelectStaticChoiceWithParameters()
    {
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

    private ArrayLiteralSyntax? ParseSelectDescription()
    {
        if (!Check(TokenKind.LeftBracket))
        {
            ReportExpected(TokenKind.LeftBracket);
            return null;
        }

        var description = (ArrayLiteralSyntax)ParseArray();
        if (description.Elements.Count == 0)
        {
            description.IsDictionaryLiteral = true;
        }

        if (!description.IsDictionaryLiteral)
        {
            Report(ParserError.InvalidExpression, Current);
            return null;
        }

        foreach (var element in description.Elements)
        {
            if (element is not LabelLiteralSyntax { FromString: true })
            {
                Report(ParserError.InvalidExpression, Current);
                return null;
            }
        }

        ExpectSeparator();
        return description;
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
