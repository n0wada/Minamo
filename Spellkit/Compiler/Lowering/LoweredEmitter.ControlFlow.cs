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
    private void EmitSelectInvocation(LoweredSelectInvocation node, bool keepResult, CompilerContext ctx)
    {
        EmitValue(node.Target, ctx);
        cw.SuspendSelect();
        if (keepResult)
        {
            cw.LoadNil();
        }
    }

    private void EmitControlTransfer(LoweredControlTransfer node, bool keepResult, CompilerContext ctx)
    {
        switch (node.Kind)
        {
            case LoweredControlTransferKind.Break:
                EmitBreak(node, ctx);
                break;
            case LoweredControlTransferKind.Continue:
                EmitContinue(node, keepResult, ctx);
                break;
            case LoweredControlTransferKind.Return:
                EmitReturn(node, ctx);
                break;
            case LoweredControlTransferKind.Goto:
                EmitGoto(node, ctx);
                break;
            case LoweredControlTransferKind.Throw:
                EmitThrow(node, ctx);
                break;
            case LoweredControlTransferKind.Yield:
                EmitYield(node, keepResult, ctx);
                break;
            case LoweredControlTransferKind.YieldBreak:
                EmitYieldBreak(node, keepResult, ctx);
                break;
        }
    }

    private void EmitBreak(LoweredControlTransfer node, CompilerContext ctx)
    {
        if (!ctx.HasLoop)
        {
            target.AddError(CompilerError.NoEnclosingLoop, node.Location);
        }

        if (node.Expression is not null)
        {
            EmitValue(node.Expression, ctx);
        }
        else
        {
            cw.LoadNil();
        }

        target.CallAutosForKind(ScopeKind.Loop);
        target.AddLinePragma(node.Location);
        cw.Jump(ctx.BlockBreakExit);
    }

    private void EmitContinue(LoweredControlTransfer node, bool keepResult, CompilerContext ctx)
    {
        if (!ctx.HasLoopContinue)
        {
            target.AddError(CompilerError.NoEnclosingLoop, node.Location);
        }

        target.CallAutosForKind(ScopeKind.Loop);
        target.AddLinePragma(node.Location);
        cw.Jump(ctx.BlockSkip);
        if (keepResult)
        {
            cw.LoadNil();
        }
    }

    private void EmitReturn(LoweredControlTransfer node, CompilerContext ctx)
    {
        if (!ctx.HasFunctionExit)
        {
            target.AddError(CompilerError.ReturnNotAllowed, node.Location);
        }

        if (ctx.IsIteratorBody)
        {
            target.AddError(CompilerError.ReturnInIterator, node.Location);
        }

        if (node.Expression is not null)
        {
            EmitValue(node.Expression, ctx);
        }
        else
        {
            cw.LoadNil();
        }

        target.CallAutosForKind(ScopeKind.Function);
        target.AddLinePragma(node.Location);
        cw.Jump(ctx.FunctionExit);
    }

    private void EmitGoto(LoweredControlTransfer node, CompilerContext ctx)
    {
        if (node.SelectState is not null
            && (ctx.SelectStates is null || !ctx.SelectStates.Contains(node.SelectState)))
        {
            target.AddError(
                CompilerError.SelectStateNotFound,
                node.Location,
                node.SelectState,
                ctx.SelectName ?? string.Empty);
        }

        EmitReturn(node, ctx);
    }

    private void EmitThrow(LoweredControlTransfer node, CompilerContext ctx)
    {
        if (node.Expression is not null)
        {
            EmitValue(node.Expression, ctx);
            target.AddLinePragma(node.Location);
            cw.Throw();
            return;
        }

        if (ctx.Errors.Count is 0)
        {
            target.AddError(CompilerError.InvalidRethrow, node.Location);
        }
        else
        {
            cw.LoadVariable(new ScopeVar(ctx.Errors.Peek()));
        }

        target.AddLinePragma(node.Location);
        cw.Throw();
    }

    private void EmitYield(LoweredControlTransfer node, bool keepResult, CompilerContext ctx)
    {
        if (!ctx.HasFunctionExit)
        {
            target.AddError(CompilerError.YieldNotAllowed, node.Location);
        }

        EmitValue(node.Expression!, ctx);
        target.AddLinePragma(node.Location);
        cw.Yield();
        if (keepResult)
        {
            cw.LoadNil();
        }
    }

    private void EmitYieldBreak(LoweredControlTransfer node, bool keepResult, CompilerContext ctx)
    {
        if (!ctx.HasFunctionExit)
        {
            target.AddError(CompilerError.YieldNotAllowed, node.Location);
        }

        target.AddLinePragma(node.Location);
        cw.EndIterator();
        cw.Jump(ctx.FunctionExit);
    }

    private void EmitYieldBlock(LoweredYieldBlock node, bool keepResult, CompilerContext ctx)
    {
        if (node.Elements.Count is 0)
        {
            if (keepResult)
            {
                cw.LoadNil();
            }

            return;
        }

        for (var i = 0; i < node.Elements.Count; i++)
        {
            var element = node.Elements[i];
            var last = i == node.Elements.Count - 1;
            EmitValue(element, ctx);
            target.AddLinePragma(node.Location);
            cw.Yield();

            if (last && keepResult)
            {
                cw.LoadNil();
            }
        }
    }


    private void EmitIf(LoweredIf node, bool keepResult, CompilerContext ctx)
    {
        if (node.TrueBranchIsBindingGuard)
        {
            target.AddError(CompilerError.GuardOnBinding, node.Location);
        }

        if (node.RequiresElseResult)
        {
            target.AddError(CompilerError.IfExpressionRequiresElse, node.Location);
        }

        var falseLabel = cw.DefineLabel();
        var skipLabel = cw.DefineLabel();

        target.StartScope(ScopeKind.Lexical, node.Location);
        EmitValue(node.Condition, ctx, tailPosition: false);
        target.AddLinePragma(node.Location);
        cw.JumpIfFalse(falseLabel);
        EmitNode(node.TrueBranch, keepResult, ctx);
        target.AddLinePragma(node.Location);
        cw.Jump(skipLabel);
        cw.MarkLabel(falseLabel);

        if (node.FalseBranch != null)
        {
            EmitNode(node.FalseBranch, keepResult, ctx);
        }
        else if (keepResult)
        {
            cw.LoadNil();
        }

        cw.MarkLabel(skipLabel);
        cw.NoOperation();
        target.EndScope();
    }

    private void EmitBlock(LoweredBlock block, bool keepResult, CompilerContext ctx)
    {
        if (block.Nodes.Count is 0)
        {
            if (keepResult)
            {
                cw.LoadNil();
            }

            return;
        }

        var nodes = block.Nodes;
        Label gotcha = default;
        var hasAuto = false;

        if (!block.NoScope)
        {
            hasAuto = block.HasAutoClose;
            if (hasAuto)
            {
                gotcha = cw.DefineLabel();
                cw.EnterTry(gotcha);
            }

            target.StartScope(ScopeKind.Lexical, block.Location);
        }
        else if (block.HasAutoClose)
        {
            target.AddError(CompilerError.AutoNotAllowed, block.Location);
        }

        target.PreInitLocalFunctions(block.Nodes);

        for (var i = 0; i < nodes.Count; i++)
        {
            var lowered = nodes[i];
            var last = i == nodes.Count - 1;
            var needsValue = keepResult && last;

            if (needsValue)
            {
                EmitValue(lowered, ctx, tailPosition: ctx.IsTailPosition);
            }
            else
            {
                EmitEffect(lowered, ctx, tailPosition: false);
            }
        }

        if (hasAuto)
        {
            var skip = cw.DefineLabel();
            var exit = cw.DefineLabel();
            cw.Jump(skip);
            cw.MarkLabel(gotcha);
            target.CallAutos(cls: false);
            cw.Throw();
            cw.Jump(exit);
            cw.MarkLabel(skip);
            cw.LeaveTry();
            target.CallAutos(cls: true);
            cw.MarkLabel(exit);
            cw.NoOperation();
        }

        if (!block.NoScope)
        {
            target.EndScope();
        }
    }

    private void EmitTryCatch(LoweredTryCatch node, bool keepResult, CompilerContext ctx)
    {
        if (node.FinallyBody is not null)
        {
            EmitTryCatchFinally(node, keepResult, ctx);
            return;
        }

        var labels = Resolve(node.Labels);
        cw.EnterTry(labels.Gotcha);
        EmitNode(node.TryBody, keepResult, ctx);

        target.AddLinePragma(node.Location);
        cw.LeaveTry();
        cw.Jump(labels.Skip);
        cw.MarkLabel(labels.Gotcha);

        target.StartScope(ScopeKind.Lexical, node.CatchBody!.Location);
        cw.Duplicate();
        var a = target.AddVariable();
        cw.StoreVariable(a);
        ctx.Errors.Push(a);

        if (node.BindVariable is not null)
        {
            target.AddLinePragma(node.BindVariableLocation);

            if (node.BindVariable is not "_")
            {
                var sv = target.AddVariable(node.BindVariable, node.CatchBody.Location, VarFlags.Const);
                cw.StoreVariable(sv);
            }
            else
            {
                cw.Drop();
            }
        }
        else
        {
            cw.Drop();
        }

        EmitNode(node.CatchBody!, keepResult, ctx);

        ctx.Errors.Pop();
        target.EndScope();

        cw.MarkLabel(labels.Skip);
        target.AddLinePragma(node.Location);
        cw.NoOperation();
    }

    private void EmitTryCatchFinally(LoweredTryCatch node, bool keepResult, CompilerContext ctx)
    {
        var labels = Resolve(node.Labels);
        var resultVar = keepResult ? target.AddVariable() : -1;
        var errorVar = node.CatchBody is null ? target.AddVariable() : -1;

        cw.EnterTry(labels.Gotcha);
        EmitNode(node.TryBody, keepResult, ctx);

        if (keepResult)
        {
            cw.StoreVariable(resultVar);
        }

        target.AddLinePragma(node.Location);
        cw.LeaveTry();
        cw.Jump(labels.RunFinally);
        cw.MarkLabel(labels.Gotcha);

        if (node.CatchBody is not null)
        {
            target.StartScope(ScopeKind.Lexical, node.CatchBody.Location);
            cw.Duplicate();
            var a = target.AddVariable();
            cw.StoreVariable(a);
            ctx.Errors.Push(a);

            if (node.BindVariable is not null)
            {
                target.AddLinePragma(node.BindVariableLocation);

                if (node.BindVariable is not "_")
                {
                    var sv = target.AddVariable(node.BindVariable, node.CatchBody.Location, VarFlags.Const);
                    cw.StoreVariable(sv);
                }
                else
                {
                    cw.Drop();
                }
            }
            else
            {
                cw.Drop();
            }

            EmitNode(node.CatchBody, keepResult, ctx);

            if (keepResult)
            {
                cw.StoreVariable(resultVar);
            }

            ctx.Errors.Pop();
            target.EndScope();
            cw.Jump(labels.RunFinally);
        }
        else
        {
            cw.StoreVariable(errorVar);
            cw.Jump(labels.RethrowFinally);
        }

        cw.MarkLabel(labels.RunFinally);
        EmitEffect(node.FinallyBody!, ctx);

        if (keepResult)
        {
            cw.LoadVariable(new ScopeVar(resultVar));
        }

        cw.Jump(labels.Exit);

        cw.MarkLabel(labels.RethrowFinally);
        EmitEffect(node.FinallyBody!, ctx);
        cw.LoadVariable(new ScopeVar(errorVar));
        cw.Throw();

        cw.MarkLabel(labels.Exit);
        target.AddLinePragma(node.Location);
        cw.NoOperation();
    }

    private void EmitWhile(LoweredWhile node, bool keepResult, CompilerContext ctx)
    {
        var labels = Resolve(node.Labels);
        ctx = ctx.WithLoop(
            blockSkip: labels.Continue,
            blockExit: labels.Exit,
            blockBreakExit: labels.BreakExit);
        target.StartScope(ScopeKind.Loop, node.Location);

        if (node.IsDoWhile)
        {
            EmitEffect(node.Body, ctx);
        }

        cw.MarkLabel(labels.Iteration);
        EmitValue(node.Condition, ctx);
        cw.JumpIfFalse(ctx.BlockExit);

        EmitEffect(node.Body, ctx);

        cw.MarkLabel(ctx.BlockSkip);
        cw.Jump(labels.Iteration);

        cw.MarkLabel(ctx.BlockExit);
        cw.LoadNil();
        target.AddLinePragma(node.Location);

        cw.MarkLabel(ctx.BlockBreakExit);
        if (!keepResult)
        {
            cw.Drop();
        }

        cw.NoOperation();
        target.EndScope();
    }

    private void EmitFor(LoweredFor node, bool keepResult, CompilerContext ctx)
    {
        var labels = Resolve(node.Labels);
        ctx = ctx.WithLoop(
            blockSkip: labels.Continue,
            blockExit: labels.Exit,
            blockBreakExit: labels.BreakExit);

        if (TryOptimizeFor(node, keepResult, ctx))
        {
            return;
        }

        target.StartScope(ScopeKind.Loop, node.Location);
        var inc = node.CanUseSimpleNameBinding;

        var sys = target.AddVariable();
        var entered = -1;

        if (node.Else is not null)
        {
            entered = target.AddVariable();
            cw.Push(false);
            cw.StoreVariable(entered);
        }

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

        if (inc)
        {
            var ai = target.AddVariable(node.Pattern.Name!, node.Pattern.Location, VarFlags.None);
            cw.StoreVariable(ai);
        }
        else
        {
            BuildPattern(node.Pattern, ctx, rebind: false, openMatch: false);
            cw.JumpIfFalse(ctx.BlockSkip);
        }

        if (node.Guard != null)
        {
            EmitValue(node.Guard, ctx);
            cw.JumpIfFalse(ctx.BlockSkip);
        }

        if (entered >= 0)
        {
            cw.Push(true);
            cw.StoreVariable(entered);
        }

        EmitEffect(node.Body, ctx);

        cw.MarkLabel(ctx.BlockSkip);
        cw.Jump(labels.Iteration);

        cw.MarkLabel(ctx.BlockExit);
        cw.Drop();

        if (node.Else is not null)
        {
            cw.LoadVariable(new ScopeVar(entered));
            cw.JumpIfTrue(labels.ElseSkip);
            EmitEffect(node.Else, ctx);
            cw.MarkLabel(labels.ElseSkip);
            cw.NoOperation();
        }

        cw.LoadNil();
        target.AddLinePragma(node.Location);

        cw.MarkLabel(ctx.BlockBreakExit);
        if (!keepResult)
        {
            cw.Drop();
        }

        cw.NoOperation();
        target.EndScope();
    }

    private bool TryOptimizeFor(LoweredFor node, bool keepResult, CompilerContext ctx)
    {
        if (target.NoOptimizations
            || node.Else is not null
            || node.Guard is not null
            || node.Pattern.Kind is not LoweredPatternKind.Name and not LoweredPatternKind.Wildcard
            || node.Target is not LoweredRange range)
        {
            return false;
        }

        var incName = node.Pattern.Name;

        if (incName is not null && incName.Length > 0 && char.IsUpper(incName[0]))
        {
            return false;
        }

        if (range.From is null)
        {
            return false;
        }

        const long step = 1L;

        int to = -1;

        if (range.To is not null)
        {
            EmitValue(range.To, ctx);
            to = target.AddVariable();
            cw.StoreVariable(to);
        }

        target.StartScope(ScopeKind.Loop, node.Location);
        var inc = incName is null ? target.AddVariable() : target.AddVariable(incName, node.Pattern.Location, VarFlags.None);

        EmitValue(range.From, ctx);
        var labels = Resolve(node.Labels);
        cw.Jump(labels.InitSkip);
        cw.MarkLabel(labels.Iteration);
        cw.LoadVariable(new ScopeVar(inc));
        cw.Push(step);
        cw.Add();

        cw.MarkLabel(labels.InitSkip);

        if (to != -1)
        {
            cw.Duplicate();
        }

        cw.StoreVariable(inc);

        if (to != -1)
        {
            cw.LoadVariable(new ScopeVar(to));

            if (range.Exclusive)
            {
                cw.LessThan();
            }
            else
            {
                cw.LessThanOrEqual();
            }

            cw.JumpIfFalse(ctx.BlockExit);
        }

        EmitEffect(node.Body, ctx, tailPosition: false);
        cw.MarkLabel(ctx.BlockSkip);
        cw.Jump(labels.Iteration);

        target.AddLinePragma(node.Location);
        cw.MarkLabel(ctx.BlockExit);
        cw.LoadNil();
        cw.MarkLabel(ctx.BlockBreakExit);
        if (!keepResult)
        {
            cw.Drop();
        }

        cw.NoOperation();
        target.EndScope();
        return true;
    }


    private Label Resolve(LoweredLabelId label)
    {
        if (labels.TryGetValue(label.Value, out var resolved))
        {
            return resolved;
        }

        resolved = cw.DefineLabel();
        labels.Add(label.Value, resolved);
        return resolved;
    }

    private (
        Label Gotcha,
        Label Skip,
        Label RunFinally,
        Label RethrowFinally,
        Label Exit) Resolve(LoweredTryLabels source) =>
        (
            Resolve(source.Gotcha),
            Resolve(source.Skip),
            Resolve(source.RunFinally),
            Resolve(source.RethrowFinally),
            Resolve(source.Exit));

    private (
        Label Continue,
        Label Exit,
        Label BreakExit,
        Label Iteration,
        Label InitSkip,
        Label ElseSkip) Resolve(LoweredLoopLabels source) =>
        (
            Resolve(source.Continue),
            Resolve(source.Exit),
            Resolve(source.BreakExit),
            Resolve(source.Iteration),
            Resolve(source.InitSkip),
            Resolve(source.ElseSkip));
}
