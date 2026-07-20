using Spellkit.Linker;
using Spellkit.Parser;
using Spellkit.Debug;
using Spellkit.Parser.Model;
using Spellkit.Compiler.Lowering;
using Spellkit.Runtime;
using Spellkit.Diagnostics;
using System;
using System.Collections.Generic;

namespace Spellkit.Compiler;

partial class Builder
{
    private void RegisterIndexerDeclaration(LoweredFunctionDeclaration node)
    {
        var accessor = node.Getter ? "getter" : node.Setter ? "setter" : "accessor";
        var typeName = node.TypeName?.ToString() ?? "<unknown>";
        var key = $"{typeName}:{accessor}";

        if (!indexerDeclarations.Add(key))
        {
            AddError(CompilerError.DuplicateIndexer, node.Location, accessor, typeName);
        }
    }

    //Converts symbolic names (used when overriding operators) to special internal
    //names, e.g. "*" becomes "__op_mul"
    private string GetMethodName(string name, LoweredFunctionDeclaration node)
    {
        switch (name)
        {
            case "+" when node.Parameters.Count == 0: return Builtins.Plus;
            case "-" when node.Parameters.Count == 0: return Builtins.Neg;
            case "!":
                if (node.Parameters.Count > 0)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.Not;
            case "Length":
                if (node.Parameters.Count > 0)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return name;
            case "ToLiteral":
                if (node.Parameters.Count > 0)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return name;
            case "Iterate":
                if (node.Parameters.Count > 0)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return name;
            case "Dispose":
                if (node.Parameters.Count > 0)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return name;
            case "Clone":
                if (node.Parameters.Count > 0)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return name;
            case "ToString":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return name;
            case "Contains":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return name;
            case "+":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.Add;
            case "-":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.Sub;
            case "*":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.Mul;
            case "<<":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.ShiftLeft;
            case ">>":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.ShiftRight;
            case "/":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.Div;
            case "==":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.Eq;
            case "!=":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.Neq;
            case ">":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.Gt;
            case "<":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.Lt;
            case ">=":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.Gte;
            case "<=":
                if (node.Parameters.Count > 1)
                {
                    AddError(CompilerError.BuiltinWrongArguments, node.Location);
                }

                return Builtins.Lte;
            default:
                return name;
        }
    }


    private int RegisterNominalDeclaration(LoweredNominalDeclaration node)
    {
        if (!char.IsUpper(node.Name[0]))
        {
            AddError(CompilerError.TypeNameCamel, node.Location);
        }

        if (currentScope != globalScope)
        {
            AddError(CompilerError.TypesOnlyGlobalScope, node.Location);
        }

        var unitId = unit.UnitIds.Count - 1;
        var ti = new TypeInfo(node, new(unitId, unit.ExportList));
        var typeVar = 0;

        if (types.Remove(node.Name))
        {
            AddError(CompilerError.TypeAlreadyDeclared, node.Location, node.Name);
        }
        else
        {
            var flags = VarFlags.Type | VarFlags.Const;
            if (node.IsPrivate)
            {
                flags |= VarFlags.Private;
            }

            typeVar = AddVariable(node.Name, node.Location, flags);
            cw.CreateType(node.Name);
            cw.StoreVariable(typeVar);
        }

        types.Add(node.Name, ti);
        return typeVar;
    }

    private void ValidateTraitContracts(LoweredNominalDeclaration declaration)
    {
        if (declaration.Style != TypeDeclarationStyle.Trait)
        {
            return;
        }

        var set = new HashSet<string>();

        foreach (var contract in declaration.Contracts)
        {
            var key = MemberKey(contract);

            if (!set.Add(key))
            {
                AddError(CompilerError.TraitMemberConflict, contract.Location, contract.Name ?? "<unknown>");
            }
        }
    }

    private static string MemberKey(LoweredFunctionDeclaration func)
    {
        var name = func.Name!;

        if (func.Setter && !func.IsIndexer)
        {
            name = Builtins.Setter(name);
        }

        return name;
    }

    private void GenerateConstructor(LoweredFunctionDeclaration func, CompilerContext ctx)
    {
        var declaration = types[func.TypeName!.Local].Declaration;
        var useImplState = declaration.PrivateFields.Count > 0 || declaration.Initializer is not null;

        if (useImplState)
        {
            for (var i = 0; i < declaration.PrivateFields.Count; i++)
            {
                var field = declaration.PrivateFields[i];
                var name = field.Name!;
                AddVariable(name, field.Location, field.Constant ? VarFlags.Const : VarFlags.None);

                if (field.Init is not null)
                {
                    loweredEmitter.Emit(field.Init, keepResult: true, ctx.WithTailPosition(false));
                }
                else
                {
                    cw.LoadNil();
                }

                cw.CreateLabel(name);

                if (!field.Constant)
                {
                    cw.MarkMutable();
                }
            }

            AddLinePragma(func.Location);
            cw.CreateTuple(declaration.PrivateFields.Count);
        }
        else if (func.Body is LoweredBlock body)
        {
            loweredEmitter.Emit(body with { NoScope = true }, keepResult: false, new());
            var count = 0;

            foreach (var (k, v) in currentScope.Locals)
            {
                if ((v.Data & VarFlags.Argument) != VarFlags.Argument)
                {
                    count++;
                    DirectPushScopeVar(k, new ScopeVar(0 | (v.Address << 8), v.Data));
                    cw.CreateLabel(k);

                    if ((v.Data & VarFlags.Const) != VarFlags.Const)
                    {
                        cw.MarkMutable();
                    }
                }
            }

            cw.CreateTuple(count);
        }
        else
        {
            cw.CreateTuple(0);
        }

        if (func.Parameters.Count == 0)
        {
            AddLinePragma(func.Location);
            cw.CreateTuple(0);
        }
        else
        {
            for (var i = 0; i < func.Parameters.Count; i++)
            {
                var p = func.Parameters[i];
                PushVariable(ctx, p.Name, p.Location);
                cw.CreateLabel(p.Name);

                if (p.Mutable)
                {
                    cw.MarkMutable();
                }
            }

            AddLinePragma(func.Location);
            cw.CreateTuple(func.Parameters.Count);
        }

        PushTypeInfo(ctx, func.TypeName!, func.Location);
        cw.CreateObject(func.Name!);

        if (declaration.Initializer is not null)
        {
            var init = declaration.Initializer;
            var thisVar = AddVariable("this", init.Location, data: VarFlags.Const | VarFlags.This);
            cw.Duplicate();
            cw.Duplicate();
            cw.StoreVariable(thisVar);

            AddLinePragma(init.Location);
            cw.LoadMember(HiddenInitMethodName);
            cw.PrepareCall(func.Parameters.Count);

            for (var i = 0; i < func.Parameters.Count; i++)
            {
                var p = init.Parameters[i];
                PushVariable(ctx, p.Name, p.Location);

                if (p.IsVarArgs)
                {
                    cw.SetNamedCallArgument(p.Name);
                }
                else
                {
                    cw.SetCallArgument(i);
                }
            }

            AddLinePragma(init.Location);
            cw.InvokePreparedCall(func.Parameters.Count);
            cw.Drop();
        }
    }

    private int PushTypeInfo(CompilerContext ctx, Qualident qual, Location loc)
    {
        if (qual.Parent is null) //Type is local
        {
            return PushVariable(ctx, qual.Local, loc);
        }
        else //Type is external
        {
            //Can't find module
            if (!referencedUnits.TryGetValue(qual.Parent, out var info))
            {
                AddError(CompilerError.UndefinedModule, loc, qual.Parent);
                return default;
            }

            //Push type from found module
            return PushTypeInfo(ctx, info, qual.Local, loc);
        }
    }

    private int PushTypeInfo(CompilerContext _, UnitInfo info, string name, Location loc)
    {
        //Can't find type in the module
        if (!info.ExportList.TryGetValue(name, out var sv))
        {
            AddError(CompilerError.UndefinedType, loc, name);
            return default;
        }

        //Imported private types cannot be accessed outside their declaring module.
        if ((sv.Data & VarFlags.Private) == VarFlags.Private)
        {
            AddError(CompilerError.PrivateNameAccess, loc, name);
        }

        cw.LoadVariable(new(info.Handle | (sv.Address >> 8) << 8, sv.Data | VarFlags.External));
        return info.Handle | (sv.Address >> 8) << 8;
    }

}
