using Spellkit.Debug;
using Spellkit.Parser;
using Spellkit.Parser.Model;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System;
using System.Collections.Generic;

namespace Spellkit.Compiler.Lowering;

internal interface ILoweredEmitterTarget
{
    StackMachineEmitter Code { get; }

    int ErrorCount { get; }

    bool NoOptimizations { get; }

    int SectionDepth { get; }

    bool IsGlobalScope { get; }

    void EmitBinaryOp(BinaryOperator op);

    void StoreName(string name, Location loc, CompilerContext ctx);

    void StartScope(ScopeKind kind, Location loc);

    void EndScope();

    int AddVariable();

    int AddVariable(string name, Location loc, int data);

    int AddVariable(string name, Location loc, int data, int args);

    void RegisterIndexerDeclaration(LoweredFunctionDeclaration node);

    string GetMethodName(string name, LoweredFunctionDeclaration node);

    void StartFunction(string name, string? typeName, Par[] parameters);

    int FinishFunctionLayout(int address);

    void StartSection();

    void EndSection();

    void GenerateConstructor(LoweredFunctionDeclaration node, CompilerContext ctx);

    int RegisterNominalDeclaration(LoweredNominalDeclaration node);

    void ValidateTraitContracts(LoweredNominalDeclaration node);

    bool TryGetTypeInfo(string name, out TypeInfo typeInfo);

    LoweredImportResolution LinkImport(LoweredImport node);

    bool TryResolveModuleMember(string moduleName, string memberName, out ScopeVar symbol);

    bool TryAddReferencedUnit(string key, UnitInfo unitInfo);

    bool TryAddImportedSymbol(string name, ImportedSymbol symbol);

    bool IsImportNameUsed(string name);

    CompilerError VariableExists(string name);

    CompilerError GetVariable(string name, out ScopeVar symbol);

    int PushVariable(CompilerContext ctx, string name, Location loc);

    void PopVariable(CompilerContext ctx, string name, Location loc);

    void CallAutosForKind(ScopeKind kind);

    void AddAutoClose(int address, string name);

    bool TryGetLocalVariable(string name, out ScopeVar var);

    int PushTypeInfo(CompilerContext ctx, Qualident qual, Location loc);

    LoweredBareEnumConstructorResolution TryResolveBareEnumConstructor(
        string name,
        int arity,
        out LoweredBareEnumConstructor constructor);

    void ThrowError(SpkError code);

    void CallAutos(bool cls = false);

    void PreInitLocalFunctions(IReadOnlyList<LoweredNode> nodes);

    void CheckPattern(LoweredPattern pattern, int matchCount);

    bool TryResolveZeroArgConstructor(Qualident typeName, out Qualident resolvedType, out string constructorName);

    void AddError(CompilerError error, Location loc, params object[] args);

    void AddLinePragma(Location loc);
}

internal sealed partial class LoweredEmitter
{
    private readonly ILoweredEmitterTarget target;
    private readonly Dictionary<int, Label> labels = new();

#if DEBUG
    static LoweredEmitter() => VerifyDispatchCoverage();
#endif

    public LoweredEmitter(ILoweredEmitterTarget target) =>
        this.target = target;

    public void Emit(LoweredAssignment node, bool keepResult, CompilerContext ctx) =>
        EmitAssignment(node, keepResult, ctx);

    public void Emit(LoweredNode node, bool keepResult, CompilerContext ctx) =>
        EmitNode(node, keepResult, ctx);

    public void Emit(LoweredAccess node, bool keepResult, CompilerContext ctx) =>
        EmitAccess(node, keepResult, ctx);

    public void Emit(LoweredIndexer node, bool keepResult, CompilerContext ctx) =>
        EmitIndexer(node, keepResult, ctx);

    public void Emit(LoweredFunctionDeclaration node, CompilerContext ctx) =>
        EmitFunctionDeclaration(node, ctx);

    public void Emit(LoweredSelectDeclaration node, bool keepResult, CompilerContext ctx) =>
        EmitSelectDeclaration(node, keepResult, ctx);

    public void Emit(LoweredSelectInvocation node, bool keepResult, CompilerContext ctx) =>
        EmitSelectInvocation(node, keepResult, ctx);

    public void Emit(LoweredNominalDeclaration node, CompilerContext ctx) =>
        EmitNominalDeclaration(node, ctx);

    public void Emit(LoweredImplDeclaration node, CompilerContext ctx) =>
        EmitImplDeclaration(node, ctx);

    public void Emit(LoweredImport node) =>
        EmitImport(node);

    public void Emit(LoweredIf node, bool keepResult, CompilerContext ctx) =>
        EmitIf(node, keepResult, ctx);

    public void Emit(LoweredBlock node, bool keepResult, CompilerContext ctx) =>
        EmitBlock(node, keepResult, ctx);

    public void Emit(LoweredMatch node, bool keepResult, CompilerContext ctx) =>
        EmitMatch(node, keepResult, ctx);

    public void Emit(LoweredTryCatch node, bool keepResult, CompilerContext ctx) =>
        EmitTryCatch(node, keepResult, ctx);

    public void Emit(LoweredWhile node, bool keepResult, CompilerContext ctx) =>
        EmitWhile(node, keepResult, ctx);

    public void Emit(LoweredFor node, bool keepResult, CompilerContext ctx) =>
        EmitFor(node, keepResult, ctx);

    public void Emit(LoweredExpressionStatement node, bool keepResult, CompilerContext ctx) =>
        EmitExpressionStatement(node, keepResult, ctx);

    public void Emit(LoweredConstDeclaration node, bool keepResult, CompilerContext ctx) =>
        EmitConstDeclaration(node, keepResult, ctx);

    public void Emit(LoweredBinding node, bool keepResult, CompilerContext ctx) =>
        EmitBinding(node, keepResult, ctx);

    public void Emit(LoweredRebinding node, bool keepResult, CompilerContext ctx) =>
        EmitRebinding(node, keepResult, ctx);

    public void Emit(LoweredUnary node, bool keepResult, CompilerContext ctx) =>
        EmitUnary(node, keepResult, ctx);

    public void Emit(LoweredBinary node, bool keepResult, CompilerContext ctx) =>
        EmitBinary(node, keepResult, ctx);

    public void Emit(LoweredName node, bool keepResult, CompilerContext ctx) =>
        EmitName(node, keepResult, ctx);

    public void Emit(LoweredApplication node, bool keepResult, CompilerContext ctx) =>
        EmitApplication(node, keepResult, ctx);

    public void Emit(LoweredControlTransfer node, bool keepResult, CompilerContext ctx) =>
        EmitControlTransfer(node, keepResult, ctx);

    public void Emit(LoweredYieldBlock node, bool keepResult, CompilerContext ctx) =>
        EmitYieldBlock(node, keepResult, ctx);

    public void Emit(LoweredCast node, bool keepResult, CompilerContext ctx) =>
        EmitCast(node, keepResult, ctx);

    public void Emit(LoweredRange node, bool keepResult, CompilerContext ctx) =>
        EmitRange(node, keepResult, ctx);

    public void Emit(LoweredTuple node, bool keepResult, CompilerContext ctx) =>
        EmitTuple(node, keepResult, ctx);

    public void Emit(LoweredArray node, bool keepResult, CompilerContext ctx) =>
        EmitArray(node, keepResult, ctx);

    public void Emit(LoweredComprehension node, bool keepResult, CompilerContext ctx) =>
        EmitComprehension(node, keepResult, ctx);

    public void Emit(LoweredLiteral node, bool keepResult) =>
        EmitLiteral(node, keepResult);

    public void EmitPattern(LoweredPattern node, CompilerContext ctx, bool rebind, bool openMatch, int flags = VarFlags.None) =>
        BuildPattern(node, ctx, rebind, openMatch, flags);

    public void PreinitPattern(LoweredPattern node, bool rebind, bool openMatch, int flags = VarFlags.None) =>
        PreinitLoweredPattern(node, rebind, openMatch, flags);

    private StackMachineEmitter cw => target.Code;

    private void EmitValue(LoweredNode node, CompilerContext ctx, bool tailPosition = false) =>
        EmitNode(node, keepResult: true, ctx.WithTailPosition(tailPosition));

    private void EmitEffect(LoweredNode node, CompilerContext ctx, bool tailPosition = false) =>
        EmitNode(node, keepResult: false, ctx.WithTailPosition(tailPosition));

    private void EmitNode(LoweredNode node, bool keepResult, CompilerContext ctx)
    {
        switch (node)
        {
            case LoweredAccess access:
                EmitAccess(access, keepResult, ctx);
                break;
            case LoweredApplication application:
                EmitApplication(application, keepResult, ctx);
                break;
            case LoweredArray array:
                EmitArray(array, keepResult, ctx);
                break;
            case LoweredAssignment assignment:
                EmitAssignment(assignment, keepResult, ctx);
                break;
            case LoweredBinary binary:
                EmitBinary(binary, keepResult, ctx);
                break;
            case LoweredBinding binding:
                EmitBinding(binding, keepResult, ctx);
                break;
            case LoweredBlock block:
                EmitBlock(block, keepResult, ctx);
                break;
            case LoweredCast cast:
                EmitCast(cast, keepResult, ctx);
                break;
            case LoweredComprehension comprehension:
                EmitComprehension(comprehension, keepResult, ctx);
                break;
            case LoweredConstDeclaration declaration:
                EmitConstDeclaration(declaration, keepResult, ctx);
                break;
            case LoweredControlTransfer control:
                EmitControlTransfer(control, keepResult, ctx);
                break;
            case LoweredExpressionStatement statement:
                EmitExpressionStatement(statement, keepResult, ctx);
                break;
            case LoweredFunctionDeclaration function:
                EmitFunctionDeclaration(function with { NeedsValue = keepResult }, ctx);
                break;
            case LoweredSelectDeclaration select:
                EmitSelectDeclaration(select, keepResult, ctx);
                break;
            case LoweredSelectInvocation selectInvocation:
                EmitSelectInvocation(selectInvocation, keepResult, ctx);
                break;
            case LoweredIf @if:
                EmitIf(@if, keepResult, ctx);
                break;
            case LoweredImplDeclaration impl:
                EmitImplDeclaration(impl with { NeedsValue = keepResult }, ctx);
                break;
            case LoweredImport import:
                EmitImport(import);
                break;
            case LoweredIndexer indexer:
                EmitIndexer(indexer, keepResult, ctx);
                break;
            case LoweredLabel label:
                EmitLabel(label, keepResult, ctx);
                break;
            case LoweredLiteral literal:
                EmitLiteral(literal, keepResult);
                break;
            case LoweredMatch match:
                EmitMatch(match, keepResult, ctx);
                break;
            case LoweredName name:
                EmitName(name, keepResult, ctx);
                break;
            case LoweredNominalDeclaration declaration:
                EmitNominalDeclaration(declaration with { NeedsValue = keepResult }, ctx);
                break;
            case LoweredRange range:
                EmitRange(range, keepResult, ctx);
                break;
            case LoweredRebinding rebinding:
                EmitRebinding(rebinding, keepResult, ctx);
                break;
            case LoweredTryCatch tryCatch:
                EmitTryCatch(tryCatch, keepResult, ctx);
                break;
            case LoweredTuple tuple:
                EmitTuple(tuple, keepResult, ctx);
                break;
            case LoweredUnary unary:
                EmitUnary(unary, keepResult, ctx);
                break;
            case LoweredWhile @while:
                EmitWhile(@while, keepResult, ctx);
                break;
            case LoweredFor @for:
                EmitFor(@for, keepResult, ctx);
                break;
            case LoweredYieldBlock yieldBlock:
                EmitYieldBlock(yieldBlock, keepResult, ctx);
                break;
            default:
                throw new InvalidOperationException($"Unsupported lowered node: {node.GetType().Name}.");
        }
    }

#if DEBUG
    private static void VerifyDispatchCoverage()
    {
        // Keep this list aligned with the cases in EmitNode.
        var supportedTypes = new HashSet<Type>
        {
            typeof(LoweredAccess),
            typeof(LoweredApplication),
            typeof(LoweredArray),
            typeof(LoweredAssignment),
            typeof(LoweredBinary),
            typeof(LoweredBinding),
            typeof(LoweredBlock),
            typeof(LoweredCast),
            typeof(LoweredComprehension),
            typeof(LoweredConstDeclaration),
            typeof(LoweredControlTransfer),
            typeof(LoweredExpressionStatement),
            typeof(LoweredFor),
            typeof(LoweredFunctionDeclaration),
            typeof(LoweredSelectDeclaration),
            typeof(LoweredSelectInvocation),
            typeof(LoweredIf),
            typeof(LoweredImplDeclaration),
            typeof(LoweredImport),
            typeof(LoweredIndexer),
            typeof(LoweredLabel),
            typeof(LoweredLiteral),
            typeof(LoweredMatch),
            typeof(LoweredName),
            typeof(LoweredNominalDeclaration),
            typeof(LoweredRange),
            typeof(LoweredRebinding),
            typeof(LoweredTryCatch),
            typeof(LoweredTuple),
            typeof(LoweredUnary),
            typeof(LoweredWhile),
            typeof(LoweredYieldBlock)
        };

        foreach (var type in typeof(LoweredNode).Assembly.GetTypes())
        {
            if (type.IsAbstract || !type.IsSubclassOf(typeof(LoweredNode)))
            {
                continue;
            }

            if (!supportedTypes.Remove(type))
            {
                throw new InvalidOperationException($"Lowered node is missing from emitter dispatch: {type.Name}.");
            }
        }

        if (supportedTypes.Count != 0)
        {
            throw new InvalidOperationException("Emitter dispatch contains a type that is not a concrete lowered node.");
        }
    }
#endif

}
