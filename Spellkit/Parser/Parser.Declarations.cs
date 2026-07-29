using Spellkit.Parser.Model;
using System.Collections.Generic;
using System.Linq;

namespace Spellkit.Parser;

internal sealed partial class HandwrittenParser
{
    private ConstDeclarationSyntax ParseConstDeclaration()
    {
        var keyword = Consume();
        var declaration = new ConstDeclarationSyntax(keyword.Location);
        if (Match(TokenKind.LeftBrace))
        {
            while (!Check(TokenKind.RightBrace) && !IsAtEnd)
            {
                var entry = ParseConstEntry();
                if (entry is not null)
                {
                    declaration.Declarations.Add(entry);
                }

                if (!Match(TokenKind.Comma))
                {
                    break;
                }
            }
            Expect(TokenKind.RightBrace);
        }
        else
        {
            var entry = ParseConstEntry();
            if (entry is not null)
            {
                declaration.Declarations.Add(entry);
            }
        }
        return declaration;
    }

    private BindingSyntax? ParseConstEntry()
    {
        if (!IsIdentifier(Current.Kind))
        {
            ReportExpected(TokenKind.UpperIdentifier);
            return null;
        }
        var name = Consume();
        var binding = new BindingSyntax(name.Location)
        {
            Constant = true,
            Pattern = new NamePatternSyntax(name.Location) { Name = name.Text }
        };
        binding.Init = Match(TokenKind.Equal)
            ? ParseExpression()!
            : new StringLiteralSyntax(name.Location) { Value = name.Text };
        return binding;
    }
}

internal sealed partial class HandwrittenParser
{
    private FunctionDeclarationSyntax? ParseFunctionDeclaration()
    {
        var isStatic = Match(TokenKind.Static);
        if (!Expect(TokenKind.Func))
        {
            return null;
        }

        var function = ParseFunctionSignature(isStatic);
        if (function is null)
        {
            return null;
        }

        functions.Push(function);
        if (Check(TokenKind.LeftBrace))
        {
            function.Body = ParseBlock();
        }
        else if (Match(TokenKind.Arrow))
        {
            function.Body = ParseFunctionArrowBody();
            ExpectSeparator();
        }
        else
        {
            ReportExpected(TokenKind.LeftBrace);
        }

        functions.Pop();
        return function;
    }

    private FunctionDeclarationSyntax? ParseFunctionSignature(bool isStatic)
    {
        var getter = Match(TokenKind.Get);
        var setter = !getter && Match(TokenKind.Set);
        var location = Current.Location;
        var function = new FunctionDeclarationSyntax(location)
        {
            IsStatic = isStatic,
            Getter = getter,
            Setter = setter
        };

        if (!TryParseFunctionName(out var name, out var typeName))
        {
            return null;
        }

        function.Name = name;
        function.TypeName = typeName;

        if (Match(TokenKind.LeftBracket))
        {
            function.IsIndexer = true;
            PromoteFunctionNameToType(function);
            ParseFunctionParameters(function, TokenKind.RightBracket);
            Expect(TokenKind.RightBracket);
            function.Name = setter ? "op_set" : "op_get";
        }
        else if (Match(TokenKind.LeftParen))
        {
            ParseFunctionParameters(function, TokenKind.RightParen);
            Expect(TokenKind.RightParen);
        }
        else if (Match(TokenKind.As))
        {
            PromoteFunctionNameToType(function);
            function.Name = null;
            function.TargetTypeName = ParseDeclaredTypeName();
        }
        else
        {
            Report(ParserError.InvalidFunction, Current);
            return null;
        }

        if (function.TargetTypeName is null && Match(TokenKind.Colon))
        {
            function.ReturnTypeAnnotation = ParseTypeAnnotation();
        }

        return function;
    }

    private bool TryParseFunctionName(out string? name, out Qualident? typeName)
    {
        name = null;
        typeName = null;
        if (!IsIdentifier(Current.Kind))
        {
            ReportExpected(TokenKind.LowerIdentifier);
            return false;
        }

        var first = Consume().Text;
        if (!Match(TokenKind.Dot) && !IsFunctionOperator(Current.Kind))
        {
            name = first;
            return true;
        }

        if (Previous.Kind == TokenKind.Dot)
        {
            if (!TryConsumeFunctionName(out var second))
            {
                return false;
            }

            if (Match(TokenKind.Dot))
            {
                if (!TryConsumeFunctionName(out var third))
                {
                    return false;
                }

                typeName = new Qualident(second, first);
                name = third;
            }
            else
            {
                typeName = new Qualident(first);
                name = second;
            }
        }
        else
        {
            typeName = new Qualident(first);
            name = Consume().Text;
        }
        return true;
    }

    private bool TryConsumeFunctionName(out string name)
    {
        if (IsIdentifier(Current.Kind) || IsFunctionOperator(Current.Kind))
        {
            name = Consume().Text;
            return true;
        }
        ReportExpected(TokenKind.LowerIdentifier);
        name = string.Empty;
        return false;
    }

    private void PromoteFunctionNameToType(FunctionDeclarationSyntax function)
    {
        if (function.TypeName?.Parent is not null)
        {
            Report(ParserError.InvalidTypeName, Current);
            return;
        }

        function.TypeName = function.TypeName is null
            ? new Qualident(function.Name!)
            : new Qualident(function.Name!, function.TypeName.Local);
    }

    private void ParseFunctionParameters(FunctionDeclarationSyntax function, TokenKind closingToken)
    {
        while (!Check(closingToken) && !IsAtEnd)
        {
            var parameter = ParseFunctionParameter();
            if (parameter is not null)
            {
                function.Parameters.Add(parameter);
            }

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
    }

    private ParameterSyntax? ParseFunctionParameter()
    {
        TypeAnnotation? prefixAnnotation = null;
        if (IsTypeAnnotationPrefix())
        {
            prefixAnnotation = ParseTypeAnnotation();
        }

        if (Current.Kind != TokenKind.LowerIdentifier)
        {
            ReportExpected(TokenKind.LowerIdentifier);
            return null;
        }

        var name = Consume();
        var parameter = new ParameterSyntax(name.Location)
        {
            Name = name.Text,
            TypeAnnotation = prefixAnnotation
        };
        if (Match(TokenKind.Colon))
        {
            parameter.TypeAnnotation = ParseTypeAnnotation();
        }

        if (Match(TokenKind.Equal))
        {
            parameter.DefaultValue = ParseExpression();
        }

        if (Match(TokenKind.Ellipsis))
        {
            parameter.IsVarArgs = true;
        }

        return parameter;
    }

    private TypeAnnotation? ParseTypeAnnotation()
    {
        var terms = new List<(Qualident Name, IReadOnlyList<TypeAnnotation> Arguments)>();

        do
        {
            var typeName = ParseTypeHintName();
            if (typeName is null)
            {
                return null;
            }

            var typeArguments = ParseTypeHintArguments();
            terms.Add((typeName, typeArguments));

            if (Match(TokenKind.Question)
                && !terms.Any(term => term.Name.Parent is null
                    && term.Name.Local == "Nil"
                    && term.Arguments.Count == 0))
            {
                terms.Add((new Qualident("Nil"), Array.Empty<TypeAnnotation>()));
            }
        }
        while (Match(TokenKind.Pipe));

        TypeAnnotation? annotation = null;
        for (var i = terms.Count - 1; i >= 0; i--)
        {
            annotation = new TypeAnnotation(terms[i].Name, terms[i].Arguments, annotation);
        }

        return annotation;
    }

    private IReadOnlyList<TypeAnnotation> ParseTypeHintArguments()
    {
        if (!Match(TokenKind.Less))
        {
            return Array.Empty<TypeAnnotation>();
        }

        var arguments = new List<TypeAnnotation>();
        if (Check(TokenKind.Greater))
        {
            ReportExpected(TokenKind.UpperIdentifier);
        }

        while (!Check(TokenKind.Greater) && !IsAtEnd)
        {
            var argument = ParseTypeAnnotation();
            if (argument is not null)
            {
                arguments.Add(argument);
            }

            if (!Match(TokenKind.Comma))
            {
                break;
            }

            if (Check(TokenKind.Greater))
            {
                ReportExpected(TokenKind.UpperIdentifier);
                break;
            }
        }
        Expect(TokenKind.Greater);
        return arguments;
    }

    private Qualident? ParseTypeHintName()
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

    private Qualident? ParseDeclaredTypeName()
    {
        if (Current.Kind == TokenKind.UpperIdentifier)
        {
            return new Qualident(Consume().Text);
        }

        if (Current.Kind == TokenKind.LowerIdentifier
            && Peek(1).Kind == TokenKind.Dot
            && Peek(2).Kind == TokenKind.UpperIdentifier)
        {
            var parent = Consume().Text;
            Consume();
            return new Qualident(Consume().Text, parent);
        }

        ReportExpected(TokenKind.UpperIdentifier);
        return null;
    }

    private bool IsTypeAnnotationPrefix() =>
        Current.Kind == TokenKind.UpperIdentifier
        || Current.Kind == TokenKind.LowerIdentifier && Peek(1).Kind == TokenKind.Dot;

    private SyntaxNode? ParseFunctionArrowBody()
    {
        if (IsContextualKeyword("guard"))
        {
            return ParseIf(isExpression: false);
        }

        return Current.Kind switch
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
            TokenKind.Match => ParseMatch(),
            _ => ParseAssignment()
        };
    }

    private bool IsLambdaStart()
    {
        if (!allowLambda)
        {
            return false;
        }

        if (Current.Kind == TokenKind.LowerIdentifier)
        {
            return Peek(1).Kind == TokenKind.Arrow;
        }

        if (Current.Kind != TokenKind.LeftParen)
        {
            return false;
        }

        var depth = 0;
        var offset = 0;
        var inDefaultValue = false;
        var expectsParameter = true;
        while (true)
        {
            var kind = Peek(offset).Kind;
            if (kind == TokenKind.EndOfFile)
            {
                return false;
            }

            if (kind == TokenKind.LeftParen)
            {
                depth++;
            }
            else if (kind == TokenKind.RightParen)
            {
                if (--depth == 0)
                {
                    return Peek(offset + 1).Kind == TokenKind.Arrow;
                }
            }
            else if (depth == 1)
            {
                if (expectsParameter)
                {
                    if (kind is not TokenKind.LowerIdentifier and not TokenKind.UpperIdentifier)
                    {
                        return false;
                    }

                    expectsParameter = false;
                }
                else if (kind == TokenKind.Equal)
                {
                    inDefaultValue = true;
                }
                else if (kind == TokenKind.Comma)
                {
                    inDefaultValue = false;
                    expectsParameter = true;
                }
                else if (!inDefaultValue && IsNonParameterOperator(kind))
                {
                    return false;
                }
            }
            offset++;
        }
    }

    private static bool IsNonParameterOperator(TokenKind kind) => kind is
        TokenKind.LogicalOr or TokenKind.LogicalAnd
        or TokenKind.EqualEqual or TokenKind.NotEqual
        or TokenKind.Greater or TokenKind.Less
        or TokenKind.GreaterOrEqual or TokenKind.LessOrEqual
        or TokenKind.Coalesce or TokenKind.Plus or TokenKind.Minus
        or TokenKind.Multiply or TokenKind.Divide or TokenKind.Remainder
        or TokenKind.Range or TokenKind.ExclusiveRange;

    private FunctionDeclarationSyntax? ParseLambda()
    {
        var location = Current.Location;
        var function = new FunctionDeclarationSyntax(location);
        if (Match(TokenKind.LeftParen))
        {
            ParseFunctionParameters(function, TokenKind.RightParen);
            Expect(TokenKind.RightParen);
        }
        else
        {
            var parameter = ParseFunctionParameter();
            if (parameter is not null)
            {
                function.Parameters.Add(parameter);
            }
        }

        if (!Expect(TokenKind.Arrow))
        {
            return null;
        }

        functions.Push(function);
        function.Body = ParseExpression();
        functions.Pop();
        return function;
    }

    private static bool IsFunctionOperator(TokenKind kind) => kind is
        TokenKind.Plus or TokenKind.Minus or TokenKind.Multiply or TokenKind.Divide
        or TokenKind.Remainder
        or TokenKind.Bang or TokenKind.EqualEqual or TokenKind.NotEqual
        or TokenKind.Greater or TokenKind.Less or TokenKind.GreaterOrEqual
        or TokenKind.LessOrEqual or TokenKind.DoubleLess or TokenKind.DoubleGreater;
}

internal sealed partial class HandwrittenParser
{
    private bool IsTypeDeclarationStart() =>
        IsContextualKeyword("struct")
        || IsContextualKeyword("enum")
        || IsContextualKeyword("trait")
        || IsContextualKeyword("impl");

    private SyntaxNode? ParseTypeOrImplDeclaration()
    {
        if (IsContextualKeyword("struct"))
        {
            return ParseStructDeclaration();
        }

        if (IsContextualKeyword("enum"))
        {
            return ParseEnumDeclaration();
        }

        if (IsContextualKeyword("trait"))
        {
            return ParseTraitDeclaration();
        }

        return ParseImplDeclaration();
    }

    private TypeDeclarationSyntax? ParseStructDeclaration()
    {
        var keyword = Consume();
        var name = ParseDeclarationName();
        if (name is null || !Expect(TokenKind.LeftBrace))
        {
            return null;
        }

        var type = new TypeDeclarationSyntax(keyword.Location)
        {
            Style = TypeDeclarationStyle.Struct,
            Name = name
        };
        var constructor = new FunctionDeclarationSyntax(keyword.Location)
        {
            Name = name,
            IsStatic = true,
            IsConstructor = true,
            TypeName = new Qualident(name)
        };
        ParseTypeParameters(constructor, TokenKind.RightBrace);
        Expect(TokenKind.RightBrace);
        type.Constructors.Add(constructor);
        return type;
    }

    private TypeDeclarationSyntax? ParseEnumDeclaration()
    {
        var keyword = Consume();
        var name = ParseDeclarationName();
        if (name is null || !Expect(TokenKind.LeftBrace))
        {
            return null;
        }

        var type = new TypeDeclarationSyntax(keyword.Location)
        {
            Style = TypeDeclarationStyle.Enum,
            Name = name
        };
        while (!Check(TokenKind.RightBrace) && !IsAtEnd)
        {
            var constructor = ParseEnumConstructor(type);
            if (constructor is not null)
            {
                type.Constructors.Add(constructor);
            }

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
        Expect(TokenKind.RightBrace);
        return type;
    }

    private FunctionDeclarationSyntax? ParseEnumConstructor(TypeDeclarationSyntax type)
    {
        if (!IsIdentifier(Current.Kind))
        {
            ReportExpected(TokenKind.UpperIdentifier);
            return null;
        }
        var name = Consume();
        var constructor = new FunctionDeclarationSyntax(name.Location)
        {
            Name = name.Text,
            IsStatic = true,
            IsConstructor = true,
            TypeName = new Qualident(type.Name)
        };
        if (Match(TokenKind.LeftParen))
        {
            ParseTypeParameters(constructor, TokenKind.RightParen);
            Expect(TokenKind.RightParen);
        }
        if (Check(TokenKind.LeftBrace))
        {
            constructor.Body = ParseBlock();
        }

        return constructor;
    }

    private TypeDeclarationSyntax? ParseTraitDeclaration()
    {
        var keyword = Consume();
        var name = ParseDeclarationName();
        if (name is null || !Expect(TokenKind.LeftBrace))
        {
            return null;
        }

        var type = new TypeDeclarationSyntax(keyword.Location)
        {
            Style = TypeDeclarationStyle.Trait,
            Name = name
        };
        while (!Check(TokenKind.RightBrace) && !IsAtEnd)
        {
            if (!Expect(TokenKind.Func))
            {
                Synchronize(TokenKind.Func, TokenKind.RightBrace);
                continue;
            }
            var function = ParseFunctionSignature(isStatic: false);
            if (function is not null && ValidateTraitContract(function))
            {
                function.TypeName = new Qualident(type.Name);
                type.Contracts.Add(function);
            }
            ExpectSeparator();
        }
        Expect(TokenKind.RightBrace);
        return type;
    }

    private ImplDeclarationSyntax? ParseImplDeclaration()
    {
        var keyword = Consume();
        var targetName = ParseDeclarationName();
        if (targetName is null)
        {
            return null;
        }

        var impl = new ImplDeclarationSyntax(keyword.Location) { TargetName = targetName };

        if (Match(TokenKind.With))
        {
            do
            {
                var mixin = ParseDeclaredTypeName();
                if (mixin is not null)
                {
                    impl.Mixins.Add(mixin);
                }
            }
            while (Match(TokenKind.Comma));
        }

        if (!Expect(TokenKind.LeftBrace))
        {
            return null;
        }

        while (!Check(TokenKind.RightBrace) && !IsAtEnd)
        {
            SyntaxNode? member;
            if (Current.Kind is TokenKind.Func or TokenKind.Static)
            {
                member = ParseFunctionDeclaration();
            }
            else if (Current.Kind is TokenKind.Let or TokenKind.Mut or TokenKind.Use)
            {
                member = ParseBinding();
                ExpectSeparator();
            }
            else
            {
                Report(ParserError.ExpectedFunction, Current);
                SynchronizeStatement();
                if (!Check(TokenKind.RightBrace) && !IsAtEnd)
                {
                    Consume();
                }

                continue;
            }

            if (member is not null && ValidateImplMember(member))
            {
                impl.Members.Add(member);
            }
        }
        Expect(TokenKind.RightBrace);
        return impl;
    }

    private void ParseTypeParameters(FunctionDeclarationSyntax constructor, TokenKind closingToken)
    {
        while (!Check(closingToken) && !IsAtEnd)
        {
            var parameter = ParseTypeParameter();
            if (parameter is not null)
            {
                constructor.Parameters.Add(parameter);
            }

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }
    }

    private TypeParameterSyntax? ParseTypeParameter()
    {
        var mutable = Match(TokenKind.Mut);
        if (Current.Kind != TokenKind.LowerIdentifier)
        {
            ReportExpected(TokenKind.LowerIdentifier);
            return null;
        }

        var name = Consume();
        var parameter = new TypeParameterSyntax(name.Location)
        {
            Name = name.Text,
            Mutable = mutable
        };
        if (Match(TokenKind.Colon))
        {
            parameter.TypeAnnotation = ParseTypeAnnotation();
        }

        if (Match(TokenKind.Equal))
        {
            parameter.DefaultValue = ParseExpression();
        }

        if (Match(TokenKind.Ellipsis))
        {
            parameter.IsVarArgs = true;
        }

        return parameter;
    }

    private string? ParseDeclarationName()
    {
        if (!IsIdentifier(Current.Kind))
        {
            ReportExpected(TokenKind.UpperIdentifier);
            return null;
        }
        return Consume().Text;
    }

    private bool ValidateTraitContract(FunctionDeclarationSyntax function)
    {
        if (function.TypeName is null
            && function.TargetTypeName is null
            && !function.IsStatic
            && function.Name is not null)
        {
            return true;
        }

        AddUnsuppressedError(ParserError.InvalidFunctionName,
            new LexToken(TokenKind.Func, string.Empty, 0, 0,
                function.Location.Line, function.Location.Column, false));
        return false;
    }

    private bool ValidateImplMember(SyntaxNode node) => node switch
    {
        BindingSyntax binding => ValidateImplField(binding),
        FunctionDeclarationSyntax function => ValidateImplFunction(function),
        _ => InvalidImplMember(node)
    };

    private bool ValidateImplField(BindingSyntax binding)
    {
        if (!binding.AutoClose && binding.Pattern is NamePatternSyntax)
        {
            return true;
        }

        AddErrorAt(ParserError.InvalidBinding, binding.Location);
        return false;
    }

    private bool ValidateImplFunction(FunctionDeclarationSyntax function)
    {
        if (function.TypeName is not null || function.TargetTypeName is not null)
        {
            AddErrorAt(ParserError.InvalidFunctionName, function.Location);
            return false;
        }
        if (function.Name == "init"
            && (function.IsStatic || function.Getter || function.Setter || function.IsIndexer))
        {
            AddErrorAt(ParserError.InvalidFunctionName, function.Location);
            return false;
        }
        return true;
    }

    private bool InvalidImplMember(SyntaxNode node)
    {
        AddErrorAt(ParserError.ExpectedFunction, node.Location);
        return false;
    }

    private void AddErrorAt(ParserError error, Location location) =>
        AddUnsuppressedError(error,
            new LexToken(TokenKind.Unknown, string.Empty, 0, 0,
                location.Line, location.Column, false));
}

internal sealed partial class HandwrittenParser
{
    private RegionSyntax ParseRegion()
    {
        var regionToken = Consume();
        string? name = null;
        if (IsIdentifier(Current.Kind))
        {
            name = Consume().Text;
        }
        else if (Check(TokenKind.String))
        {
            name = ParseStringValue(Consume());
        }
        else
        {
            ReportExpected(TokenKind.String);
        }

        var block = new BlockSyntax(default);
        var regionImports = new List<ImportSyntax>();
        while (!Check(TokenKind.EndRegion) && !IsAtEnd)
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
                    regionImports.Add(import);
                }
            }
            else
            {
                var statement = ParseStatement();
                if (statement is not null && (!isPrivate || MarkPrivateDeclaration(statement)))
                {
                    block.Nodes.Add(statement);
                }
            }
            if (cursor.Mark() == start.TokenPosition)
            {
                Consume();
            }
        }
        Expect(TokenKind.EndRegion);
        return new RegionSyntax(
            name ?? string.Empty,
            new SpellkitCodeModel(block, regionImports.ToArray(), fileName),
            regionToken.Location);
    }
}
