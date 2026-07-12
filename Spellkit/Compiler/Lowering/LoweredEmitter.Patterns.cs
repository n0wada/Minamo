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
    private void EmitMatch(LoweredMatch node, bool keepResult, CompilerContext ctx)
    {
        target.StartScope(ScopeKind.Lexical, node.Location);

        ctx = ctx.WithMatchExit(cw.DefineLabel());

        var sys = target.AddVariable();

        if (node.Subject != null)
        {
            EmitValue(node.Subject, ctx, tailPosition: false);
        }

        cw.StoreVariable(sys);
        var sysVar = new ScopeVar(sys);

        foreach (var e in node.Entries)
        {
            BuildEntry(e, sysVar, ctx);
        }

        if (node.HasSubject)
        {
            target.ThrowError(SpkError.MatchFailed);
            cw.Throw();
        }
        else
        {
            cw.LoadVariable(sysVar);
            cw.Throw();
        }

        cw.MarkLabel(ctx.MatchExit);
        cw.NoOperation();
        if (!keepResult)
        {
            cw.Drop();
        }

        target.EndScope();
    }

    private void BuildEntry(LoweredMatchEntry node, ScopeVar sys, CompilerContext ctx)
    {
        target.StartScope(ScopeKind.Lexical, node.Location);
        var skip = cw.DefineLabel();

        cw.LoadVariable(sys);
        BuildPattern(node.Pattern, ctx, rebind: false, openMatch: false);
        cw.JumpIfFalse(skip);

        if (node.Guard != null)
        {
            EmitValue(node.Guard, ctx, tailPosition: false);
            cw.JumpIfFalse(skip);
        }

        EmitValue(node.Expression, ctx);
        cw.Jump(ctx.MatchExit);
        cw.MarkLabel(skip);
        cw.NoOperation();
        target.EndScope();
    }

    private void BuildPattern(
        LoweredPattern pattern,
        CompilerContext ctx,
        bool rebind,
        bool openMatch,
        int flags = VarFlags.None)
    {
        switch (pattern.Kind)
        {
            case LoweredPatternKind.Not:
                PreinitLoweredPattern(pattern.Children[0], rebind, openMatch, flags);
                BuildPattern(pattern.Children[0], ctx, rebind, openMatch, flags);
                cw.Not();
                break;
            case LoweredPatternKind.Name:
                BuildName(pattern, ctx, rebind, openMatch, flags);
                break;
            case LoweredPatternKind.Literal:
                BuildLiteralPattern(pattern, ctx);
                break;
            case LoweredPatternKind.Tuple:
            case LoweredPatternKind.Array:
                BuildSequence(pattern, ctx, rebind, openMatch, flags);
                break;
            case LoweredPatternKind.Nil:
                cw.LoadNil();
                cw.Equal();
                break;
            case LoweredPatternKind.Range:
                BuildRange(ctx, pattern);
                break;
            case LoweredPatternKind.Wildcard:
                cw.Drop();
                cw.Push(true);
                break;
            case LoweredPatternKind.TypeTest:
                if (TryBuildZeroArgConstructorPattern(pattern.TypeName!, pattern.Location, ctx))
                {
                    break;
                }

                if (!pattern.AllowTypeCheck)
                {
                    target.AddError(CompilerError.TypePatternOnlyInIs, pattern.Location);
                    cw.Drop();
                    cw.Push(false);
                    break;
                }

                target.PushTypeInfo(ctx, pattern.TypeName!, pattern.Location);
                cw.CheckType();
                break;
            case LoweredPatternKind.And:
                BuildAnd(pattern, ctx, rebind, openMatch, flags);
                break;
            case LoweredPatternKind.Or:
                BuildOr(pattern, ctx, rebind, openMatch, flags);
                break;
            case LoweredPatternKind.Constructor:
                BuildCtor(pattern, ctx, rebind, openMatch, flags);
                break;
        }
    }

    private void BuildLiteralPattern(LoweredPattern node, CompilerContext ctx)
    {
        if (node.LiteralExpression is not null)
        {
            EmitValue(node.LiteralExpression, ctx);
        }
        else
        {
            switch (node.Literal)
            {
                case long value:
                    cw.Push(value);
                    break;
                case double value:
                    cw.Push(value);
                    break;
                case char value:
                    cw.Push(value);
                    break;
                case bool value:
                    cw.Push(value);
                    break;
            }
        }

        cw.Equal();
    }

    private void BuildCtor(
        LoweredPattern pattern,
        CompilerContext ctx,
        bool rebind,
        bool openMatch,
        int flags)
    {
        var bad = cw.DefineLabel();
        var ok = cw.DefineLabel();

        if (pattern.TypeName is not null)
        {
            cw.Duplicate();
            target.PushTypeInfo(ctx, pattern.TypeName, pattern.Location);
            cw.CheckType();
            cw.JumpIfFalse(bad);
        }

        cw.Duplicate();
        cw.CheckConstructor(pattern.Constructor!);
        cw.JumpIfFalse(bad);

        if (pattern.Children.Count is 0)
        {
            cw.Drop();
            cw.Push(true);
        }
        else
        {
            BuildSequence(pattern, ctx, rebind, openMatch, flags);
        }

        cw.Jump(ok);

        cw.MarkLabel(bad);
        target.AddLinePragma(pattern.Location);
        cw.Drop();
        cw.Push(false);

        cw.MarkLabel(ok);
    }

    private void BuildName(LoweredPattern node, CompilerContext ctx, bool rebind, bool openMatch, int flags)
    {
        var name = node.Name!;

        if (rebind && target.VariableExists(name) is CompilerError.None)
        {
            target.PopVariable(ctx, name, node.Location);
        }
        else if (!openMatch && target.TryGetLocalVariable(name, out var sv))
        {
            cw.StoreVariable(sv.Address);
        }
        else
        {
            var sva = target.AddVariable(name, node.Location, flags);
            cw.StoreVariable(sva);
        }

        target.AddLinePragma(node.Location);
        cw.Push(true);
    }

    private void BuildAnd(
        LoweredPattern node,
        CompilerContext ctx,
        bool rebind,
        bool openMatch,
        int flags)
    {
        cw.Duplicate();
        BuildPattern(node.Children[0], ctx, rebind, openMatch, flags);
        var termLab = cw.DefineLabel();
        var exitLab = cw.DefineLabel();
        cw.JumpIfFalse(termLab);
        BuildPattern(node.Children[1], ctx, rebind, openMatch, flags);
        target.AddLinePragma(node.Location);
        cw.Jump(exitLab);
        cw.MarkLabel(termLab);
        cw.Drop();
        target.AddLinePragma(node.Location);
        cw.Push(false);
        cw.MarkLabel(exitLab);
        cw.NoOperation();
    }

    private void BuildOr(
        LoweredPattern node,
        CompilerContext ctx,
        bool rebind,
        bool openMatch,
        int flags)
    {
        PreinitOr(node, rebind, openMatch, flags);

        cw.Duplicate();
        BuildPattern(node.Children[0], ctx, rebind, openMatch, flags);
        var termLab = cw.DefineLabel();
        var exitLab = cw.DefineLabel();
        cw.JumpIfTrue(termLab);
        BuildPattern(node.Children[1], ctx, rebind, openMatch, flags);
        target.AddLinePragma(node.Location);
        cw.Jump(exitLab);
        cw.MarkLabel(termLab);
        cw.Drop();
        target.AddLinePragma(node.Location);
        cw.Push(true);
        cw.MarkLabel(exitLab);
        cw.NoOperation();
    }

    private void BuildRange(CompilerContext ctx, LoweredPattern node)
    {
        var skip = cw.DefineLabel();
        var exit = cw.DefineLabel();

        cw.Duplicate();
        cw.HasMember(Builtins.Lt);
        cw.JumpIfFalse(skip);

        cw.Duplicate();
        cw.HasMember(Builtins.Gt);
        cw.JumpIfFalse(skip);

        cw.Duplicate();
        BuildRangeElement(ctx, node.Children[0]);
        cw.GreaterThanOrEqual();
        cw.JumpIfFalse(skip);

        cw.Duplicate();
        BuildRangeElement(ctx, node.Children[1]);
        cw.LessThanOrEqual();
        cw.JumpIfFalse(skip);

        cw.Push(true);
        cw.Drop();
        cw.Jump(exit);

        cw.MarkLabel(skip);
        cw.Drop();
        cw.Push(false);

        cw.MarkLabel(exit);
        cw.NoOperation();
    }

    private void BuildRangeElement(CompilerContext ctx, LoweredPattern node)
    {
        if (node.LiteralExpression is not null)
        {
            EmitValue(node.LiteralExpression, ctx);
            return;
        }

        switch (node.Literal)
        {
            case long value:
                cw.Push(value);
                break;
            case double value:
                cw.Push(value);
                break;
            case bool value:
                cw.Push(value);
                break;
            case char value:
                cw.Push(value);
                break;
            case null when node.Kind == LoweredPatternKind.Nil:
                cw.LoadNil();
                break;
            default:
                target.AddError(CompilerError.PatternNotSupported, node.Location, node);
                break;
        }
    }

    private bool TryBuildZeroArgConstructorPattern(Qualident typeNameInput, Location location, CompilerContext ctx)
    {
        if (!target.TryResolveZeroArgConstructor(typeNameInput, out var typeName, out var constructorName))
        {
            return false;
        }

        cw.Duplicate();
        target.PushTypeInfo(ctx, typeName, location);
        cw.CheckType();

        var bad = cw.DefineLabel();
        var ok = cw.DefineLabel();
        cw.JumpIfFalse(bad);

        cw.Duplicate();
        cw.CheckConstructor(constructorName);
        cw.JumpIfFalse(bad);
        cw.Drop();
        cw.Push(true);
        cw.Jump(ok);

        cw.MarkLabel(bad);
        cw.Drop();
        cw.Push(false);
        cw.MarkLabel(ok);
        cw.NoOperation();
        return true;
    }

    private void BuildSequence(
        LoweredPattern node,
        CompilerContext ctx,
        bool rebind,
        bool openMatch,
        int flags)
    {
        const bool onlyLabels = false;
        var elements = node.Children;

        var skip = cw.DefineLabel();
        var ok = cw.DefineLabel();

        if (!onlyLabels)
        {
            cw.Duplicate();
            cw.HasMember(Builtins.Length);
            cw.JumpIfFalse(skip);
        }

        cw.Duplicate();
        cw.HasMember(Builtins.Get);
        cw.JumpIfFalse(skip);

        if (!onlyLabels)
        {
            cw.Duplicate();
            cw.Length();
            cw.Push(elements.Count);
            if (node.RequiresExactLength)
            {
                cw.Equal();
            }
            else
            {
                cw.GreaterThanOrEqual();
            }

            cw.JumpIfFalse(skip);
        }

        for (var i = 0; i < elements.Count; i++)
        {
            cw.Duplicate();

            cw.Push(i);
            cw.LoadIndex();
            BuildPattern(elements[i], ctx, rebind, openMatch, flags);

            cw.JumpIfFalse(skip);
        }

        cw.Drop();
        cw.Push(true);
        cw.Jump(ok);
        cw.MarkLabel(skip);
        cw.Drop();
        cw.Push(false);
        cw.MarkLabel(ok);
        cw.NoOperation();
    }

    private void PreinitLoweredPattern(LoweredPattern pattern, bool rebind, bool openMatch, int flags = VarFlags.None)
    {
        switch (pattern.Kind)
        {
            case LoweredPatternKind.TypeTest:
            case LoweredPatternKind.Nil:
            case LoweredPatternKind.Range:
            case LoweredPatternKind.Wildcard:
            case LoweredPatternKind.Literal:
                break;
            case LoweredPatternKind.Name:
                PreinitName(pattern, flags);
                break;
            case LoweredPatternKind.Tuple:
            case LoweredPatternKind.Array:
                PreinitSequence(pattern.Children, rebind, openMatch, flags);
                break;
            case LoweredPatternKind.Not:
                PreinitLoweredPattern(pattern.Children[0], rebind, openMatch, flags);
                break;
            case LoweredPatternKind.And:
                PreinitAnd(pattern, rebind, openMatch, flags);
                break;
            case LoweredPatternKind.Or:
                PreinitOr(pattern, rebind, openMatch, flags);
                break;
            case LoweredPatternKind.Constructor:
                PreinitCtor(pattern, rebind, openMatch, flags);
                break;
        }
    }

    private void PreinitCtor(LoweredPattern node, bool rebind, bool openMatch, int flags)
    {
        if (node.Children.Count > 0)
        {
            PreinitSequence(node.Children, rebind, openMatch, flags);
        }
    }

    private void PreinitName(LoweredPattern node, int flags)
    {
        var name = node.Name!;

        if (!char.IsUpper(name[0]))
        {
            var found = target.TryGetLocalVariable(name, out var sv);
            int sva;

            if (!found)
            {
                sva = target.AddVariable(name, node.Location, flags);
            }
            else
            {
                sva = sv.Address;
            }

            cw.LoadNil();
            cw.StoreVariable(sva);
        }
    }

    private void PreinitAnd(LoweredPattern node, bool rebind, bool openMatch, int flags)
    {
        PreinitLoweredPattern(node.Children[0], rebind, openMatch, flags);
        PreinitLoweredPattern(node.Children[1], rebind, openMatch, flags);
    }

    private void PreinitOr(LoweredPattern node, bool rebind, bool openMatch, int flags)
    {
        PreinitLoweredPattern(node.Children[0], rebind, openMatch, flags);
        PreinitLoweredPattern(node.Children[1], rebind, openMatch, flags);
    }

    private void PreinitSequence(IReadOnlyList<LoweredPattern> elements, bool rebind, bool openMatch, int flags)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            PreinitLoweredPattern(elements[i], rebind, openMatch, flags);
        }
    }

}
