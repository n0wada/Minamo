using Spellkit.Parser.Model;
using System;
using System.Collections.Generic;

namespace Spellkit.Compiler.Lowering;

internal sealed class LoweringPass
{
    private readonly Func<BlockSyntax, bool> hasAuto;
    private readonly bool noOptimizations;
    private int nextLabelId;

    public LoweringPass(Func<BlockSyntax, bool> hasAuto, bool noOptimizations)
    {
        this.hasAuto = hasAuto;
        this.noOptimizations = noOptimizations;
    }

    public LoweredModule LowerModule(SpkCodeModel codeModel, CompilerContext ctx, bool includeLangModule)
    {
        var importOffset = includeLangModule ? 1 : 0;
        var imports = new LoweredImport[codeModel.Imports.Length + importOffset];

        if (includeLangModule)
        {
            imports[0] = new LoweredImport(
                default,
                ImportKind.All,
                Alias: null,
                SymbolName: null,
                ModuleName: "lang",
                LocalPath: null);
        }

        for (var i = 0; i < codeModel.Imports.Length; i++)
        {
            imports[i + importOffset] = Lower(codeModel.Imports[i]);
        }

        return new LoweredModule(
            codeModel.FileName,
            imports,
            LowerNodeList(codeModel.Root.Nodes, ctx, keepLastResult: true));
    }

    public LoweredAssignment Lower(AssignmentSyntax node, CompilerContext ctx)
    {
        var target = LowerStoreTarget(node.Target, ctx);
        var kind = target.Kind switch
        {
            LoweredStoreKind.PublicMemberSetter when node.AutoAssign is BinaryOperator.Coalesce =>
                LoweredAssignmentKind.PublicMemberSetterCoalesceAssign,
            LoweredStoreKind.PublicMemberSetter when node.AutoAssign is not null =>
                LoweredAssignmentKind.PublicMemberSetterAutoAssign,
            LoweredStoreKind.PublicMemberSetter =>
                LoweredAssignmentKind.PublicMemberSetter,
            _ when node.AutoAssign is BinaryOperator.Coalesce =>
                LoweredAssignmentKind.CoalesceAssign,
            _ when node.AutoAssign is not null =>
                LoweredAssignmentKind.AutoAssign,
            _ => LoweredAssignmentKind.Simple
        };

        return new(node.Location, target, LowerNode(node.Value, ctx), node.AutoAssign, kind);
    }

    public LoweredFunctionDeclaration Lower(FunctionDeclarationSyntax node, CompilerContext ctx, bool needsValue, bool iteratorBody)
    {
        var lowered = new LoweredFunctionDeclaration(
            node.Location,
            node.TypeName,
            node.TargetTypeName,
            node.Name,
            node.IsStatic,
            node.IsIndexer,
            node.IsConstructor,
            node.Getter,
            node.Setter,
            node.IsIterator,
            node.IsImplInitializer,
            node.IsPrivate,
            LowerParameters(node.Parameters),
            null,
            needsValue,
            iteratorBody,
            IsStdCall(node));

        return lowered with
        {
            Body = node.Body is null ? null : LowerFunctionBody(node.Body, ctx)
        };
    }

    public LoweredSelectDeclaration Lower(SelectDeclarationSyntax node, CompilerContext ctx)
    {
        var states = new LoweredSelectState[node.States.Count];
        for (var i = 0; i < node.States.Count; i++)
        {
            var state = node.States[i];
            var choices = new LoweredSelectChoice[state.Choices.Count];
            for (var j = 0; j < state.Choices.Count; j++)
            {
                var choice = state.Choices[j];
                choices[j] = new(
                    choice.Location,
                    choice.Name,
                    LowerParameters(choice.Parameters),
                    choice.Label ?? choice.Name,
                    choice.Description,
                    choice.Guard is null ? null : LowerNode(choice.Guard, new CompilerContext()),
                    LowerNode(choice.Body, new CompilerContext()));
            }

            states[i] = new(state.Location, state.Name, state.IsInitial, choices);
        }

        return new(node.Location, node.Name, states);
    }

    public LoweredSelectInvocation Lower(SelectInvocationSyntax node) =>
        new(node.Location, node.Name);

    public LoweredControlTransfer Lower(GotoSyntax node) =>
        new(
            node.Location,
            new LoweredTuple(
                node.Location,
                [
                    new LoweredLiteral(node.Location, SelectControlSignal.Goto, LoweredLiteralKind.String),
                    new LoweredLiteral(node.Location, node.State, LoweredLiteralKind.String)
                ]),
            LoweredControlTransferKind.Return);

    public LoweredControlTransfer Lower(ExitSyntax node, CompilerContext ctx) =>
        new(
            node.Location,
            new LoweredTuple(
                node.Location,
                [
                    new LoweredLiteral(node.Location, SelectControlSignal.Exit, LoweredLiteralKind.String),
                    node.Expression is null
                        ? new LoweredLiteral(node.Location, null, LoweredLiteralKind.Nil)
                        : LowerNode(node.Expression, ctx)
                ]),
            LoweredControlTransferKind.Return);

    public LoweredNominalDeclaration Lower(TypeDeclarationSyntax node, bool needsValue)
    {
        var autoMixin = node.Style is TypeDeclarationStyle.Struct or TypeDeclarationStyle.Enum;
        return new(
            node.Location,
            node.Style,
            node.Name,
            node.IsPrivate,
            LowerFunctions(node.Constructors),
            LowerFunctions(node.Contracts),
            CopyQualidents(node.Mixins),
            LowerFields(node.PrivateFields),
            LowerFunctions(node.ProtectedMethods),
            node.Initializer is null ? null : Lower(node.Initializer, new CompilerContext(), needsValue: false, iteratorBody: false),
            needsValue,
            AutoLookup: autoMixin);
    }

    public LoweredImplDeclaration Lower(ImplDeclarationSyntax node, bool needsValue)
    {
        var members = new LoweredImplMember[node.Members.Count];

        for (var i = 0; i < node.Members.Count; i++)
        {
            members[i] = node.Members[i] switch
            {
                FunctionDeclarationSyntax function => new LoweredImplFunction(Lower(function, new CompilerContext(), needsValue: false, iteratorBody: false)),
                BindingSyntax field => new LoweredImplField(LowerField(field, new CompilerContext())),
                var invalid => new LoweredInvalidImplMember(invalid.Location)
            };
        }

        return new(node.Location, node.TargetName, CopyQualidents(node.Mixins), members, needsValue);
    }

    public LoweredImport Lower(ImportSyntax node) =>
        new(node.Location, node.Kind, node.Alias, node.SymbolName, node.ModuleName, node.LocalPath);

    public LoweredExpressionStatement Lower(ExpressionStatementSyntax node, CompilerContext ctx) =>
        new(node.Location, LowerNode(node.Expression, ctx));

    public LoweredConstDeclaration Lower(ConstDeclarationSyntax node, CompilerContext ctx) =>
        new(node.Location, node.IsPrivate, LowerBindingList(node.Declarations, ctx, node.IsPrivate));

    public LoweredBinding Lower(BindingSyntax node, CompilerContext ctx) =>
        new(
            node.Location,
            Lower(node.Pattern),
            node.Init is null ? null : LowerNode(node.Init, ctx),
            node.Constant,
            node.AutoClose,
            node.IsPrivate,
            node.Pattern.NodeType == NodeType.NamePattern,
            node.Init is INodeContainer nc ? nc.NodeCount : -1);

    public LoweredRebinding Lower(RebindingSyntax node, CompilerContext ctx) =>
        new(node.Location, Lower(node.Pattern), LowerNode(node.Init, ctx));

    public LoweredUnary Lower(UnaryOperationSyntax node, CompilerContext ctx) =>
        new(node.Location, LowerNode(node.Node, ctx), node.Operator);

    public LoweredBinary Lower(BinaryOperationSyntax node, CompilerContext ctx) =>
        node.Operator is BinaryOperator.Is
            ? new(node.Location, LowerNode(node.Left, ctx), null, node.Operator, Lower((PatternSyntax)node.Right))
            : new(node.Location, LowerNode(node.Left, ctx), LowerNode(node.Right, ctx), node.Operator);

    public LoweredName Lower(NameSyntax node) =>
        new(node.Location, node.Value);

    public LoweredApplication Lower(ApplicationSyntax node, CompilerContext ctx) =>
        new(node.Location, LowerNode(node.Target, ctx), LowerNodeList(node.Arguments, ctx));

    public LoweredControlTransfer Lower(BreakSyntax node, CompilerContext ctx) =>
        new(node.Location, node.Expression is null ? null : LowerNode(node.Expression, ctx), LoweredControlTransferKind.Break);

    public LoweredControlTransfer Lower(ContinueSyntax node) =>
        new(node.Location, null, LoweredControlTransferKind.Continue);

    public LoweredControlTransfer Lower(ReturnSyntax node, CompilerContext ctx) =>
        new(node.Location, node.Expression is null ? null : LowerNode(node.Expression, ctx), LoweredControlTransferKind.Return);

    public LoweredControlTransfer Lower(ThrowSyntax node, CompilerContext ctx) =>
        new(node.Location, node.Expression is null ? null : LowerNode(node.Expression, ctx), LoweredControlTransferKind.Throw);

    public LoweredControlTransfer Lower(YieldSyntax node, CompilerContext ctx) =>
        new(node.Location, LowerNode(node.Expression, ctx), LoweredControlTransferKind.Yield);

    public LoweredControlTransfer Lower(YieldBreakSyntax node) =>
        new(node.Location, null, LoweredControlTransferKind.YieldBreak);

    public LoweredYieldBlock Lower(YieldBlockSyntax node, CompilerContext ctx) =>
        new(node.Location, LowerNodeList(node.Elements, ctx));

    public LoweredCast Lower(AsSyntax node, CompilerContext ctx) =>
        new(node.Location, LowerNode(node.Expression, ctx), node.TypeName);

    public LoweredRange Lower(RangeSyntax node, CompilerContext ctx) =>
        new(
            node.Location,
            node.From is null ? null : LowerNode(node.From, ctx),
            node.To is null ? null : LowerNode(node.To, ctx),
            node.Exclusive);

    public LoweredTuple Lower(TupleLiteralSyntax node, CompilerContext ctx) =>
        new(node.Location, LowerNodeList(node.Elements, ctx));

    public LoweredArray Lower(ArrayLiteralSyntax node, CompilerContext ctx) =>
        new(node.Location, LowerNodeList(node.Elements, ctx));

    public LoweredComprehension Lower(ComprehensionSyntax node, CompilerContext ctx)
    {
        var simpleName = node.Pattern.NodeType == NodeType.NamePattern
            && !char.IsUpper(((NamePatternSyntax)node.Pattern).Name[0]);

        return new(
            node.Location,
            Lower(node.Pattern),
            LowerNode(node.Target, ctx),
            node.Guard is null ? null : LowerNode(node.Guard, ctx),
            node.Key is null ? null : LowerNode(node.Key, ctx),
            LowerNode(node.Value, ctx),
            node.IsDictionary,
            simpleName,
            NewLoopLabels());
    }

    public LoweredLiteral Lower(StringLiteralSyntax node) =>
        new(node.Location, node.Value, LoweredLiteralKind.String);

    public LoweredLiteral Lower(CharLiteralSyntax node) =>
        new(node.Location, node.Value, LoweredLiteralKind.Char);

    public LoweredLiteral Lower(FloatLiteralSyntax node) =>
        new(node.Location, node.Value, LoweredLiteralKind.Float);

    public LoweredLiteral Lower(IntegerLiteralSyntax node) =>
        new(node.Location, node.Value, LoweredLiteralKind.Integer);

    public LoweredLiteral Lower(BooleanLiteralSyntax node) =>
        new(node.Location, node.Value, LoweredLiteralKind.Boolean);

    public LoweredLiteral Lower(NilLiteralSyntax node) =>
        new(node.Location, null, LoweredLiteralKind.Nil);

    public LoweredAccess Lower(AccessSyntax node, CompilerContext ctx)
        => new(node.Location, LowerNode(node.Target, ctx), node.Name, LoweredAccessKind.Unresolved, node.SpecialName);

    public LoweredIndexer Lower(IndexerSyntax node, CompilerContext ctx)
    {
        var kind = node.Index.NodeType == NodeType.Range
            ? LoweredIndexerKind.Slice
            : LoweredIndexerKind.Unresolved;

        return new(node.Location, LowerNode(node.Target, ctx), LowerNode(node.Index, ctx), kind);
    }

    public LoweredStoreTarget LowerStoreTarget(SyntaxNode node, CompilerContext ctx)
    {
        return node.NodeType switch
        {
            NodeType.Name => new(node.Location, LowerNode(node, ctx), LoweredStoreKind.Name),
            NodeType.Access => LowerAccessStore((AccessSyntax)node, ctx),
            NodeType.Index => LowerIndexerStore((IndexerSyntax)node, ctx),
            _ => new(node.Location, LowerNode(node, ctx), LoweredStoreKind.Invalid)
        };
    }

    public LoweredIf Lower(IfSyntax node, CompilerContext ctx)
    {
        return new(
            node.Location,
            LowerNode(node.Condition, ctx),
            LowerNode(node.True, ctx),
            node.False is null ? null : LowerNode(node.False, ctx),
            RequiresElseResult: node.IsExpression && node.False is null,
            TrueBranchIsBindingGuard: node.True.NodeType == NodeType.Binding);
    }

    public LoweredBlock Lower(BlockSyntax node, CompilerContext ctx, bool noScope)
    {
        return new(
            node.Location,
            LowerBlockNodes(node.Nodes, ctx),
            HasAutoClose: hasAuto(node),
            NoScope: noScope);
    }

    public LoweredMatch Lower(MatchSyntax node, CompilerContext ctx)
    {
        var entries = new LoweredMatchEntry[node.Entries.Count];

        for (var i = 0; i < node.Entries.Count; i++)
        {
            var entry = node.Entries[i];
            entries[i] = new(
                entry.Location,
                Lower(entry.Pattern),
                entry.Guard is null ? null : LowerNode(entry.Guard, ctx),
                LowerNode(entry.Expression, ctx));
        }

        return new(
            node.Location,
            node.Expression is null ? null : LowerNode(node.Expression, ctx),
            entries,
            HasSubject: node.Expression is not null);
    }

    public LoweredPattern Lower(PatternSyntax node)
    {
        return node.NodeType switch
        {
            NodeType.NotPattern => new(node.Location, LoweredPatternKind.Not,
                [Lower(((NotPatternSyntax)node).Pattern)]),
            NodeType.NamePattern => new(node.Location, LoweredPatternKind.Name, [],
                Name: ((NamePatternSyntax)node).Name),
            NodeType.IntegerPattern => new(node.Location, LoweredPatternKind.Literal, [],
                Literal: ((IntegerPatternSyntax)node).Value),
            NodeType.StringPattern => new(node.Location, LoweredPatternKind.Literal, [],
                LiteralExpression: Lower(((StringPatternSyntax)node).Value)),
            NodeType.FloatPattern => new(node.Location, LoweredPatternKind.Literal, [],
                Literal: ((FloatPatternSyntax)node).Value),
            NodeType.CharPattern => new(node.Location, LoweredPatternKind.Literal, [],
                Literal: ((CharPatternSyntax)node).Value),
            NodeType.BooleanPattern => new(node.Location, LoweredPatternKind.Literal, [],
                Literal: ((BooleanPatternSyntax)node).Value),
            NodeType.TuplePattern => new(node.Location, LoweredPatternKind.Tuple,
                LowerPatternList(((TuplePatternSyntax)node).Elements), RequiresExactLength: true),
            NodeType.ArrayPattern => new(node.Location, LoweredPatternKind.Array,
                LowerPatternList(((ArrayPatternSyntax)node).Elements), RequiresExactLength: false),
            NodeType.NilPattern => new(node.Location, LoweredPatternKind.Nil, []),
            NodeType.RangePattern => LowerRange((RangePatternSyntax)node),
            NodeType.WildcardPattern => new(node.Location, LoweredPatternKind.Wildcard, []),
            NodeType.TypeTestPattern => new(node.Location, LoweredPatternKind.TypeTest, [],
                TypeName: ((TypeTestPatternSyntax)node).TypeName,
                AllowTypeCheck: ((TypeTestPatternSyntax)node).AllowTypeCheck),
            NodeType.AndPattern => LowerAnd((AndPatternSyntax)node),
            NodeType.OrPattern => LowerOr((OrPatternSyntax)node),
            NodeType.CtorPattern => LowerConstructor((ConstructorPatternSyntax)node),
            _ => throw new InvalidOperationException($"Unsupported pattern node in lowering: {node.NodeType}.")
        };
    }

    public LoweredTryCatch Lower(TryCatchSyntax node, CompilerContext ctx)
    {
        var kind = node.Finally is null
            ? LoweredTryKind.TryCatch
            : node.Catch is null
                ? LoweredTryKind.TryFinally
                : LoweredTryKind.TryCatchFinally;

        return new(
            node.Location,
            LowerNode(node.Expression, ctx),
            node.Catch is null ? null : LowerNode(node.Catch, ctx),
            node.BindVariable?.Value,
            node.BindVariable?.Location ?? node.Location,
            node.Finally is null ? null : LowerNode(node.Finally, ctx),
            kind,
            new(
                Gotcha: NewLabel(),
                Skip: NewLabel(),
                RunFinally: NewLabel(),
                RethrowFinally: NewLabel(),
                Exit: NewLabel()));
    }

    public LoweredWhile Lower(WhileSyntax node, CompilerContext ctx) =>
        new(node.Location, LowerNode(node.Condition, ctx), LowerNode(node.Body, ctx), node.DoWhile, NewLoopLabels());

    public LoweredFor Lower(ForSyntax node, CompilerContext ctx)
    {
        var simpleName = node.Pattern is NamePatternSyntax name && !char.IsUpper(name.Name[0]);
        return new(
            node.Location,
            Lower(node.Pattern),
            LowerNode(node.Target, ctx),
            node.Guard is null ? null : LowerNode(node.Guard, ctx),
            LowerNode(node.Body, ctx),
            node.Else is null ? null : LowerNode(node.Else, ctx),
            simpleName,
            NewLoopLabels());
    }

    public LoweredLabel Lower(LabelLiteralSyntax node, CompilerContext ctx) =>
        new(node.Location, node.Label, LowerNode(node.Expression, ctx), node.Mutable, node.FromString);

    private LoweredLoopLabels NewLoopLabels() =>
        new(
            Continue: NewLabel(),
            Exit: NewLabel(),
            BreakExit: NewLabel(),
            Iteration: NewLabel(),
            InitSkip: NewLabel(),
            ElseSkip: NewLabel());

    private LoweredLabelId NewLabel() => new(nextLabelId++);

    private LoweredStoreTarget LowerAccessStore(AccessSyntax node, CompilerContext ctx)
        => new(node.Location, LowerNode(node.Target, ctx), LoweredStoreKind.UnresolvedAccess, node.Name, node.SpecialName);

    private LoweredStoreTarget LowerIndexerStore(IndexerSyntax node, CompilerContext ctx)
        => new(node.Location, Lower(node, ctx), LoweredStoreKind.UnresolvedIndexer);

    public LoweredNode LowerNode(SyntaxNode node, CompilerContext ctx, bool needsValue = false)
    {
        return node.NodeType switch
        {
            NodeType.Access => Lower((AccessSyntax)node, ctx),
            NodeType.Application => Lower((ApplicationSyntax)node, ctx),
            NodeType.Array => Lower((ArrayLiteralSyntax)node, ctx),
            NodeType.As => Lower((AsSyntax)node, ctx),
            NodeType.Assignment => Lower((AssignmentSyntax)node, ctx),
            NodeType.Binary => Lower((BinaryOperationSyntax)node, ctx),
            NodeType.Binding => Lower((BindingSyntax)node, ctx),
            NodeType.Block => Lower((BlockSyntax)node, ctx, noScope: false),
            NodeType.Boolean => Lower((BooleanLiteralSyntax)node),
            NodeType.Break => Lower((BreakSyntax)node, ctx),
            NodeType.Char => Lower((CharLiteralSyntax)node),
            NodeType.Comprehension => Lower((ComprehensionSyntax)node, ctx),
            NodeType.ConstDeclaration => Lower((ConstDeclarationSyntax)node, ctx),
            NodeType.Continue => Lower((ContinueSyntax)node),
            NodeType.ExpressionStatement => Lower((ExpressionStatementSyntax)node, ctx),
            NodeType.Float => Lower((FloatLiteralSyntax)node),
            NodeType.For => Lower((ForSyntax)node, ctx),
            NodeType.Function => Lower((FunctionDeclarationSyntax)node, ctx, needsValue, iteratorBody: false),
            NodeType.Select => Lower((SelectDeclarationSyntax)node, ctx),
            NodeType.SelectInvocation => Lower((SelectInvocationSyntax)node),
            NodeType.If => Lower((IfSyntax)node, ctx),
            NodeType.Impl => Lower((ImplDeclarationSyntax)node, needsValue),
            NodeType.Index => Lower((IndexerSyntax)node, ctx),
            NodeType.Integer => Lower((IntegerLiteralSyntax)node),
            NodeType.Label => Lower((LabelLiteralSyntax)node, ctx),
            NodeType.Match => Lower((MatchSyntax)node, ctx),
            NodeType.Name => Lower((NameSyntax)node),
            NodeType.Nil => Lower((NilLiteralSyntax)node),
            NodeType.Range => Lower((RangeSyntax)node, ctx),
            NodeType.Rebinding => Lower((RebindingSyntax)node, ctx),
            NodeType.Return => Lower((ReturnSyntax)node, ctx),
            NodeType.Goto => Lower((GotoSyntax)node),
            NodeType.Exit => Lower((ExitSyntax)node, ctx),
            NodeType.String => Lower((StringLiteralSyntax)node),
            NodeType.Throw => Lower((ThrowSyntax)node, ctx),
            NodeType.TryCatch => Lower((TryCatchSyntax)node, ctx),
            NodeType.Tuple => Lower((TupleLiteralSyntax)node, ctx),
            NodeType.Type => Lower((TypeDeclarationSyntax)node, needsValue),
            NodeType.Unary => Lower((UnaryOperationSyntax)node, ctx),
            NodeType.While => Lower((WhileSyntax)node, ctx),
            NodeType.Yield => Lower((YieldSyntax)node, ctx),
            NodeType.YieldBlock => Lower((YieldBlockSyntax)node, ctx),
            NodeType.YieldBreak => Lower((YieldBreakSyntax)node),
            _ => throw new InvalidOperationException($"Unsupported AST node in lowering: {node.NodeType}.")
        };
    }

    public IReadOnlyList<LoweredNode> LowerNodeList(IReadOnlyList<SyntaxNode> nodes, CompilerContext ctx, bool keepLastResult)
    {
        var lowered = new LoweredNode[nodes.Count];

        for (var i = 0; i < nodes.Count; i++)
        {
            lowered[i] = LowerNode(nodes[i], ctx, keepLastResult && i == nodes.Count - 1);
        }

        return lowered;
    }

    private IReadOnlyList<LoweredNode> LowerNodeList(IReadOnlyList<SyntaxNode> nodes, CompilerContext ctx) =>
        LowerNodeList(nodes, ctx, keepLastResult: false);

    private IReadOnlyList<LoweredBinding> LowerBindingList(IReadOnlyList<BindingSyntax> nodes, CompilerContext ctx, bool isPrivate)
    {
        var lowered = new LoweredBinding[nodes.Count];

        for (var i = 0; i < nodes.Count; i++)
        {
            lowered[i] = Lower(nodes[i], ctx) with { IsPrivate = isPrivate || nodes[i].IsPrivate };
        }

        return lowered;
    }

    private IReadOnlyList<LoweredPattern> LowerPatternList(IReadOnlyList<SyntaxNode> nodes)
    {
        var patterns = new LoweredPattern[nodes.Count];

        for (var i = 0; i < nodes.Count; i++)
        {
            patterns[i] = Lower((PatternSyntax)nodes[i]);
        }

        return patterns;
    }

    private LoweredPattern LowerRange(RangePatternSyntax node) =>
        new(node.Location, LoweredPatternKind.Range, [Lower(node.From), Lower(node.To)]);

    private LoweredPattern LowerAnd(AndPatternSyntax node) =>
        new(node.Location, LoweredPatternKind.And, [Lower(node.Left), Lower(node.Right)]);

    private LoweredPattern LowerOr(OrPatternSyntax node) =>
        new(node.Location, LoweredPatternKind.Or, [Lower(node.Left), Lower(node.Right)]);

    private LoweredPattern LowerConstructor(ConstructorPatternSyntax node) =>
        new(node.Location, LoweredPatternKind.Constructor, LowerPatternList(node.Arguments),
            TypeName: node.TypeName, Constructor: node.Constructor, RequiresExactLength: true);

    private List<LoweredFunctionDeclaration> LowerFunctions(IReadOnlyList<FunctionDeclarationSyntax> functions)
    {
        var lowered = new List<LoweredFunctionDeclaration>(functions.Count);

        for (var i = 0; i < functions.Count; i++)
        {
            lowered.Add(Lower(functions[i], new CompilerContext(), needsValue: false, iteratorBody: false));
        }

        return lowered;
    }

    private List<LoweredField> LowerFields(IReadOnlyList<BindingSyntax> fields)
    {
        var lowered = new List<LoweredField>(fields.Count);

        for (var i = 0; i < fields.Count; i++)
        {
            lowered.Add(LowerField(fields[i], new CompilerContext()));
        }

        return lowered;
    }

    private LoweredField LowerField(BindingSyntax field, CompilerContext ctx) =>
        new(
            field.Location,
            (field.Pattern as NamePatternSyntax)?.Name,
            field.Constant,
            field.AutoClose,
            field.Init is null ? null : LowerNode(field.Init, ctx));

    private LoweredNode LowerFunctionBody(SyntaxNode body, CompilerContext ctx) =>
        body is BlockSyntax block
            ? Lower(block, ctx, noScope: false)
            : LowerNode(body, ctx, needsValue: true);

    private IReadOnlyList<LoweredNode> LowerBlockNodes(IReadOnlyList<SyntaxNode> nodes, CompilerContext ctx) =>
        LowerNodeList(nodes, ctx, keepLastResult: true);

    private IReadOnlyList<LoweredParameter> LowerParameters(IReadOnlyList<ParameterSyntax> parameters)
    {
        var lowered = new LoweredParameter[parameters.Count];

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            lowered[i] = new(
                parameter.Location,
                parameter.Name,
                LowerDefaultValue(parameter.DefaultValue),
                HasDefaultValue: parameter.DefaultValue is not null,
                parameter.DefaultValue?.Location ?? parameter.Location,
                parameter.TypeAnnotation,
                parameter.IsVarArgs,
                Mutable: parameter is TypeParameterSyntax { Mutable: true });
        }

        return lowered;
    }

    private static IReadOnlyList<Qualident> CopyQualidents(IReadOnlyList<Qualident>? names)
    {
        if (names is null || names.Count == 0)
        {
            return [];
        }

        var copy = new Qualident[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            copy[i] = names[i];
        }

        return copy;
    }

    private LoweredLiteral? LowerDefaultValue(SyntaxNode? node) => node switch
    {
        IntegerLiteralSyntax integer => Lower(integer),
        FloatLiteralSyntax @float => Lower(@float),
        CharLiteralSyntax character => Lower(character),
        BooleanLiteralSyntax boolean => Lower(boolean),
        StringLiteralSyntax @string => Lower(@string),
        NilLiteralSyntax nil => Lower(nil),
        _ => null
    };

    private bool IsStdCall(FunctionDeclarationSyntax node)
    {
        if (noOptimizations)
        {
            return false;
        }

        if (node.TargetTypeName is not null)
        {
            return false;
        }

        for (var i = 0; i < node.Parameters.Count; i++)
        {
            if (node.Parameters[i].DefaultValue is not null
                || node.Parameters[i].IsVarArgs)
            {
                return false;
            }
        }

        return true;
    }
}
