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
    private void EmitAssignment(LoweredAssignment node, bool keepResult, CompilerContext ctx)
    {
        var resolvedTarget = ResolveStoreTarget(node.Target, ctx);
        node = node with
        {
            Target = resolvedTarget,
            Kind = ResolveAssignmentKind(resolvedTarget.Kind, node.AutoAssign)
        };

        _ = node.Kind switch
        {
            LoweredAssignmentKind.PublicMemberSetterCoalesceAssign => EmitSetterCoalesce(node, keepResult, ctx),
            LoweredAssignmentKind.PublicMemberSetterAutoAssign => EmitSetterAutoAssign(node, keepResult, ctx),
            LoweredAssignmentKind.PublicMemberSetter => EmitSetter(node, keepResult, ctx),
            LoweredAssignmentKind.CoalesceAssign => EmitCoalesce(node, keepResult, ctx),
            LoweredAssignmentKind.AutoAssign => EmitAutoAssign(node, keepResult, ctx),
            _ => EmitSimpleAssignment(node, keepResult, ctx)
        };
    }

    private bool EmitSetter(LoweredAssignment node, bool keepResult, CompilerContext ctx)
    {
        EmitSetterCall(node.Target.Target, node.Value, node.Target.Name!, ctx, keepResult);
        return true;
    }

    private bool EmitSetterCoalesce(LoweredAssignment node, bool keepResult, CompilerContext ctx)
    {
        var exitLab = cw.DefineLabel();
        EmitGetter(node.Target.Target, node.Target.Name!, ctx);
        cw.JumpIfTrue(exitLab);
        EmitSetterCall(node.Target.Target, node.Value, node.Target.Name!, ctx, keepResult: false);
        cw.MarkLabel(exitLab);
        cw.NoOperation();
        if (keepResult)
        {
            cw.LoadNil();
        }

        return true;
    }

    private bool EmitCoalesce(LoweredAssignment node, bool keepResult, CompilerContext ctx)
    {
        var exitLab = cw.DefineLabel();
        EmitLoadStoreTarget(node.Target, ctx);
        cw.JumpIfTrue(exitLab);
        EmitValue(node.Value, ctx);
        StoreTarget(node.Target, ctx);
        cw.MarkLabel(exitLab);
        cw.NoOperation();
        if (keepResult)
        {
            cw.LoadNil();
        }

        return true;
    }

    private bool EmitSetterAutoAssign(LoweredAssignment node, bool keepResult, CompilerContext ctx)
    {
        EmitValue(node.Target.Target, ctx, tailPosition: false);
        cw.LoadMember(Builtins.Setter(node.Target.Name!));
        cw.PrepareCall(1);
        EmitGetter(node.Target.Target, node.Target.Name!, ctx);
        EmitValue(node.Value, ctx);
        target.EmitBinaryOp(node.AutoAssign!.Value);
        cw.SetCallArgument(0);
        cw.InvokePreparedCall(1);
        if (!keepResult)
        {
            cw.Drop();
        }

        return true;
    }

    private bool EmitAutoAssign(LoweredAssignment node, bool keepResult, CompilerContext ctx)
    {
        EmitLoadStoreTarget(node.Target, ctx);
        EmitValue(node.Value, ctx);
        target.EmitBinaryOp(node.AutoAssign!.Value);

        if (target.ErrorCount == 0)
        {
            StoreTarget(node.Target, ctx);
        }

        if (keepResult)
        {
            cw.LoadNil();
        }

        return true;
    }

    private bool EmitSimpleAssignment(LoweredAssignment node, bool keepResult, CompilerContext ctx)
    {
        EmitValue(node.Value, ctx);
        StoreTarget(node.Target, ctx);
        if (keepResult)
        {
            cw.LoadNil();
        }

        return true;
    }

    private void EmitAccess(LoweredAccess node, bool keepResult, CompilerContext ctx)
    {
        node = ResolveAccess(node, ctx);

        if (node.Kind is LoweredAccessKind.ModuleExport or LoweredAccessKind.ModuleType)
        {
            if ((node.ModuleSymbol.Data & VarFlags.Private) == VarFlags.Private)
            {
                target.AddError(CompilerError.PrivateNameAccess, node.Location, node.Name);
            }

            target.AddLinePragma(node.Location);
            cw.LoadVariable(node.ModuleSymbol);
            if (!keepResult)
            {
                cw.Drop();
            }

            return;
        }

        EmitValue(node.Target, ctx);

        switch (node.Kind)
        {
            case LoweredAccessKind.PublicMember:
                target.AddLinePragma(node.Location);
                cw.LoadMember(node.Name);
                break;
            case LoweredAccessKind.PrivateMember:
                target.AddLinePragma(node.Location);
                cw.LoadPrivateMember(node.Name);
                break;
            case LoweredAccessKind.IndexedMemberName:
                target.AddLinePragma(node.Location);
                cw.Push(node.Name);
                cw.LoadIndex();
                break;
            case LoweredAccessKind.LocalEnumConstant:
                target.AddLinePragma(node.Location);
                cw.LoadMember(node.Name);
                cw.PrepareCall(0);
                cw.InvokePreparedCall(0);
                break;
        }

        if (!keepResult)
        {
            cw.Drop();
        }
    }

    private void EmitIndexer(LoweredIndexer node, bool keepResult, CompilerContext ctx)
    {
        node = ResolveIndexer(node, ctx);
        EmitValue(node.Target, ctx);
        EmitIndexerValue(node, ctx);
        if (!keepResult)
        {
            cw.Drop();
        }
    }

    private void EmitGetter(LoweredNode receiver, string member, CompilerContext ctx)
    {
        EmitValue(receiver, ctx, tailPosition: false);
        cw.LoadMember(member);
    }

    private void EmitSetterCall(LoweredNode receiver, LoweredNode value, string member, CompilerContext ctx, bool keepResult)
    {
        EmitValue(receiver, ctx);
        cw.LoadMember(Builtins.Setter(member));
        cw.PrepareCall(1);
        EmitValue(value, ctx);
        cw.SetCallArgument(0);
        cw.InvokePreparedCall(1);

        if (!keepResult)
        {
            cw.Drop();
        }
    }

    private void EmitLoadStoreTarget(LoweredStoreTarget node, CompilerContext ctx)
    {
        switch (node.Kind)
        {
            case LoweredStoreKind.Name:
                EmitValue(node.Target, ctx, tailPosition: false);
                break;
            case LoweredStoreKind.PublicMemberSetter:
                EmitGetter(node.Target, node.Name!, ctx);
                break;
            case LoweredStoreKind.PrivateMember:
                EmitValue(node.Target, ctx, tailPosition: false);
                target.AddLinePragma(node.Location);
                cw.LoadPrivateMember(node.Name!);
                break;
            case LoweredStoreKind.IndexedMemberName:
                EmitValue(node.Target, ctx, tailPosition: false);
                target.AddLinePragma(node.Location);
                cw.Push(node.Name!);
                cw.LoadIndex();
                break;
            case LoweredStoreKind.Indexed:
            case LoweredStoreKind.RawIndexed:
                var indexer = (LoweredIndexer)node.Target;
                EmitValue(indexer.Target, ctx);
                EmitIndexerValue(indexer, ctx);
                break;
            default:
                target.AddError(CompilerError.UnableAssignExpression, node.Location, node.Target);
                break;
        }
    }

    private void StoreTarget(LoweredStoreTarget node, CompilerContext ctx)
    {
        switch (node.Kind)
        {
            case LoweredStoreKind.Name:
                target.StoreName(((LoweredName)node.Target).Value, node.Target.Location, ctx);
                break;
            case LoweredStoreKind.PrivateMember:
                EmitValue(node.Target, ctx);
                target.AddLinePragma(node.Location);
                cw.StorePrivateMember(node.Name!);
                cw.Drop();
                break;
            case LoweredStoreKind.IndexedMemberName:
                EmitValue(node.Target, ctx);
                target.AddLinePragma(node.Location);
                cw.Push(node.Name!);
                cw.StoreIndex();
                cw.Drop();
                break;
            case LoweredStoreKind.Indexed:
            case LoweredStoreKind.RawIndexed:
                StoreIndexer((LoweredIndexer)node.Target, ctx);
                break;
            default:
                target.AddError(CompilerError.UnableAssignExpression, node.Location, node.Target);
                break;
        }
    }

    private void EmitIndexerValue(LoweredIndexer node, CompilerContext ctx)
    {
        if (node.Kind == LoweredIndexerKind.Slice)
        {
            var range = (LoweredRange)node.Index;

            if (range.Exclusive)
            {
                target.AddError(CompilerError.InvalidSlice, range.Location);
            }

            cw.LoadMember(Builtins.Slice);
            cw.PrepareCall(2);

            if (range.From is null)
            {
                cw.Push(0);
            }
            else
            {
                EmitValue(range.From, ctx);
            }

            cw.SetCallArgument(0);

            if (range.To is not null)
            {
                EmitValue(range.To, ctx);
            }
            else
            {
                cw.LoadNil();
            }

            cw.SetCallArgument(1);
            target.AddLinePragma(node.Location);
            cw.InvokePreparedCall(2);
            return;
        }

        EmitValue(node.Index, ctx);
        target.AddLinePragma(node.Location);
        if (node.Kind == LoweredIndexerKind.RawIndexed)
        {
            cw.LoadRawIndex();
        }
        else
        {
            cw.LoadIndex();
        }
    }

    private void StoreIndexer(LoweredIndexer node, CompilerContext ctx)
    {
        if (node.Kind == LoweredIndexerKind.Slice)
        {
            target.AddError(CompilerError.SliceNotSupported, node.Index.Location);
            return;
        }

        EmitValue(node.Target, ctx);
        EmitValue(node.Index, ctx);
        target.AddLinePragma(node.Location);
        if (node.Kind == LoweredIndexerKind.RawIndexed)
        {
            cw.StoreRawIndex();
        }
        else
        {
            cw.StoreIndex();
        }

        cw.Drop();
    }

    private LoweredAccess ResolveAccess(LoweredAccess node, CompilerContext ctx)
    {
        if (node.Kind is not LoweredAccessKind.Unresolved)
        {
            return node;
        }

        var privateAccess = IsPrivateAccess(node.Target, node.Name, node.SpecialName, ctx);

        if (!privateAccess && node.Target is LoweredName targetName && !node.SpecialName)
        {
            if (char.IsUpper(node.Name[0])
                && target.TryResolveZeroArgConstructor(
                    new Qualident(node.Name, targetName.Value),
                    out _,
                    out _))
            {
                return node with { Kind = LoweredAccessKind.LocalEnumConstant };
            }

            if (!target.NoOptimizations
                && target.TryResolveModuleMember(targetName.Value, node.Name, out var symbol))
            {
                var kind = (symbol.Data & VarFlags.Type) == VarFlags.Type
                    ? LoweredAccessKind.ModuleType
                    : LoweredAccessKind.ModuleExport;
                return node with { Kind = kind, ModuleSymbol = symbol };
            }
        }

        if ((char.IsUpper(node.Name[0]) || node.SpecialName) && !privateAccess)
        {
            return node with { Kind = LoweredAccessKind.PublicMember };
        }

        return node with
        {
            Kind = privateAccess
                ? LoweredAccessKind.PrivateMember
                : LoweredAccessKind.IndexedMemberName
        };
    }

    private static LoweredIndexer ResolveIndexer(LoweredIndexer node, CompilerContext ctx)
    {
        if (node.Kind is not LoweredIndexerKind.Unresolved)
        {
            return node;
        }

        var raw = ctx.Function is { IsIndexer: true }
            && node.Target is LoweredName { Value: "this" };
        return node with { Kind = raw ? LoweredIndexerKind.RawIndexed : LoweredIndexerKind.Indexed };
    }

    private static LoweredStoreTarget ResolveStoreTarget(LoweredStoreTarget node, CompilerContext ctx)
    {
        if (node.Kind is LoweredStoreKind.UnresolvedIndexer)
        {
            var indexer = ResolveIndexer((LoweredIndexer)node.Target, ctx);
            return node with
            {
                Target = indexer,
                Kind = indexer.Kind is LoweredIndexerKind.RawIndexed
                    ? LoweredStoreKind.RawIndexed
                    : LoweredStoreKind.Indexed
            };
        }

        if (node.Kind is not LoweredStoreKind.UnresolvedAccess)
        {
            return node;
        }

        var privateAccess = IsPrivateAccess(node.Target, node.Name!, node.SpecialName, ctx);
        if ((char.IsUpper(node.Name![0]) || node.SpecialName) && !privateAccess)
        {
            return node with { Kind = LoweredStoreKind.PublicMemberSetter };
        }

        return node with
        {
            Kind = privateAccess
                ? LoweredStoreKind.PrivateMember
                : LoweredStoreKind.IndexedMemberName
        };
    }

    private static bool IsPrivateAccess(
        LoweredNode receiver,
        string member,
        bool specialName,
        CompilerContext ctx) =>
        !specialName
        && !char.IsUpper(member[0])
        && ctx.Function is { TypeName: not null, IsStatic: false }
        && receiver is LoweredName { Value: "this" };

    private static LoweredAssignmentKind ResolveAssignmentKind(
        LoweredStoreKind targetKind,
        BinaryOperator? autoAssign) =>
        targetKind switch
        {
            LoweredStoreKind.PublicMemberSetter when autoAssign is BinaryOperator.Coalesce =>
                LoweredAssignmentKind.PublicMemberSetterCoalesceAssign,
            LoweredStoreKind.PublicMemberSetter when autoAssign is not null =>
                LoweredAssignmentKind.PublicMemberSetterAutoAssign,
            LoweredStoreKind.PublicMemberSetter =>
                LoweredAssignmentKind.PublicMemberSetter,
            _ when autoAssign is BinaryOperator.Coalesce =>
                LoweredAssignmentKind.CoalesceAssign,
            _ when autoAssign is not null =>
                LoweredAssignmentKind.AutoAssign,
            _ => LoweredAssignmentKind.Simple
        };

}
