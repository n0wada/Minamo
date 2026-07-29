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
    private void EmitImport(LoweredImport lowered)
    {
        var node = lowered;
        var resolution = target.LinkImport(node);

        switch (node.Kind)
        {
            case ImportKind.All:
                RegisterAllImportedSymbols(node, resolution.LinkedUnit, resolution.ModuleIndex);
                EmitImportedModuleExecution(resolution.ModuleIndex);
                break;
            case ImportKind.Symbol:
                RegisterImportedSymbol(node, resolution.LinkedUnit, resolution.ModuleIndex);
                EmitImportedModuleExecution(resolution.ModuleIndex);
                break;
            default:
                RegisterModuleImport(node, resolution.LinkedUnit, resolution.ModuleIndex);
                break;
        }
    }

    private void RegisterModuleImport(LoweredImport node, Unit linkedUnit, int moduleIndex)
    {
        var key = node.Alias ?? node.ModuleName;

        if (!target.TryAddReferencedUnit(key, new UnitInfo(moduleIndex, linkedUnit.ExportList)))
        {
            target.AddError(CompilerError.DuplicateModuleAlias, node.Location, key);
            return;
        }

        EmitImportedModuleBinding(key, node.Location, moduleIndex);
    }

    private void RegisterAllImportedSymbols(LoweredImport node, Unit linkedUnit, int moduleIndex)
    {
        foreach (var (name, symbol) in linkedUnit.ExportList)
        {
            if ((symbol.Data & VarFlags.Private) == VarFlags.Private)
            {
                continue;
            }

            TryRegisterImportedSymbol(node, (string)name, symbol, moduleIndex, reportDuplicate: false);
        }
    }

    private void RegisterImportedSymbol(LoweredImport node, Unit linkedUnit, int moduleIndex)
    {
        var name = node.SymbolName!;

        if (!linkedUnit.ExportList.TryGetValue(name, out var symbol))
        {
            target.AddError(CompilerError.UndefinedVariable, node.Location, name);
            return;
        }

        if ((symbol.Data & VarFlags.Private) == VarFlags.Private)
        {
            target.AddError(CompilerError.PrivateNameAccess, node.Location, name);
            return;
        }

        TryRegisterImportedSymbol(node, name, symbol, moduleIndex, reportDuplicate: true);
    }

    private bool TryRegisterImportedSymbol(LoweredImport node, string name, ScopeVar symbol, int moduleIndex, bool reportDuplicate)
    {
        if (target.IsImportNameUsed(name))
        {
            if (reportDuplicate)
            {
                target.AddError(CompilerError.DuplicateModuleAlias, node.Location, name);
            }

            return false;
        }

        target.TryAddImportedSymbol(name, new ImportedSymbol(moduleIndex, symbol));
        return true;
    }

    private void EmitImportedModuleBinding(string name, Location location, int moduleIndex)
    {
        cw.LoadModule(moduleIndex);
        var addr = target.AddVariable(name, location, VarFlags.Module | VarFlags.Const | VarFlags.Private);
        cw.StoreVariable(addr);
    }

    private void EmitImportedModuleExecution(int moduleIndex)
    {
        cw.LoadModule(moduleIndex);
        cw.Drop();
    }

    private void EmitNominalDeclaration(LoweredNominalDeclaration lowered, CompilerContext ctx)
    {
        var node = lowered;
        var typeVar = target.RegisterNominalDeclaration(node);

        foreach (var constructor in node.Constructors)
        {
            EmitEffect(constructor, ctx);
        }

        foreach (var contract in node.Contracts)
        {
            EmitEffect(contract, ctx);
        }

        target.ValidateTraitContracts(node);

        if (lowered.AutoLookup)
        {
            EmitMixin(typeVar, new Qualident("Lookup"), node.Location, ctx);
        }

        if (node.Mixins is not null)
        {
            EmitExplicitMixins(node, typeVar, ctx, lowered.AutoLookup);
        }

        if (lowered.NeedsValue)
        {
            cw.LoadNil();
        }
    }

    private void EmitExplicitMixins(
        LoweredNominalDeclaration node,
        int typeVar,
        CompilerContext ctx,
        bool autoLookup)
    {
        var set = new HashSet<int>();

        foreach (var mixin in node.Mixins!)
        {
            if (autoLookup && mixin.Parent is null && mixin.Local == "Lookup")
            {
                target.AddError(CompilerError.LookupImplicitForStructAndEnum, node.Location);
                continue;
            }

            if (mixin.Parent is null && mixin.Local == node.Name)
            {
                target.AddError(CompilerError.MixinSameAsType, node.Location, mixin.ToString());
            }

            var code = EmitMixin(typeVar, mixin, node.Location, ctx);

            if (set.Contains(code))
            {
                target.AddError(CompilerError.MixinAlreadySpecified, node.Location, mixin.ToString());
            }
            else
            {
                set.Add(code);
            }

            if (code < 0 && -code < Spk.Number)
            {
                target.AddError(CompilerError.InvalidMixin, node.Location, mixin.Local.ToString());
            }
        }
    }

    private int EmitMixin(int typeVar, Qualident mixin, Location location, CompilerContext ctx)
    {
        cw.LoadVariable(new ScopeVar(typeVar));
        var code = target.PushTypeInfo(ctx, mixin, location);
        cw.ApplyMixin();
        return code;
    }

    private void EmitImplDeclaration(LoweredImplDeclaration lowered, CompilerContext ctx)
    {
        var node = lowered;

        if (!target.IsGlobalScope)
        {
            target.AddError(CompilerError.ImplOnlyGlobalScope, node.Location);
            if (lowered.NeedsValue)
            {
                cw.LoadNil();
            }

            return;
        }

        if (!target.TryGetTypeInfo(node.TargetName, out var typeInfo))
        {
            target.AddError(CompilerError.UndefinedType, node.Location, node.TargetName);
            if (lowered.NeedsValue)
            {
                cw.LoadNil();
            }

            return;
        }

        if (typeInfo.Declaration.Style == TypeDeclarationStyle.Trait)
        {
            EmitTraitImpl(node, typeInfo, lowered.NeedsValue, ctx);
            return;
        }

        if (typeInfo.Declaration.Style != TypeDeclarationStyle.Struct)
        {
            target.AddError(CompilerError.ImplStructOnly, node.Location);
        }

        var set = new HashSet<int>();
        var traitMembers = new HashSet<string>();
        var implTarget = new Qualident(node.TargetName);
        var fieldsAdded = false;
        var initAdded = false;

        foreach (var mixin in node.Mixins)
        {
            if (mixin.Parent is null && mixin.Local == node.TargetName)
            {
                target.AddError(CompilerError.MixinSameAsType, node.Location, mixin.ToString());
            }

            if (TryGetLocalTrait(mixin, out var traitInfo))
            {
                if (ApplyTraitState(typeInfo.Declaration, traitInfo.Declaration, traitMembers, node.Location))
                {
                    fieldsAdded = true;
                }
            }

            target.PushTypeInfo(ctx, implTarget, node.Location);
            var code = target.PushTypeInfo(ctx, mixin, node.Location);

            if (set.Contains(code))
            {
                target.AddError(CompilerError.MixinAlreadySpecified, node.Location, mixin.ToString());
            }
            else
            {
                set.Add(code);
            }

            if (code < 0 && -code < Spk.Number)
            {
                target.AddError(CompilerError.InvalidMixin, node.Location, mixin.Local.ToString());
            }

            cw.ApplyMixin();
        }

        foreach (var member in node.Members)
        {
            if (member is LoweredImplField fieldMember)
            {
                var field = fieldMember.Field;
                if (!TryAddImplField(typeInfo.Declaration, field))
                {
                    target.AddError(CompilerError.InvalidImplField, field.Location);
                }
                else
                {
                    fieldsAdded = true;
                }

                continue;
            }

            if (member is LoweredImplFunction { Function.Name: "init" } initMember)
            {
                var init = PrepareImplInitializer(typeInfo.Declaration, initMember.Function, implTarget);
                if (init is null)
                {
                    target.AddError(CompilerError.InvalidImplInit, initMember.Location);
                }
                else
                {
                    EmitEffect(init, ctx);
                    initAdded = true;
                }

                continue;
            }

            if (member is not LoweredImplFunction functionMember)
            {
                target.AddError(CompilerError.InvalidImplMember, member.Location);
                continue;
            }

            var function = ApplyImplTarget(functionMember.Function, implTarget);
            if (function is null)
            {
                target.AddError(CompilerError.InvalidImplMember, member.Location);
                continue;
            }

            EmitEffect(function, ctx);
        }

        if (fieldsAdded || initAdded)
        {
            foreach (var constructor in typeInfo.Declaration.Constructors)
            {
                EmitEffect(constructor, ctx);
            }
        }

        if (lowered.NeedsValue)
        {
            cw.LoadNil();
        }
    }

    private void EmitTraitImpl(LoweredImplDeclaration node, TypeInfo typeInfo, bool needsValue, CompilerContext ctx)
    {
        if (node.Mixins.Count > 0)
        {
            target.AddError(CompilerError.TraitMixinNotAllowed, node.Location, node.TargetName);
        }

        var implTarget = new Qualident(node.TargetName);
        var contracts = new HashSet<string>();
        CollectTraitContracts(typeInfo.Declaration, contracts);

        foreach (var member in node.Members)
        {
            if (member is LoweredImplField fieldMember)
            {
                var field = fieldMember.Field;
                if (!TryAddImplField(typeInfo.Declaration, field))
                {
                    target.AddError(CompilerError.InvalidImplField, field.Location);
                }

                continue;
            }

            if (member is LoweredImplFunction functionMember)
            {
                var func = functionMember.Function;
                if (IsPublicImplMember(func) && !contracts.Contains(MemberKey(func)))
                {
                    target.AddError(CompilerError.PublicImplMemberNotInTrait, func.Location, func.Name ?? "<unknown>");
                    continue;
                }

                var attached = ApplyImplTarget(func, implTarget);
                if (attached is null)
                {
                    target.AddError(CompilerError.InvalidImplMember, func.Location);
                    continue;
                }

                typeInfo.Declaration.ProtectedMethods.Add(attached);
                EmitEffect(attached, ctx);
                continue;
            }

            target.AddError(CompilerError.InvalidImplMember, member.Location);
        }

        if (needsValue)
        {
            cw.LoadNil();
        }
    }

    private bool ApplyTraitState(LoweredNominalDeclaration implTarget, LoweredNominalDeclaration trait, HashSet<string> members, Location loc)
    {
        var changed = false;
        var names = new HashSet<string>();

        foreach (var field in implTarget.PrivateFields)
        {
            if (field.Name is not null)
            {
                names.Add(field.Name);
            }
        }

        foreach (var field in trait.PrivateFields)
        {
            var name = field.Name!;

            if (!names.Add(name))
            {
                target.AddError(CompilerError.TraitMemberConflict, loc, name);
            }
            else if (!TryAddImplField(implTarget, field))
            {
                target.AddError(CompilerError.InvalidImplField, field.Location);
            }
            else
            {
                changed = true;
            }
        }

        foreach (var method in trait.ProtectedMethods)
        {
            var key = MemberKey(method);

            if (!members.Add(key))
            {
                target.AddError(CompilerError.TraitMemberConflict, loc, method.Name ?? "<unknown>");
            }
        }

        return changed;
    }

    private bool TryGetLocalTrait(Qualident name, out TypeInfo traitInfo)
    {
        traitInfo = null!;

        if (name.Parent is not null || !target.TryGetTypeInfo(name.Local, out var info)
            || info.Declaration.Style != TypeDeclarationStyle.Trait)
        {
            return false;
        }

        traitInfo = info;
        return true;
    }

    private static void CollectTraitContracts(LoweredNominalDeclaration trait, HashSet<string> contracts)
    {
        foreach (var contract in trait.Contracts)
        {
            contracts.Add(MemberKey(contract));
        }
    }

    private static bool IsPublicImplMember(LoweredFunctionDeclaration func) =>
        func.Name is not null && (func.Name.Length == 0 || !char.IsLower(func.Name[0]));

    private static string MemberKey(LoweredFunctionDeclaration func)
    {
        var name = func.Name!;

        if (func.Setter && !func.IsIndexer)
        {
            name = Builtins.Setter(name);
        }

        return name;
    }

    private static bool TryAddImplField(LoweredNominalDeclaration declaration, LoweredField field)
    {
        if (field.AutoClose || field.Name is null)
        {
            return false;
        }

        for (var i = 0; i < declaration.PrivateFields.Count; i++)
        {
            if (declaration.PrivateFields[i].Name == field.Name)
            {
                return false;
            }
        }

        declaration.PrivateFields.Add(field);
        return true;
    }

    private static LoweredFunctionDeclaration? PrepareImplInitializer(
        LoweredNominalDeclaration declaration,
        LoweredFunctionDeclaration init,
        Qualident implTarget)
    {
        if (declaration.Initializer is not null)
        {
            return null;
        }

        var attached = ApplyImplTarget(init, implTarget);
        if (attached is null)
        {
            return null;
        }

        if (declaration.Constructors.Count != 1 || attached.Parameters.Count != declaration.Constructors[0].Parameters.Count)
        {
            return null;
        }

        attached = attached with { IsImplInitializer = true };
        declaration.Initializer = attached;
        return attached;
    }

    private static LoweredFunctionDeclaration? ApplyImplTarget(LoweredFunctionDeclaration node, Qualident implTarget)
    {
        if (node.TypeName is not null || node.TargetTypeName is not null || node.IsConstructor)
        {
            return null;
        }

        return node with { TypeName = implTarget };
    }

    private void EmitFunctionDeclaration(LoweredFunctionDeclaration lowered, CompilerContext ctx)
    {
        var node = lowered;

        if (node.Name is not null || node.TargetTypeName is not null)
        {
            var flags = VarFlags.Const | VarFlags.Function;
            var addr = 0;

            if (lowered.IsStdCall)
            {
                flags |= VarFlags.StdCall;
            }

            if (node.IsPrivate)
            {
                flags |= VarFlags.Private;
            }

            if (node.TypeName is null && node.Name is not null)
            {
                addr = target.AddVariable(node.Name, node.Location, flags, lowered.IsStdCall ? node.Parameters.Count : 0);
            }

            EmitFunctionBody(addr, node, ctx, lowered.IteratorBody);

            if (lowered.NeedsValue)
            {
                cw.Duplicate();
            }

            if (node.TypeName is not null)
            {
                EmitMethodAttachment(node, ctx);
            }

            target.AddLinePragma(node.Location);

            if (node.TypeName is not null)
            {
                cw.NoOperation();
            }
            else
            {
                cw.StoreVariable(addr);
            }

            return;
        }

        EmitFunctionBody(-1, node, ctx, lowered.IteratorBody);
        target.AddLinePragma(node.Location);
        cw.NoOperation();

        if (!lowered.NeedsValue)
        {
            cw.Drop();
        }
    }

    private void EmitSelectDeclaration(LoweredSelectDeclaration node, bool keepResult, CompilerContext ctx)
    {
        if (node.Locals.Count > 0 && !node.IsInstanceFactory)
        {
            EmitSelectFactoryTemplate(node, keepResult, ctx);
            return;
        }

        if (node.Name is not null && !node.IsInstanceFactory && !target.IsGlobalScope)
        {
            target.AddError(CompilerError.SelectOnlyGlobalScope, node.Location);
        }

        var selectAddress = node.Name is null || node.IsInstanceFactory ? -1 : target.AddVariable(
            node.Name,
            node.Location,
            VarFlags.Const | VarFlags.Private,
            args: 0);

        var states = new List<SelectStateDefinition>(node.States.Count);
        var initialCount = 0;
        var stateNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in node.States)
        {
            if (!stateNames.Add(state.Name))
            {
                target.AddError(CompilerError.SelectDuplicateState, node.Location, state.Name);
            }
        }

        var selectContext = ctx.WithSelectStates(node.Name ?? "<anonymous>", stateNames);
        var closureCount = 0;

        for (var i = 0; i < node.States.Count; i++)
        {
            var state = node.States[i];
            if (state.IsInitial)
            {
                initialCount++;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var choices = new List<SelectChoiceDefinition>(state.Choices.Count);
            for (var j = 0; j < state.Choices.Count; j++)
            {
                var choice = state.Choices[j];
                if (!names.Add(choice.Name))
                {
                    target.AddError(CompilerError.SelectDuplicateChoice, choice.Location, choice.Name, state.Name);
                }

                var hiddenName = $"$select:{node.Name ?? "anonymous"}:{i}:{j}";
                int? guardSlot = null;
                if (choice.Guard is not null)
                {
                    var hiddenGuardName = $"$select-guard:{node.Name}:{i}:{j}";
                    var guard = new LoweredFunctionDeclaration(
                        choice.Location,
                        TypeName: null,
                        TargetTypeName: null,
                        Name: hiddenGuardName,
                        IsStatic: false,
                        IsIndexer: false,
                        IsConstructor: false,
                        Getter: false,
                        Setter: false,
                        IsIterator: false,
                        IsImplInitializer: false,
                        IsPrivate: true,
                        Parameters: [],
                        Body: choice.Guard,
                        NeedsValue: false,
                        IteratorBody: false,
                        IsStdCall: !target.NoOptimizations);
                    EmitFunctionBody(-1, guard, selectContext, iteratorBody: false);
                    guardSlot = closureCount++;
                }

                var function = new LoweredFunctionDeclaration(
                    choice.Location,
                    TypeName: null,
                    TargetTypeName: null,
                    Name: hiddenName,
                    IsStatic: false,
                    IsIndexer: false,
                    IsConstructor: false,
                    Getter: false,
                    Setter: false,
                    IsIterator: false,
                    IsImplInitializer: false,
                    IsPrivate: true,
                    Parameters: choice.Parameters,
                    Body: choice.Body,
                    NeedsValue: false,
                    IteratorBody: false,
                    IsStdCall: !target.NoOptimizations);
                EmitFunctionBody(-1, function, selectContext, iteratorBody: false);
                var functionSlot = closureCount++;
                choices.Add(new(
                    choice.Name,
                    choice.Label,
                    choice.Description,
                    functionSlot,
                    guardSlot,
                    choice.Parameters.Count));
            }

            states.Add(new(state.Name, state.IsInitial, choices));
        }

        if (node.States.Count == 0)
        {
            target.AddError(CompilerError.SelectRequiresState, node.Location);
        }
        else if (initialCount != 1)
        {
            target.AddError(CompilerError.SelectRequiresOneInitialState, node.Location);
        }

        cw.CreateSelectFactory(new SpkSelectDefinitionValue(new SelectDefinition(node.Name, states), closureCount), closureCount);
        if (node.Name is not null && !node.IsInstanceFactory)
        {
            if (keepResult)
            {
                cw.Duplicate();
            }

            cw.StoreVariable(selectAddress);
        }
        else if (!keepResult)
        {
            cw.Drop();
        }
    }

    private void EmitSelectFactoryTemplate(LoweredSelectDeclaration node, bool keepResult, CompilerContext ctx)
    {
        if (node.Name is not null && !target.IsGlobalScope)
        {
            target.AddError(CompilerError.SelectOnlyGlobalScope, node.Location);
        }

        var selectAddress = node.Name is null ? -1 : target.AddVariable(
            node.Name,
            node.Location,
            VarFlags.Const | VarFlags.Private,
            args: 0);

        var concreteSelect = node with
        {
            Locals = Array.Empty<LoweredBinding>(),
            IsInstanceFactory = true
        };
        var initializerBody = new LoweredBlock(
            node.Location,
            [.. node.Locals, concreteSelect],
            HasAutoClose: false,
            NoScope: true);
        var initializer = new LoweredFunctionDeclaration(
            node.Location,
            TypeName: null,
            TargetTypeName: null,
            Name: $"$select-factory:{node.Name ?? "anonymous"}",
            IsStatic: false,
            IsIndexer: false,
            IsConstructor: false,
            Getter: false,
            Setter: false,
            IsIterator: false,
            IsImplInitializer: false,
            IsPrivate: true,
            Parameters: [],
            Body: initializerBody,
            NeedsValue: true,
            IteratorBody: false,
            IsStdCall: !target.NoOptimizations);
        EmitFunctionBody(-1, initializer, ctx, iteratorBody: false);
        cw.CreateSelectFactoryTemplate(SpkString.Get(node.Name ?? "<anonymous>"));

        if (node.Name is not null)
        {
            if (keepResult)
            {
                cw.Duplicate();
            }

            cw.StoreVariable(selectAddress);
        }
        else if (!keepResult)
        {
            cw.Drop();
        }
    }

    private void EmitFunctionBody(int addr, LoweredFunctionDeclaration node, CompilerContext oldctx, bool iteratorBody)
    {
        var args = CompileFunctionParameters(node);
        var functionName = node.IsImplInitializer
            ? "<init>"
            : node.Setter ? Builtins.Setter(node.Name!) : node.Name!;
        target.StartFunction(functionName, node.TypeName?.Local, args);

        if (node.IsStatic && node.TypeName is null)
        {
            target.AddError(CompilerError.StaticOnlyMethods, node.Location, node.Name!);
        }

        var startLabel = cw.DefineLabel();
        var funEndLabel = cw.DefineLabel();
        var funSkipLabel = cw.DefineLabel();
        cw.Jump(funSkipLabel);

        if (node.TypeName is not null && oldctx.Function is not null && oldctx.Function.TypeName is not null)
        {
            target.AddError(CompilerError.NestedMethod, node.Location);
        }

        var ctx = oldctx.WithFunction(
            function: node,
            functionAddress: target.SectionDepth | (addr >> 8) << 8,
            functionStart: startLabel,
            functionExit: funEndLabel,
            isIteratorBody: iteratorBody);

        target.StartScope(ScopeKind.Function, node.Location);
        target.StartSection();

        target.AddLinePragma(node.Location);
        var address = cw.Offset;

        var variadicIndex = BuildFunctionArguments(node, args);

        if (node.TypeName is not null && !node.IsStatic)
        {
            var va = target.AddVariable("this", node.Location, data: VarFlags.Const | VarFlags.This);
            cw.LoadThis();
            cw.StoreVariable(va);
        }

        if ((node.Getter || node.Setter) && node.TypeName is null)
        {
            target.AddError(CompilerError.AccessorOnlyMethod, node.Location);
        }

        cw.MarkLabel(startLabel);

        if (node.IsConstructor)
        {
            target.GenerateConstructor(node, ctx);
        }
        else if (node.IsIterator)
        {
            var iteratorBodyFunction = node with
            {
                TypeName = null,
                TargetTypeName = null,
                IsStatic = false,
                IsIndexer = false,
                IsConstructor = false,
                Getter = false,
                Setter = false,
                IsIterator = false,
                IsImplInitializer = false,
                Parameters = [],
                NeedsValue = true,
                IteratorBody = true,
                IsStdCall = !target.NoOptimizations
            };
            EmitFunctionDeclaration(iteratorBodyFunction, ctx);
        }
        else if (node.Body is null)
        {
            target.ThrowError(SpkError.NotImplemented);
            cw.Throw();
        }
        else
        {
            EmitValue(node.Body, ctx, tailPosition: true);
        }

        cw.MarkLabel(funEndLabel);

        if (iteratorBody)
        {
            cw.Drop();
            cw.LoadTerminator();
        }

        cw.Return();
        cw.MarkLabel(funSkipLabel);
        target.AddLinePragma(node.Location);

        var funHandle = target.FinishFunctionLayout(address);
        target.EndScope();
        target.EndSection();

        if (iteratorBody)
        {
            cw.CreateIterator(funHandle);
        }
        else
        {
            if (variadicIndex > -1)
            {
                cw.CreateVariadicFunction(funHandle, variadicIndex);
            }
            else
            {
                cw.CreateFunction(funHandle);
            }
        }

        if (node.Getter && !node.IsIndexer)
        {
            cw.SetFunctionAttribute(FunAttr.Auto);
        }
    }

    private Par[] CompileFunctionParameters(LoweredFunctionDeclaration node)
    {
        var pars = node.Parameters;
        var arr = new Par[pars.Count];
        var hasVarArg = false;

        for (var i = 0; i < pars.Count; i++)
        {
            var p = pars[i];

            if (p.IsVarArgs)
            {
                if (hasVarArg)
                {
                    target.AddError(CompilerError.VarArgOnlyOne, p.Location);
                }

                hasVarArg = true;
            }

            if (p.HasDefaultValue)
            {
                if (p.IsVarArgs)
                {
                    target.AddError(CompilerError.VarArgNoDefaultValue, p.Location);
                }

                SpkObject? val = null;

                switch (p.DefaultValue?.Kind)
                {
                    case LoweredLiteralKind.Integer:
                        val = new SpkInteger((long)p.DefaultValue.Value!);
                        break;
                    case LoweredLiteralKind.Float:
                        val = new SpkFloat((double)p.DefaultValue.Value!);
                        break;
                    case LoweredLiteralKind.Char:
                        val = new SpkChar((char)p.DefaultValue.Value!);
                        break;
                    case LoweredLiteralKind.Boolean:
                        val = (bool)p.DefaultValue.Value! ? SpkBool.True : SpkBool.False;
                        break;
                    case LoweredLiteralKind.String:
                        val = SpkString.Get((string)p.DefaultValue.Value!);
                        break;
                    case LoweredLiteralKind.Nil:
                        val = SpkNil.Instance;
                        break;
                    default:
                        target.AddError(CompilerError.InvalidDefaultValue, p.DefaultValueLocation, p.Name);
                        break;
                }

                arr[i] = new Par(p.Name, val, false, p.TypeAnnotation);
            }
            else
            {
                arr[i] = new Par(p.Name, null, p.IsVarArgs, p.TypeAnnotation);
            }
        }

        return arr;
    }

    private int BuildFunctionArguments(LoweredFunctionDeclaration node, Par[] args)
    {
        var variadicIndex = -1;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.IsVarArg)
            {
                variadicIndex = i;
            }

            target.AddVariable(arg.Name, node.Location, data: VarFlags.Argument);
        }

        return variadicIndex;
    }

    private bool IsStdCall(LoweredFunctionDeclaration node)
    {
        if (target.NoOptimizations)
        {
            return false;
        }

        if (node.TargetTypeName is not null)
        {
            return false;
        }

        for (var i = 0; i < node.Parameters.Count; i++)
        {
            if (node.Parameters[i].HasDefaultValue
                || node.Parameters[i].IsVarArgs)
            {
                return false;
            }
        }

        return true;
    }

    private void EmitMethodAttachment(LoweredFunctionDeclaration node, CompilerContext ctx)
    {
        if (ctx.Function is not null)
        {
            target.AddError(CompilerError.MethodNested, node.Location);
        }

        if (node.TargetTypeName is not null)
        {
            EmitCastAttachment(node, ctx);
        }

        if (node.IsIndexer)
        {
            ValidateIndexer(node);
        }

        if (node.Name is null)
        {
            return;
        }

        var realName = node.IsImplInitializer ? "<init>" : node.Name;

        if (!node.IsStatic && !node.IsImplInitializer)
        {
            realName = target.GetMethodName(realName, node);
        }

        if (node.Setter && !node.IsIndexer)
        {
            realName = Builtins.Setter(realName);

            if (node.Parameters.Count != 1)
            {
                target.AddError(CompilerError.SetterWrongArguments, node.Location);
            }
        }

        if (node.Getter && !node.IsIndexer && node.Parameters.Count > 0)
        {
            target.AddError(CompilerError.GetterWrongArguments, node.Location);
        }

        if (node.Name is Builtins.Has || (!node.IsStatic && node.Name is Builtins.Type))
        {
            target.AddError(CompilerError.OverrideNotAllowed, node.Location, node.Name);
        }

        target.PushTypeInfo(ctx, node.TypeName!, node.Location);

        if (node.IsStatic)
        {
            cw.StoreStaticMember(realName);
        }
        else
        {
            cw.StoreMember(realName);
        }
    }

    private void EmitCastAttachment(LoweredFunctionDeclaration node, CompilerContext ctx)
    {
        if (node.IsStatic || node.Setter || node.Getter)
        {
            target.AddError(CompilerError.InvalidCast, node.Location);
        }

        var t1 = target.PushTypeInfo(ctx, node.TargetTypeName!, node.Location);

        if (t1 < 0 && -t1 == Spk.Bool)
        {
            target.AddError(CompilerError.BoolCastNotAllowed, node.Location);
        }

        var t2 = target.PushTypeInfo(ctx, node.TypeName!, node.Location);

        if (t1 == t2)
        {
            target.AddError(CompilerError.SelfCastNotAllowed, node.Location);
        }

        cw.CreateCast();
    }

    private void ValidateIndexer(LoweredFunctionDeclaration node)
    {
        if (node.IsStatic)
        {
            target.AddError(CompilerError.IndexerStatic, node.Location);
        }

        if (node.Setter && node.Parameters.Count is not 2)
        {
            target.AddError(CompilerError.IndexerWrongArguments, node.Location);
        }

        if (node.Getter && node.Parameters.Count is not 1)
        {
            target.AddError(CompilerError.IndexerWrongArguments, node.Location);
        }

        if (!node.Getter && !node.Setter)
        {
            target.AddError(CompilerError.IndexerSetOrGet, node.Location);
        }

        target.RegisterIndexerDeclaration(node);
    }

}
