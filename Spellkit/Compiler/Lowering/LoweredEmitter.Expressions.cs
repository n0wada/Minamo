using Spellkit.Debug;
using Spellkit.Parser;
using Spellkit.Parser.Model;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System;
using System.Collections.Generic;

namespace Spellkit.Compiler.Lowering;

internal sealed partial class LoweredEmitter
{
    private void EmitExpressionStatement(LoweredExpressionStatement node, bool keepResult, CompilerContext ctx)
    {
        if (keepResult)
        {
            EmitValue(node.Expression, ctx);
        }
        else
        {
            EmitEffect(node.Expression, ctx);
        }
    }

    private void EmitConstDeclaration(LoweredConstDeclaration node, bool keepResult, CompilerContext ctx)
    {
        if (!target.IsGlobalScope)
        {
            target.AddError(CompilerError.ConstOnlyGlobalScope, node.Location);
            if (keepResult)
            {
                cw.LoadNil();
            }

            return;
        }

        foreach (var declaration in node.Declarations)
        {
            EmitEffect(declaration, ctx);
        }

        if (keepResult)
        {
            cw.LoadNil();
        }
    }

    private void EmitBinding(LoweredBinding node, bool keepResult, CompilerContext ctx)
    {
        if (node.Init is not null)
        {
            EmitValue(node.Init, ctx);
        }
        else
        {
            cw.LoadNil();
        }

        if (node.SimpleNameBinding)
        {
            target.AddLinePragma(node.Location);
            var flags = node.Constant ? VarFlags.Const : VarFlags.None;
            if (node.IsPrivate)
            {
                flags |= VarFlags.Private;
            }

            var name = node.Pattern.Name!;
            var address = target.AddVariable(name!, node.Location, flags);
            cw.StoreVariable(address);

            if (node.AutoClose)
            {
                target.AddAutoClose(address, name);
            }
        }
        else
        {
            if (node.Init is null)
            {
                target.AddError(CompilerError.BindingPatternNoInit, node.Location);
            }

            if (node.Init is not null)
            {
                target.CheckPattern(node.Pattern, node.InitNodeCount);
            }

            var flags = node.Constant ? VarFlags.Const : VarFlags.None;
            if (node.IsPrivate)
            {
                flags |= VarFlags.Private;
            }

            BuildPattern(node.Pattern, ctx, rebind: false, openMatch: true, flags);
            var skip = cw.DefineLabel();
            cw.JumpIfTrue(skip);
            target.ThrowError(SpellkitError.MatchFailed);
            cw.Throw();
            cw.MarkLabel(skip);
            cw.NoOperation();
        }

        if (keepResult)
        {
            cw.LoadNil();
        }
    }

    private void EmitRebinding(LoweredRebinding node, bool keepResult, CompilerContext ctx)
    {
        EmitValue(node.Init, ctx);
        BuildPattern(node.Pattern, ctx, rebind: true, openMatch: false);
        var skip = cw.DefineLabel();
        cw.JumpIfTrue(skip);
        target.ThrowError(SpellkitError.MatchFailed);
        cw.Throw();
        cw.MarkLabel(skip);
        cw.NoOperation();

        if (keepResult)
        {
            cw.LoadNil();
        }
    }

    private void EmitUnary(LoweredUnary node, bool keepResult, CompilerContext ctx)
    {
        EmitValue(node.Operand, ctx);
        target.AddLinePragma(node.Location);

        if (node.Operator == UnaryOperator.Neg)
        {
            cw.Negate();
        }
        else if (node.Operator == UnaryOperator.Plus)
        {
            cw.Plus();
        }
        else if (node.Operator == UnaryOperator.Not)
        {
            cw.Not();
        }
        if (!keepResult)
        {
            cw.Drop();
        }
    }

    private void EmitBinary(LoweredBinary node, bool keepResult, CompilerContext ctx)
    {
        switch (node.Operator)
        {
            case BinaryOperator.Coalesce:
                EmitOptionCoalesce(node, ctx);
                break;
            case BinaryOperator.And:
                EmitShortCircuit(node, ctx, branchOnTruth: false);
                break;
            case BinaryOperator.Or:
                EmitShortCircuit(node, ctx, branchOnTruth: true);
                break;
            case BinaryOperator.Is:
                {
                    var loweredPattern = node.Pattern!;
                    target.AddLinePragma(node.Location);
                    PreinitLoweredPattern(loweredPattern, rebind: false, openMatch: false);
                    EmitValue(node.Left, ctx, tailPosition: false);
                    BuildPattern(loweredPattern, ctx, rebind: false, openMatch: false);
                }
                break;
            case BinaryOperator.In:
                EmitValue(node.Right!, ctx, tailPosition: false);
                target.AddLinePragma(node.Location);
                cw.LoadMember(Builtins.In);
                cw.PrepareCall(1);
                EmitValue(node.Left, ctx, tailPosition: false);
                target.AddLinePragma(node.Location);
                cw.SetCallArgument(0);
                cw.InvokePreparedCall(1);
                break;
            case BinaryOperator.ShiftLeft:
            case BinaryOperator.ShiftRight:
                EmitValue(node.Left, ctx, tailPosition: false);
                EmitValue(node.Right!, ctx, tailPosition: false);
                target.AddLinePragma(node.Location);
                cw.CallMember(
                    node.Operator == BinaryOperator.ShiftLeft ? Builtins.ShiftLeft : Builtins.ShiftRight,
                    1);
                break;
            default:
                EmitValue(node.Left, ctx, tailPosition: false);
                EmitValue(node.Right!, ctx, tailPosition: false);
                target.AddLinePragma(node.Location);
                target.EmitBinaryOp(node.Operator);
                break;
        }

        if (!keepResult)
        {
            cw.Drop();
        }
    }

    private void EmitOptionCoalesce(LoweredBinary node, CompilerContext ctx)
    {
        var someLabel = cw.DefineLabel();
        var noneLabel = cw.DefineLabel();
        var fallbackLabel = cw.DefineLabel();
        var exitLabel = cw.DefineLabel();

        EmitValue(node.Left, ctx, tailPosition: false);

        cw.Duplicate();
        cw.CheckConstructor("Some");
        cw.JumpIfTrue(someLabel);

        cw.Duplicate();
        cw.CheckConstructor("None");
        cw.JumpIfTrue(noneLabel);

        cw.Duplicate();
        cw.JumpIfFalse(fallbackLabel);
        cw.Jump(exitLabel);

        cw.MarkLabel(someLabel);
        cw.Push(0);
        cw.LoadIndex();
        cw.Jump(exitLabel);

        cw.MarkLabel(noneLabel);
        cw.Drop();
        EmitValue(node.Right!, ctx);
        cw.Jump(exitLabel);

        cw.MarkLabel(fallbackLabel);
        cw.Drop();
        EmitValue(node.Right!, ctx);
        cw.MarkLabel(exitLabel);
        cw.NoOperation();
    }

    private void EmitShortCircuit(LoweredBinary node, CompilerContext ctx, bool branchOnTruth)
    {
        EmitValue(node.Left, ctx, tailPosition: false);
        var termLab = cw.DefineLabel();
        var exitLab = cw.DefineLabel();

        if (branchOnTruth)
        {
            cw.JumpIfTrue(termLab);
        }
        else
        {
            cw.JumpIfFalse(termLab);
        }

        EmitValue(node.Right!, ctx, tailPosition: false);
        target.AddLinePragma(node.Location);
        cw.Jump(exitLab);
        cw.MarkLabel(termLab);
        target.AddLinePragma(node.Location);
        cw.Push(branchOnTruth);
        cw.MarkLabel(exitLab);
        cw.NoOperation();
    }

    private void EmitName(LoweredName node, bool keepResult, CompilerContext ctx)
    {
        if (!keepResult)
        {
            target.AddLinePragma(node.Location);
            cw.NoOperation();
            return;
        }

        if (TryEmitBareEnumValue(node, keepResult, ctx))
        {
            return;
        }

        target.PushVariable(ctx, node.Value, node.Location);
    }

    private bool TryEmitBareEnumValue(LoweredName node, bool keepResult, CompilerContext ctx)
    {
        if (target.VariableExists(node.Value) is CompilerError.None)
        {
            return false;
        }

        var status = target.TryResolveBareEnumConstructor(node.Value, 0, out var resolution);

        if (status == LoweredBareEnumConstructorResolution.NotFound)
        {
            return false;
        }

        if (status == LoweredBareEnumConstructorResolution.Ambiguous)
        {
            target.AddError(CompilerError.AmbiguousEnumConstructor, node.Location, node.Value, resolution.TypeName.Local);
            if (keepResult)
            {
                cw.LoadNil();
            }

            return true;
        }

        target.PushTypeInfo(ctx, resolution.TypeName, node.Location);
        target.AddLinePragma(node.Location);
        cw.LoadMember(resolution.MemberName);

        if (resolution.CallRequired)
        {
            target.AddLinePragma(node.Location);
            cw.Call(0);
        }

        if (!keepResult)
        {
            cw.Drop();
        }

        return true;
    }

    private void EmitApplication(LoweredApplication node, bool keepResult, CompilerContext ctx)
    {
        var name = node.Target is LoweredName targetName ? targetName.Value : null;
        var symbol = ScopeVar.Empty;
        var error = CompilerError.None;

        if (name is not null)
        {
            error = target.GetVariable(name, out symbol);
        }

        if (TryEmitBareEnumApplication(node, name, error, keepResult, ctx)
            || TryEmitBuiltinOptionOrResult(node, name, error, keepResult, ctx)
            || TryEmitNameof(node, name, symbol, keepResult))
        {
            return;
        }

        if (IsStdCall(symbol, node))
        {
            for (var i = 0; i < node.Arguments.Count; i++)
            {
                EmitValue(node.Arguments[node.Arguments.Count - i - 1], ctx, tailPosition: false);
            }

            EmitValue(node.Target, ctx, tailPosition: false);
            target.AddLinePragma(node.Location);
            cw.TailCall(node.Arguments.Count);
        }
        else
        {
            EmitValue(node.Target, ctx, tailPosition: false);
            EmitApplicationArguments(node.Location, node.Arguments, ctx);
        }

        if (!keepResult)
        {
            cw.Drop();
        }
    }

    private bool TryEmitBareEnumApplication(LoweredApplication node, string? name, CompilerError error, bool keepResult, CompilerContext ctx)
    {
        if (name is null || error is CompilerError.None)
        {
            return false;
        }

        var status = target.TryResolveBareEnumConstructor(name, node.Arguments.Count, out var resolution);

        if (status == LoweredBareEnumConstructorResolution.NotFound)
        {
            return false;
        }

        if (status == LoweredBareEnumConstructorResolution.Ambiguous)
        {
            target.AddError(CompilerError.AmbiguousEnumConstructor, node.Location, name, resolution.TypeName.Local);
            if (keepResult)
            {
                cw.LoadNil();
            }

            return true;
        }

        target.PushTypeInfo(ctx, resolution.TypeName, node.Location);
        target.AddLinePragma(node.Location);
        cw.LoadMember(resolution.MemberName);

        if (resolution.CallRequired)
        {
            EmitApplicationArguments(node.Location, node.Arguments, ctx);
        }

        if (!keepResult)
        {
            cw.Drop();
        }

        return true;
    }

    private bool TryEmitBuiltinOptionOrResult(LoweredApplication node, string? name, CompilerError error, bool keepResult, CompilerContext ctx)
    {
        if (name is null || error is CompilerError.None)
        {
            return false;
        }

        var typeName = name switch
        {
            "Some" or "None" => "Option",
            "Ok" or "Err" => "Result",
            _ => null
        };

        if (typeName is null)
        {
            return false;
        }

        if (name == "None" && node.Arguments.Count != 0)
        {
            return false;
        }

        target.PushTypeInfo(ctx, new Qualident(typeName), node.Location);
        target.AddLinePragma(node.Location);
        cw.LoadMember(name);

        if (name != "None")
        {
            EmitApplicationArguments(node.Location, node.Arguments, ctx);
        }

        if (!keepResult)
        {
            cw.Drop();
        }

        return true;
    }

    private bool TryEmitNameof(LoweredApplication node, string? name, ScopeVar symbol, bool keepResult)
    {
        if (name is null || !symbol.IsEmpty() || name is not "nameof" || node.Arguments.Count != 1)
        {
            return false;
        }

        var value = GetExpressionName(node.Arguments[0]);
        target.AddLinePragma(node.Location);
        if (value is not null)
        {
            cw.Push(value);
        }

        if (!keepResult)
        {
            cw.Drop();
        }

        return true;
    }

    private bool IsStdCall(ScopeVar symbol, LoweredApplication app) =>
        (symbol.Data & VarFlags.StdCall) == VarFlags.StdCall
        && symbol.Args == app.Arguments.Count
        && !HasLabels(app.Arguments);

    private void EmitApplicationArguments(Location loc, IReadOnlyList<LoweredNode> arguments, CompilerContext ctx)
    {
        if (!HasLabels(arguments))
        {
            for (var i = 0; i < arguments.Count; i++)
            {
                EmitValue(arguments[i], ctx, tailPosition: false);
            }

            target.AddLinePragma(loc);
            cw.Call(arguments.Count);
            return;
        }

        target.AddLinePragma(loc);
        cw.PrepareCall(arguments.Count);
        Dictionary<string, object?>? dict = null;
        var keywordArgument = false;

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];

            if (argument is LoweredLabel label)
            {
                dict ??= new();
                keywordArgument = true;

                if (label.Mutable)
                {
                    target.AddError(CompilerError.InvalidFunctionArgument, label.Location);
                }

                if (dict.ContainsKey(label.Label))
                {
                    target.AddError(CompilerError.NamedArgumentMultipleTimes, label.Location, label.Label);
                }
                else
                {
                    dict.Add(label.Label, null);
                }

                EmitValue(label.Expression, ctx, tailPosition: false);
                cw.SetNamedCallArgument(label.Label);
            }
            else
            {
                EmitValue(argument, ctx, tailPosition: false);
                cw.SetCallArgument(i);

                if (keywordArgument)
                {
                    target.AddError(CompilerError.PositionalArgumentAfterKeyword, argument.Location);
                }
            }
        }

        target.AddLinePragma(loc);
        cw.InvokePreparedCall(arguments.Count);
    }

    private static bool HasLabels(IReadOnlyList<LoweredNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is LoweredLabel)
            {
                return true;
            }
        }

        return false;
    }

    private string? GetExpressionName(LoweredNode node)
    {
        switch (node)
        {
            case LoweredName name:
                return name.Value;
            case LoweredAccess access:
                return access.Name;
            case LoweredIndexer indexer:
                return GetExpressionName(indexer.Index);
            case LoweredLiteral { Kind: LoweredLiteralKind.String } literal:
                return (string?)literal.Value;
            default:
                target.AddError(CompilerError.ExpressionNoName, node.Location);
                return "";
        }
    }

    private static bool TryGetNodeName(LoweredNode node, out string name)
    {
        switch (node)
        {
            case LoweredName loweredName:
                name = loweredName.Value;
                return true;
            case LoweredAccess access:
                name = access.Name;
                return true;
            case LoweredLabel label:
                name = label.Label;
                return true;
            default:
                name = "";
                return false;
        }
    }


    private void EmitCast(LoweredCast node, bool keepResult, CompilerContext ctx)
    {
        target.PushTypeInfo(ctx, node.TypeName, node.Location);
        EmitValue(node.Expression, ctx);
        target.AddLinePragma(node.Location);
        cw.ApplyCast();
        if (!keepResult)
        {
            cw.Drop();
        }
    }

    private void EmitRange(LoweredRange node, bool keepResult, CompilerContext ctx)
    {
        cw.LoadType(SpellkitTypeCodes.Iterator);
        cw.LoadMember(Builtins.Range);
        cw.PrepareCall(4);

        if (node.From is not null)
        {
            EmitValue(node.From, ctx);
        }
        else
        {
            cw.Push(0);
        }

        cw.SetCallArgument(0);

        if (node.To is not null)
        {
            EmitValue(node.To, ctx);
        }
        else
        {
            cw.LoadNil();
        }

        cw.SetCallArgument(1);

        cw.Push(1);

        cw.SetCallArgument(2);
        cw.Push(node.Exclusive);
        cw.SetCallArgument(3);
        target.AddLinePragma(node.Location);
        cw.InvokePreparedCall(4);
        if (!keepResult)
        {
            cw.Drop();
        }
    }

    private void EmitTuple(LoweredTuple node, bool keepResult, CompilerContext ctx)
    {
        if (node.Elements.Count is 1 && node.Elements[0] is LoweredRange)
        {
            EmitValue(node.Elements[0], ctx);
            cw.LoadMember(Builtins.ToTuple);
            cw.PrepareCall(0);
            target.AddLinePragma(node.Location);
            cw.InvokePreparedCall(0);
        }
        else
        {
            EmitTupleElements(node.Elements, ctx);
            target.AddLinePragma(node.Location);
            cw.CreateTuple(node.Elements.Count);
        }

        if (!keepResult)
        {
            cw.Drop();
        }
    }

    private void EmitArray(LoweredArray node, bool keepResult, CompilerContext ctx)
    {
        if (node.Elements.Count is 1 && node.Elements[0] is LoweredRange)
        {
            EmitValue(node.Elements[0], ctx);
            cw.LoadMember(Builtins.ToArray);
            cw.PrepareCall(0);
            target.AddLinePragma(node.Location);
            cw.InvokePreparedCall(0);
        }
        else if (node.IsDictionary || (node.Elements.Count > 0 && node.Elements[0] is LoweredLabel))
        {
            EmitDictionary(node.Elements, ctx);
        }
        else
        {
            cw.LoadType(SpellkitTypeCodes.Array);
            cw.LoadMember(nameof(SpellkitTypeCodes.Array));
            cw.PrepareCall(node.Elements.Count);

            for (var i = 0; i < node.Elements.Count; i++)
            {
                EmitValue(node.Elements[i], ctx);
                cw.SetCallArgument(i);
            }

            target.AddLinePragma(node.Location);
            cw.InvokePreparedCall(node.Elements.Count);
        }

        if (!keepResult)
        {
            cw.Drop();
        }
    }

    private void EmitDictionary(IReadOnlyList<LoweredNode> elements, CompilerContext ctx)
    {
        var set = new HashSet<string>();

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[elements.Count - i - 1];

            if (element is LoweredLabel label)
            {
                EmitValue(label.Expression, ctx);
                cw.CreateLabel(label.Label);

                if (set.Contains(label.Label))
                {
                    target.AddError(CompilerError.DuplicateLabel, label.Location, label.Label);
                }
                else
                {
                    set.Add(label.Label);
                }

                if (label.Mutable)
                {
                    target.AddError(CompilerError.InvalidDictionary, element.Location);
                }
            }
            else
            {
                target.AddError(CompilerError.InvalidDictionary, element.Location);
            }
        }

        cw.CreateDictionary(elements.Count);
    }

    private void EmitTupleElements(IReadOnlyList<LoweredNode> elements, CompilerContext ctx)
    {
        var set = new HashSet<string>();

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];

            if (element is LoweredLabel label)
            {
                EmitValue(label.Expression, ctx);
                cw.CreateLabel(label.Label);

                if (char.IsUpper(label.Label[0]) && !label.FromString)
                {
                    target.AddError(CompilerError.LabelOnlyCamel, label.Location);
                }

                if (set.Contains(label.Label))
                {
                    target.AddError(CompilerError.DuplicateLabel, label.Location, label.Label);
                }
                else
                {
                    set.Add(label.Label);
                }

                if (label.Mutable)
                {
                    cw.MarkMutable();
                }
            }
            else
            {
                EmitValue(element, ctx);

                if (TryGetNodeName(element, out var name))
                {
                    cw.CreateLabel(name);
                }
            }
        }
    }

    private void EmitComprehension(LoweredComprehension node, bool keepResult, CompilerContext ctx)
    {
        var labels = Resolve(node.Labels);
        ctx = ctx.WithLoop(labels.Continue, labels.Exit, labels.BreakExit);

        target.StartScope(ScopeKind.Loop, node.Location);

        if (node.IsDictionary)
        {
            cw.LoadType(SpellkitTypeCodes.Dictionary);
            cw.LoadMember(nameof(SpellkitTypeCodes.Dictionary));
        }
        else
        {
            cw.LoadType(SpellkitTypeCodes.Array);
            cw.LoadMember(nameof(SpellkitTypeCodes.Array));
        }

        cw.PrepareCall(0);
        target.AddLinePragma(node.Location);
        cw.InvokePreparedCall(0);

        var result = target.AddVariable();
        cw.StoreVariable(result);

        var sys = target.AddVariable();
        EmitValue(node.Target, ctx);
        cw.JumpIfIteratorValue(labels.InitSkip);
        cw.LoadMember(Builtins.Iterate);
        cw.PrepareCall(0);
        cw.InvokePreparedCall(0);

        cw.MarkLabel(labels.InitSkip);
        cw.LoadIteratorFunction();
        cw.StoreVariable(sys);

        cw.MarkLabel(labels.Iteration);
        cw.LoadVariable(new ScopeVar(sys));
        cw.PrepareCall(0);
        cw.InvokePreparedCall(0);
        cw.JumpIfTerminator(ctx.BlockExit);

        if (node.CanUseSimpleNameBinding)
        {
            var address = target.AddVariable(node.Pattern.Name!, node.Pattern.Location, VarFlags.None);
            cw.StoreVariable(address);
        }
        else
        {
            BuildPattern(node.Pattern, ctx, rebind: false, openMatch: false);
            cw.JumpIfFalse(ctx.BlockSkip);
        }

        if (node.Guard is not null)
        {
            EmitValue(node.Guard, ctx);
            cw.JumpIfFalse(ctx.BlockSkip);
        }

        cw.LoadVariable(new ScopeVar(result));
        cw.LoadMember("Add");

        if (node.IsDictionary)
        {
            cw.PrepareCall(2);
            EmitValue(node.Key!, ctx);
            cw.SetCallArgument(0);
            EmitValue(node.Value, ctx);
            cw.SetCallArgument(1);
            target.AddLinePragma(node.Location);
            cw.InvokePreparedCall(2);
        }
        else
        {
            cw.PrepareCall(1);
            EmitValue(node.Value, ctx);
            cw.SetCallArgument(0);
            target.AddLinePragma(node.Location);
            cw.InvokePreparedCall(1);
        }

        cw.Drop();

        cw.MarkLabel(ctx.BlockSkip);
        cw.Jump(labels.Iteration);

        cw.MarkLabel(ctx.BlockExit);
        cw.Drop();
        cw.LoadVariable(new ScopeVar(result));
        target.AddLinePragma(node.Location);

        cw.MarkLabel(ctx.BlockBreakExit);
        if (!keepResult)
        {
            cw.Drop();
        }

        cw.NoOperation();
        target.EndScope();
    }

    private void EmitLiteral(LoweredLiteral node, bool keepResult)
    {
        if (!keepResult)
        {
            target.AddLinePragma(node.Location);
            cw.NoOperation();
            return;
        }

        target.AddLinePragma(node.Location);

        switch (node.Kind)
        {
            case LoweredLiteralKind.String:
                cw.Push((string)node.Value!);
                break;
            case LoweredLiteralKind.Char:
                cw.Push((char)node.Value!);
                break;
            case LoweredLiteralKind.Float:
                cw.Push((double)node.Value!);
                break;
            case LoweredLiteralKind.Integer:
                cw.Push((long)node.Value!);
                break;
            case LoweredLiteralKind.Boolean:
                cw.Push((bool)node.Value!);
                break;
            case LoweredLiteralKind.Nil:
                cw.LoadNil();
                break;
        }
    }

    private void EmitLabel(LoweredLabel node, bool keepResult, CompilerContext ctx)
    {
        EmitValue(node.Expression, ctx);
        cw.CreateLabel(node.Label);

        if (node.Mutable)
        {
            cw.MarkMutable();
        }

        if (!keepResult)
        {
            cw.Drop();
        }
    }

}
