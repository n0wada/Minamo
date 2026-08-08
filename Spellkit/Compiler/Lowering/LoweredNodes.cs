using Spellkit.Parser;
using Spellkit.Parser.Model;
using System.Collections.Generic;

namespace Spellkit.Compiler.Lowering;

internal abstract record LoweredNode(Location Location);

internal sealed record LoweredModule(
    string FileName,
    IReadOnlyList<LoweredImport> Imports,
    IReadOnlyList<LoweredNode> Body);

internal enum LoweredAccessKind
{
    Unresolved,
    PublicMember,
    PrivateMember,
    IndexedMemberName,
    ModuleExport,
    ModuleType,
    LocalEnumConstant
}

internal sealed record LoweredAccess(
    Location Location,
    LoweredNode Target,
    string Name,
    LoweredAccessKind Kind,
    bool SpecialName = false,
    ScopeVar ModuleSymbol = default) : LoweredNode(Location);

internal enum LoweredIndexerKind
{
    Unresolved,
    Indexed,
    RawIndexed,
    Slice
}

internal sealed record LoweredIndexer(
    Location Location,
    LoweredNode Target,
    LoweredNode Index,
    LoweredIndexerKind Kind) : LoweredNode(Location);

internal enum LoweredStoreKind
{
    UnresolvedAccess,
    UnresolvedIndexer,
    Name,
    PublicMemberSetter,
    PrivateMember,
    IndexedMemberName,
    Indexed,
    RawIndexed,
    Invalid
}

internal sealed record LoweredStoreTarget(
    Location Location,
    LoweredNode Target,
    LoweredStoreKind Kind,
    string? Name = null,
    bool SpecialName = false);

internal enum LoweredAssignmentKind
{
    Simple,
    AutoAssign,
    CoalesceAssign,
    PublicMemberSetter,
    PublicMemberSetterAutoAssign,
    PublicMemberSetterCoalesceAssign
}

internal sealed record LoweredAssignment(
    Location Location,
    LoweredStoreTarget Target,
    LoweredNode Value,
    BinaryOperator? AutoAssign,
    LoweredAssignmentKind Kind) : LoweredNode(Location);

internal sealed record LoweredFunctionDeclaration(
    Location Location,
    Qualident? TypeName,
    Qualident? TargetTypeName,
    string? Name,
    bool IsStatic,
    bool IsIndexer,
    bool IsConstructor,
    bool Getter,
    bool Setter,
    bool IsIterator,
    bool IsImplInitializer,
    bool IsPrivate,
    IReadOnlyList<LoweredParameter> Parameters,
    LoweredNode? Body,
    bool NeedsValue,
    bool IteratorBody,
    bool IsStdCall) : LoweredNode(Location)
{
    public bool IsVariadic()
    {
        for (var i = 0; i < Parameters.Count; i++)
        {
            if (Parameters[i].IsVarArgs)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record LoweredSelectDeclaration(
    Location Location,
    string? Name,
    IReadOnlyList<LoweredBinding> Locals,
    IReadOnlyList<LoweredSelectState> States,
    bool IsInstanceFactory = false) : LoweredNode(Location);

internal sealed record LoweredSelectInvocation(
    Location Location,
    LoweredNode Target) : LoweredNode(Location);

internal sealed record LoweredSelectState(
    Location Location,
    string Name,
    bool IsInitial,
    IReadOnlyList<LoweredParameter> Parameters,
    LoweredNode? Enter,
    LoweredNode? Leave,
    LoweredNode? Otherwise,
    IReadOnlyList<LoweredSelectChoice> Choices,
    IReadOnlyList<LoweredSelectEvent> Events);

internal sealed record LoweredSelectChoice(
    Location Location,
    string Name,
    IReadOnlyList<LoweredParameter> Parameters,
    string Label,
    string? Description,
    LoweredNode? Guard,
    LoweredNode Body);

internal sealed record LoweredSelectEvent(
    Location Location,
    string Name,
    IReadOnlyList<LoweredParameter> Parameters,
    LoweredNode Body);

internal sealed record LoweredParameter(
    Location Location,
    string Name,
    LoweredLiteral? DefaultValue,
    bool HasDefaultValue,
    Location DefaultValueLocation,
    TypeAnnotation? TypeAnnotation,
    bool IsVarArgs,
    bool Mutable);

internal sealed record LoweredField(
    Location Location,
    string? Name,
    bool Constant,
    bool AutoClose,
    LoweredNode? Init);

internal sealed record LoweredNominalDeclaration(
    Location Location,
    TypeDeclarationStyle Style,
    string Name,
    bool IsPrivate,
    List<LoweredFunctionDeclaration> Constructors,
    List<LoweredFunctionDeclaration> Contracts,
    IReadOnlyList<Qualident> Mixins,
    List<LoweredField> PrivateFields,
    List<LoweredFunctionDeclaration> ProtectedMethods,
    LoweredFunctionDeclaration? InitializerValue,
    bool NeedsValue,
    bool AutoLookup) : LoweredNode(Location)
{
    public LoweredFunctionDeclaration? Initializer { get; set; } = InitializerValue;
}

internal sealed record LoweredImplDeclaration(
    Location Location,
    string TargetName,
    IReadOnlyList<Qualident> Mixins,
    IReadOnlyList<LoweredImplMember> Members,
    bool NeedsValue) : LoweredNode(Location);

internal abstract record LoweredImplMember(Location Location);

internal sealed record LoweredImplFunction(
    LoweredFunctionDeclaration Function) : LoweredImplMember(Function.Location);

internal sealed record LoweredImplField(
    LoweredField Field) : LoweredImplMember(Field.Location);

internal sealed record LoweredInvalidImplMember(
    Location Location) : LoweredImplMember(Location);

internal sealed record LoweredImport(
    Location Location,
    ImportKind Kind,
    string? Alias,
    string? SymbolName,
    string ModuleName,
    string? LocalPath) : LoweredNode(Location);

internal sealed record LoweredImportResolution(
    int ModuleIndex,
    Unit LinkedUnit);

internal enum LoweredBareEnumConstructorResolution
{
    NotFound,
    Found,
    Ambiguous
}

internal readonly record struct LoweredBareEnumConstructor(
    Qualident TypeName,
    string MemberName,
    bool CallRequired);

internal sealed record LoweredExpressionStatement(
    Location Location,
    LoweredNode Expression) : LoweredNode(Location);

internal sealed record LoweredConstDeclaration(
    Location Location,
    bool IsPrivate,
    IReadOnlyList<LoweredBinding> Declarations) : LoweredNode(Location);

internal sealed record LoweredBinding(
    Location Location,
    LoweredPattern Pattern,
    LoweredNode? Init,
    bool Constant,
    bool AutoClose,
    bool IsPrivate,
    bool SimpleNameBinding,
    int InitNodeCount) : LoweredNode(Location);

internal sealed record LoweredRebinding(
    Location Location,
    LoweredPattern Pattern,
    LoweredNode Init) : LoweredNode(Location);

internal sealed record LoweredUnary(
    Location Location,
    LoweredNode Operand,
    UnaryOperator Operator) : LoweredNode(Location);

internal sealed record LoweredBinary(
    Location Location,
    LoweredNode Left,
    LoweredNode? Right,
    BinaryOperator Operator,
    LoweredPattern? Pattern = null) : LoweredNode(Location);

internal sealed record LoweredName(
    Location Location,
    string Value) : LoweredNode(Location);

internal sealed record LoweredApplication(
    Location Location,
    LoweredNode Target,
    IReadOnlyList<LoweredNode> Arguments) : LoweredNode(Location);

internal sealed record LoweredControlTransfer(
    Location Location,
    LoweredNode? Expression,
    LoweredControlTransferKind Kind,
    string? SelectState = null,
    IReadOnlyList<LoweredNode>? Arguments = null) : LoweredNode(Location);

internal enum LoweredControlTransferKind
{
    Break,
    Continue,
    Return,
    Goto,
    Throw,
    Yield,
    YieldBreak
}

internal sealed record LoweredYieldBlock(
    Location Location,
    IReadOnlyList<LoweredNode> Elements) : LoweredNode(Location);

internal sealed record LoweredCast(
    Location Location,
    LoweredNode Expression,
    Qualident TypeName) : LoweredNode(Location);

internal sealed record LoweredRange(
    Location Location,
    LoweredNode? From,
    LoweredNode? To,
    bool Exclusive) : LoweredNode(Location);

internal sealed record LoweredTuple(
    Location Location,
    IReadOnlyList<LoweredNode> Elements) : LoweredNode(Location);

internal sealed record LoweredArray(
    Location Location,
    IReadOnlyList<LoweredNode> Elements) : LoweredNode(Location);

internal sealed record LoweredComprehension(
    Location Location,
    LoweredPattern Pattern,
    LoweredNode Target,
    LoweredNode? Guard,
    LoweredNode? Key,
    LoweredNode Value,
    bool IsDictionary,
    bool CanUseSimpleNameBinding,
    LoweredLoopLabels Labels) : LoweredNode(Location);

internal enum LoweredLiteralKind
{
    String,
    Char,
    Float,
    Integer,
    Boolean,
    Nil
}

internal sealed record LoweredLiteral(
    Location Location,
    object? Value,
    LoweredLiteralKind Kind) : LoweredNode(Location);

internal sealed record LoweredIf(
    Location Location,
    LoweredNode Condition,
    LoweredNode TrueBranch,
    LoweredNode? FalseBranch,
    bool RequiresElseResult,
    bool TrueBranchIsBindingGuard) : LoweredNode(Location);

internal sealed record LoweredBlock(
    Location Location,
    IReadOnlyList<LoweredNode> Nodes,
    bool HasAutoClose,
    bool NoScope) : LoweredNode(Location);

internal sealed record LoweredMatch(
    Location Location,
    LoweredNode? Subject,
    IReadOnlyList<LoweredMatchEntry> Entries,
    bool HasSubject) : LoweredNode(Location);

internal sealed record LoweredMatchEntry(
    Location Location,
    LoweredPattern Pattern,
    LoweredNode? Guard,
    LoweredNode Expression);

internal sealed record LoweredLabel(
    Location Location,
    string Label,
    LoweredNode Expression,
    bool Mutable,
    bool FromString) : LoweredNode(Location);

internal enum LoweredPatternKind
{
    Not,
    Name,
    Literal,
    Tuple,
    Array,
    Nil,
    Range,
    Wildcard,
    TypeTest,
    And,
    Or,
    Constructor
}

internal sealed record LoweredPattern(
    Location Location,
    LoweredPatternKind Kind,
    IReadOnlyList<LoweredPattern> Children,
    string? Name = null,
    object? Literal = null,
    LoweredNode? LiteralExpression = null,
    Qualident? TypeName = null,
    string? Constructor = null,
    bool RequiresExactLength = false,
    bool AllowTypeCheck = false);

internal enum LoweredTryKind
{
    TryCatch,
    TryFinally,
    TryCatchFinally
}

internal readonly record struct LoweredLabelId(int Value);

internal sealed record LoweredTryLabels(
    LoweredLabelId Gotcha,
    LoweredLabelId Skip,
    LoweredLabelId RunFinally,
    LoweredLabelId RethrowFinally,
    LoweredLabelId Exit);

internal sealed record LoweredTryCatch(
    Location Location,
    LoweredNode TryBody,
    LoweredNode? CatchBody,
    string? BindVariable,
    Location BindVariableLocation,
    LoweredNode? FinallyBody,
    LoweredTryKind Kind,
    LoweredTryLabels Labels) : LoweredNode(Location);

internal sealed record LoweredLoopLabels(
    LoweredLabelId Continue,
    LoweredLabelId Exit,
    LoweredLabelId BreakExit,
    LoweredLabelId Iteration,
    LoweredLabelId InitSkip,
    LoweredLabelId ElseSkip);

internal sealed record LoweredWhile(
    Location Location,
    LoweredNode Condition,
    LoweredNode Body,
    bool IsDoWhile,
    LoweredLoopLabels Labels) : LoweredNode(Location);

internal sealed record LoweredFor(
    Location Location,
    LoweredPattern Pattern,
    LoweredNode Target,
    LoweredNode? Guard,
    LoweredNode Body,
    LoweredNode? Else,
    bool CanUseSimpleNameBinding,
    LoweredLoopLabels Labels) : LoweredNode(Location);
