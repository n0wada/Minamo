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

internal sealed partial class Builder : ILoweredEmitterTarget
{
    private const int ERROR_LIMIT = 100;
    private const string HiddenInitMethodName = "<init>";

    private readonly BuilderOptions options; //Build options
    private readonly StackMachineEmitter cw; //Helper for byte code emit
    private readonly Scope globalScope; //Global scope (for variables) of the current unit
    private readonly Unit unit; //Unit (file) that is beign compiler
    private Scope currentScope; //Current lexical scope
    private readonly Label programEnd; //Label that marks an end of program
    private readonly SpellkitLinker linker; //Linker
    private readonly Dictionary<string, UnitInfo> referencedUnits;
    private readonly Dictionary<string, ImportedSymbol> importedSymbols;
    private readonly HashSet<string> indexerDeclarations;
    private readonly BuilderDiagnostics diagnostics;

    private readonly Dictionary<string, TypeInfo> types;
    private readonly LoweringPass lowering;
    private readonly LoweredEmitter loweredEmitter;

    public Builder(BuilderOptions options, SpellkitLinker linker)
    {
        referencedUnits = new();
        importedSymbols = new();
        indexerDeclarations = new();
        types = new();

        this.options = options;
        this.linker = linker;
        diagnostics = new(options);
        counters = new();
        pdb = new();
        isDebug = options.Debug;
        globalScope = new(ScopeKind.Function, null);
        unit = new()
        {
            GlobalScope = globalScope,
            Symbols = pdb.Symbols
        };
        cw = new(unit);
        currentScope = globalScope;
        programEnd = cw.DefineLabel();
        lowering = CreateLoweringPass();
        loweredEmitter = CreateLoweredEmitter();
    }

    public Builder(Builder builder)
    {
        linker = builder.linker;
        types = builder.types;
        referencedUnits = builder.referencedUnits;
        importedSymbols = builder.importedSymbols;
        indexerDeclarations = new(builder.indexerDeclarations);
        counters = new();
        options = builder.options;
        diagnostics = new(options);
        pdb = builder.pdb.Clone();
        unit = builder.unit.Clone(pdb.Symbols);
        cw = builder.cw.Clone(unit);
        globalScope = unit.GlobalScope!;
        currentScope = builder.currentScope != builder.globalScope
            ? builder.currentScope.Clone() : globalScope;
        isDebug = builder.isDebug;
        lastLocation = builder.lastLocation;
        counters = new(builder.counters.ToArray());
        currentCounter = builder.currentCounter;
        programEnd = cw.DefineLabel();
        lowering = CreateLoweringPass();
        loweredEmitter = CreateLoweredEmitter();
    }

    public Unit? Build(SpellkitCodeModel codeModel)
    {
        diagnostics.Clear();
        unit.FileName = codeModel.FileName;

        if (unit.Layouts.Count == 0)
        {
            unit.Layouts.Add(null!); //A layout reserved for the top level
        }

        cw.StartFrame(); //Start a new global frame
        var res = TryBuild(codeModel);

        if (!res)
        {
            return null;
        }

        cw.MarkLabel(programEnd);
        cw.FinishModule(); //Program should always end with this op code
        cw.CompileOpList();

        //Finalizing compilation, fixing top level layout
        unit.Layouts[0] = new(currentCounter, cw.FinishFrame(), 0);
        return unit;
    }

    //Main build cycle with error handling logic
    private bool TryBuild(SpellkitCodeModel codeModel)
    {
        try
        {
            var ctx = new CompilerContext();
            var loweredModule = lowering.LowerModule(
                codeModel,
                ctx,
                includeLangModule: !options.NoLangModule && unit.UnitIds.Count == 0);

            EmitLoweredModule(loweredModule, ctx);

            //Dispose use declarations in global scope
            CallAutos(cls: true);
            return diagnostics.Success;
        }
        //This is thrown when an error limit is exceeded
        catch (TerminationException)
        {
            return false;
        }
#if !DEBUG
        catch (Exception ex)
        {
            throw Ice(ex);
        }
#endif
    }

    private void EmitLoweredModule(LoweredModule module, CompilerContext ctx)
    {
        unit.FileName = module.FileName;

        foreach (var import in module.Imports)
        {
            loweredEmitter.Emit(import);
        }

        //What if we have no code, just imports? We shouldn't crush in this case
        if (module.Body.Count == 0)
        {
            cw.LoadNil();
        }

        PreInitLocalFunctions(module.Body);

        for (var i = 0; i < module.Body.Count; i++)
        {
            var last = i == module.Body.Count - 1;
            loweredEmitter.Emit(module.Body[i], keepResult: last, ctx);
        }
    }

    private SpellkitBuildException Ice(Exception? ex = null) => new($"Internal compiler error: {ex?.Message}", ex);

    internal List<BuildMessage> Messages => diagnostics.Messages;

    internal int ErrorCount => diagnostics.ErrorCount;

    private void AddError(CompilerError error, Location loc, params object[] args) =>
        AddError(error, unit.FileName!, loc, args);

    private void AddError(CompilerError error, string fileName, Location loc, params object[] args) =>
        diagnostics.AddError(error, fileName, new(Line(loc), Col(loc)), ERROR_LIMIT, args);

    private void AddWarning(CompilerWarning warning, Location loc, params object[] args) =>
        AddWarning(warning, unit.FileName!, loc, args);

    private void AddWarning(CompilerWarning warning, string fileName, Location loc, params object[] args) =>
        diagnostics.AddWarning(warning, fileName, new(Line(loc), Col(loc)), args);

    public bool Success => diagnostics.Success;

    StackMachineEmitter ILoweredEmitterTarget.Code => cw;

    int ILoweredEmitterTarget.ErrorCount => ErrorCount;

    bool ILoweredEmitterTarget.NoOptimizations => options.NoOptimizations;

    int ILoweredEmitterTarget.SectionDepth => counters.Count;

    bool ILoweredEmitterTarget.IsGlobalScope => currentScope == globalScope;

    void ILoweredEmitterTarget.EmitBinaryOp(BinaryOperator op) =>
        EmitBinaryOp(op);

    void ILoweredEmitterTarget.StoreName(string name, Location loc, CompilerContext ctx) =>
        PopVariable(ctx, name, loc);

    void ILoweredEmitterTarget.StartScope(ScopeKind kind, Location loc) =>
        StartScope(kind, loc);

    void ILoweredEmitterTarget.EndScope() =>
        EndScope();

    int ILoweredEmitterTarget.AddVariable() =>
        AddVariable();

    int ILoweredEmitterTarget.AddVariable(string name, Location loc, int data) =>
        AddVariable(name, loc, data);

    int ILoweredEmitterTarget.AddVariable(string name, Location loc, int data, int args) =>
        AddVariable(name, loc, data, args);

    void ILoweredEmitterTarget.RegisterIndexerDeclaration(LoweredFunctionDeclaration node) =>
        RegisterIndexerDeclaration(node);

    string ILoweredEmitterTarget.GetMethodName(string name, LoweredFunctionDeclaration node) =>
        GetMethodName(name, node);

    void ILoweredEmitterTarget.StartFunction(string name, string? typeName, Par[] parameters) =>
        StartFun(name, typeName, parameters);

    int ILoweredEmitterTarget.FinishFunctionLayout(int address)
    {
        var handle = unit.Layouts.Count;
        var stackSize = EndFun(handle);
        unit.Layouts.Add(new MemoryLayout(currentCounter, stackSize, address));
        return handle;
    }

    void ILoweredEmitterTarget.StartSection() =>
        StartSection();

    void ILoweredEmitterTarget.EndSection() =>
        EndSection();

    void ILoweredEmitterTarget.GenerateConstructor(LoweredFunctionDeclaration node, CompilerContext ctx) =>
        GenerateConstructor(node, ctx);

    int ILoweredEmitterTarget.RegisterNominalDeclaration(LoweredNominalDeclaration node) =>
        RegisterNominalDeclaration(node);

    void ILoweredEmitterTarget.ValidateTraitContracts(LoweredNominalDeclaration node) =>
        ValidateTraitContracts(node);

    bool ILoweredEmitterTarget.TryGetTypeInfo(string name, out TypeInfo typeInfo) =>
        types.TryGetValue(name, out typeInfo!);

    LoweredImportResolution ILoweredEmitterTarget.LinkImport(LoweredImport node)
    {
        var reference = CreateImportReference(node);
        var result = linker.Link(unit, reference);

        if (!result.Success || result.Value is null)
        {
            AddError(CompilerError.UnableToLinkModule, node.Location, node.ModuleName);
            throw new TerminationException();
        }

        var moduleIndex = AddUnitReference(reference, result.Value);
        return new(moduleIndex, result.Value);
    }

    bool ILoweredEmitterTarget.TryResolveModuleMember(string moduleName, string memberName, out ScopeVar symbol)
    {
        symbol = default;
        var error = GetVariable(moduleName, out var moduleSymbol);

        if (error is not CompilerError.None
            || (moduleSymbol.Data & VarFlags.Module) != VarFlags.Module
            || !referencedUnits.TryGetValue(moduleName, out var unitInfo)
            || !unitInfo.ExportList.TryGetValue(memberName, out var exportedSymbol))
        {
            return false;
        }

        symbol = new ScopeVar(
            unitInfo.Handle | (exportedSymbol.Address >> 8) << 8,
            VarFlags.External | exportedSymbol.Data);
        return true;
    }

    bool ILoweredEmitterTarget.TryAddReferencedUnit(string key, UnitInfo unitInfo)
    {
        if (referencedUnits.ContainsKey(key))
        {
            return false;
        }

        referencedUnits.Add(key, unitInfo);
        return true;
    }

    bool ILoweredEmitterTarget.TryAddImportedSymbol(string name, ImportedSymbol symbol)
    {
        if (importedSymbols.ContainsKey(name) || referencedUnits.ContainsKey(name))
        {
            return false;
        }

        importedSymbols.Add(name, symbol);
        return true;
    }

    bool ILoweredEmitterTarget.IsImportNameUsed(string name) =>
        importedSymbols.ContainsKey(name) || referencedUnits.ContainsKey(name);

    CompilerError ILoweredEmitterTarget.VariableExists(string name) =>
        VariableExists(name);

    CompilerError ILoweredEmitterTarget.GetVariable(string name, out ScopeVar symbol) =>
        GetVariable(name, out symbol);

    int ILoweredEmitterTarget.PushVariable(CompilerContext ctx, string name, Location loc) =>
        PushVariable(ctx, name, loc);

    void ILoweredEmitterTarget.PopVariable(CompilerContext ctx, string name, Location loc) =>
        PopVariable(ctx, name, loc);

    void ILoweredEmitterTarget.CallAutosForKind(ScopeKind kind) =>
        CallAutosForKind(kind);

    void ILoweredEmitterTarget.AddAutoClose(int address, string name) =>
        currentScope.Autos.Enqueue((address >> 8, name));

    bool ILoweredEmitterTarget.TryGetLocalVariable(string name, out ScopeVar var) =>
        TryGetLocalVariable(name, out var);

    int ILoweredEmitterTarget.PushTypeInfo(CompilerContext ctx, Qualident qual, Location loc) =>
        PushTypeInfo(ctx, qual, loc);

    LoweredBareEnumConstructorResolution ILoweredEmitterTarget.TryResolveBareEnumConstructor(
        string name,
        int arity,
        out LoweredBareEnumConstructor constructor)
    {
        var result = TryResolveBareEnumConstructor(name, arity, out var resolution);
        constructor = result == BareEnumConstructorResolution.NotFound
            ? default
            : new(resolution.TypeName, resolution.MemberName, resolution.CallRequired);

        return result switch
        {
            BareEnumConstructorResolution.Found => LoweredBareEnumConstructorResolution.Found,
            BareEnumConstructorResolution.Ambiguous => LoweredBareEnumConstructorResolution.Ambiguous,
            _ => LoweredBareEnumConstructorResolution.NotFound
        };
    }

    void ILoweredEmitterTarget.ThrowError(SpellkitError code) =>
        ThrowError(code);

    void ILoweredEmitterTarget.CallAutos(bool cls) =>
        CallAutos(cls);

    void ILoweredEmitterTarget.PreInitLocalFunctions(IReadOnlyList<LoweredNode> nodes) =>
        PreInitLocalFunctions(nodes);

    void ILoweredEmitterTarget.CheckPattern(LoweredPattern pattern, int matchCount) =>
        CheckPattern(pattern, matchCount);

    bool ILoweredEmitterTarget.TryResolveZeroArgConstructor(Qualident typeName, out Qualident resolvedType, out string constructorName) =>
        TryResolveZeroArgConstructor(typeName, out resolvedType, out constructorName);

    void ILoweredEmitterTarget.AddError(CompilerError error, Location loc, params object[] args) =>
        AddError(error, loc, args);

    void ILoweredEmitterTarget.AddLinePragma(Location loc) =>
        AddLinePragma(loc);
}

internal sealed class TerminationException : Exception { }

internal sealed class TypeInfo
{
    public TypeInfo(LoweredNominalDeclaration dec, UnitInfo unit) => (Declaration, Unit) = (dec, unit);

    public UnitInfo Unit { get; }

    public LoweredNominalDeclaration Declaration { get; }
}

internal record UnitInfo(int Handle, Dictionary<HashString, ScopeVar> ExportList);

internal record ImportedSymbol(int Handle, ScopeVar Variable);

internal sealed class BuilderDiagnostics
{
    private readonly BuilderOptions options;

    internal BuilderDiagnostics(BuilderOptions options) => this.options = options;

    internal List<BuildMessage> Messages { get; } = new();

    internal int ErrorCount { get; private set; }

    internal bool Success => ErrorCount == 0;

    internal void Clear()
    {
        Messages.Clear();
        ErrorCount = 0;
    }

    internal void AddError(CompilerError error, string fileName, Location loc, int errorLimit, params object[] args)
    {
        var str = MessageCatalog.Format(MessageGroup.Compiler, error.ToString(), args);
        AddMessage(new BuildMessage(str, BuildMessageType.Error, (int)error, loc.Line, loc.Column, fileName), errorLimit, fileName);
    }

    internal void AddWarning(CompilerWarning warning, string fileName, Location loc, params object[] args)
    {
        if (options.NoWarnings)
        {
            return;
        }

        if (options.IgnoreWarnings.Contains((int)warning))
        {
            return;
        }

        var str = MessageCatalog.Format(MessageGroup.Compiler, warning.ToString(), args);
        AddMessage(new BuildMessage(str, BuildMessageType.Warning, (int)warning, loc.Line, loc.Column, fileName), errorLimit: -1, fileName);
    }

    private void AddMessage(BuildMessage msg, int errorLimit, string fileName)
    {
        Messages.Add(msg);

        if (msg.Type != BuildMessageType.Error)
        {
            return;
        }

        ErrorCount++;

        if (ErrorCount >= errorLimit)
        {
            Messages.Add(new BuildMessage(MessageCatalog.Get(MessageGroup.Compiler, nameof(CompilerError.TooManyErrors)), BuildMessageType.Error,
                (int)CompilerError.TooManyErrors, msg.Line, msg.Column, fileName));
            throw new TerminationException();
        }
    }
}

partial class Builder
{
    private static bool HasAuto(IReadOnlyList<SyntaxNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is BindingSyntax { AutoClose: true })
            {
                return true;
            }
        }

        return false;
    }

    private void PreInitLocalFunctions(IReadOnlyList<LoweredNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is not LoweredFunctionDeclaration func || !CanPreInitFunction(func))
            {
                continue;
            }

            var flags = VarFlags.PreInit | VarFlags.Const | VarFlags.Function;
            if (func.IsPrivate)
            {
                flags |= VarFlags.Private;
            }

            AddVariable(func.Name!, func.Location, flags);
        }
    }

    private static bool CanPreInitFunction(LoweredFunctionDeclaration func) =>
        func.Name is not null
        && func.TypeName is null
        && func.TargetTypeName is null
        && !func.IsStatic
        && !func.Getter
        && !func.Setter
        && !func.IsConstructor;

    private void EmitBinaryOp(BinaryOperator op)
    {
        switch (op)
        {
            case BinaryOperator.Add: cw.Add(); break;
            case BinaryOperator.Sub: cw.Sub(); break;
            case BinaryOperator.Mul: cw.Mul(); break;
            case BinaryOperator.Div: cw.Div(); break;
            case BinaryOperator.Rem: cw.Remainder(); break;
            case BinaryOperator.Eq: cw.Equal(); break;
            case BinaryOperator.NotEq: cw.NotEqual(); break;
            case BinaryOperator.Gt: cw.GreaterThan(); break;
            case BinaryOperator.Lt: cw.LessThan(); break;
            case BinaryOperator.GtEq: cw.GreaterThanOrEqual(); break;
            case BinaryOperator.LtEq: cw.LessThanOrEqual(); break;
            default: throw Ice();
        }
    }

    private void ThrowErrorProlog(SpellkitError code, int parameters)
    {
        cw.LoadType(SpellkitTypeCodes.Exception);
        cw.LoadMember(code.ToString());
        cw.PrepareCall(parameters);
    }

    private void ThrowError(SpellkitError code)
    {
        ThrowErrorProlog(code, 0);
        cw.InvokePreparedCall(0);
    }
}

partial class Builder
{
    private LoweringPass CreateLoweringPass() =>
        new(
            block => HasAuto(block.Nodes),
            options.NoOptimizations);

    private LoweredEmitter CreateLoweredEmitter() =>
        new(this);
}
