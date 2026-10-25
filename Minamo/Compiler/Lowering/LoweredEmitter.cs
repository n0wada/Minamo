using Minamo.Debug;
using Minamo.Parser;
using Minamo.Parser.Model;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System;
using System.Collections.Generic;

namespace Minamo.Compiler.Lowering;

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

    void RegisterMixin(string targetName, Qualident mixin, Location loc);

    void RegisterInstanceMethod(Qualident typeName, string memberName);

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

    void ThrowError(MinamoError code);

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

    public void Emit(LoweredNode node, bool keepResult, CompilerContext ctx) =>
        EmitNode(node, keepResult, ctx);

    public void Emit(LoweredImport node) =>
        EmitImport(node);

    public void Emit(LoweredBlock node, bool keepResult, CompilerContext ctx) =>
        EmitBlock(node, keepResult, ctx);

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
