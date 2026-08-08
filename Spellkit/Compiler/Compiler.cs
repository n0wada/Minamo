using System.Collections.Generic;
using Spellkit.Linker;
using Spellkit.Parser.Model;

namespace Spellkit.Compiler;

public sealed class BuilderOptions
{
    public HashSet<int> IgnoreWarnings { get; } = new();

    public bool Debug { get; set; }

    public bool NoLangModule { get; set; }

    public bool NoWarnings { get; set; }

    public bool NoWarningsLinker { get; set; }

    public bool NoOptimizations { get; set; }

    public bool LinkerSkipChecksum { get; set; }

    public string? LinkerLog { get; set; }

    internal IModuleProvider? ModuleProvider { get; set; }

    internal bool AllowEnvironmentNames { get; set; }

    internal bool ExposeHostObject { get; set; }

    public static BuilderOptions Default() =>
        new()
        {
            Debug = false,
            NoLangModule = false,
            NoWarnings = false,
            NoWarningsLinker = false,
            LinkerSkipChecksum = false,
            AllowEnvironmentNames = false,
            ExposeHostObject = true
        };
}

internal sealed class SpellkitCompilerEngine
{
    private readonly BuilderOptions options;
    private readonly SpellkitLinker linker;
    private readonly Builder builder;

    public SpellkitCompilerEngine(BuilderOptions options, SpellkitLinker linker)
    {
        this.options = options ?? BuilderOptions.Default();
        this.linker = linker;
        builder = new(this.options, linker);
    }

    public SpellkitCompilerEngine(SpellkitCompilerEngine compiler)
    {
        options = compiler.options;
        linker = compiler.linker;
        builder = new(compiler.builder);
    }

    public Result<Unit> Compile(SpellkitCodeModel codeModel)
    {
        var unit = builder.Build(codeModel);
        return Result.Create(unit, builder.Messages);
    }
}

public enum CompilerError
{
    None = 0,

    TooManyErrors = 201,

    VariableAlreadyDeclared = 202,

    UndefinedVariable = 203,

    UnableAssignExpression = 204,

    UnableAssignConstant = 205,

    NoEnclosingLoop = 206,

    UndefinedType = 207,

    UndefinedModule = 208,

    ReturnNotAllowed = 209,

    NestedMethod = 210,

    ExpressionNoName = 211,

    PrivateNameAccess = 212,

    StaticOnlyMethods = 215,

    ReturnInIterator = 216,

    VarArgNoDefaultValue = 219,

    VarArgOnlyOne = 220,

    InvalidDefaultValue = 221,

    PatternNotSupported = 222,

    SliceNotSupported = 223,

    NamedArgumentMultipleTimes = 224,

    OverrideNotAllowed = 225,

    TypeAlreadyDeclared = 226,

    MemberNameCamel = 229,

    TypesOnlyGlobalScope = 231,

    UnableToLinkModule = 232,

    BindingPatternNoInit = 233,

    InvalidLabel = 234,

    InvalidSlice = 237,

    InvalidRethrow = 238,

    LabelOnlyCamel = 239,

    PositionalArgumentAfterKeyword = 240,

    IndexerStatic = 241,

    IndexerWrongArguments = 242,

    IndexerSetOrGet = 243,

    TypeNameCamel = 244,

    AccessorOnlyMethod = 245,

    AutoNotAllowed = 246,

    InvalidTypeDefaultValue = 247,

    GuardOnBinding = 248,

    MethodNotRecursive = 249,

    DuplicateModuleAlias = 250,

    InvalidCast = 252,

    YieldNotAllowed = 253,

    BoolCastNotAllowed = 254,

    SelfCastNotAllowed = 255,

    InvalidFunctionArgument = 256,

    BuiltinWrongArguments = 257,

    SetterWrongArguments = 258,

    GetterWrongArguments = 259,

    InvalidMixin = 260,

    MixinAlreadySpecified = 261,

    MixinSameAsType = 262,

    DuplicateLabel = 263,

    InvalidDictionary = 265,

    MethodNested = 267,

    ConstOnlyGlobalScope = 270,

    ImplOnlyGlobalScope = 271,

    ImplStructOnly = 272,

    InvalidImplMember = 273,

    LookupImplicitForStructAndEnum = 275,

    InvalidImplField = 276,

    InvalidImplInit = 277,

    AmbiguousEnumConstructor = 278,

    SelectRequiresState = 286,

    SelectRequiresOneInitialState = 287,

    SelectDuplicateState = 288,

    SelectDuplicateChoice = 289,

    SelectOnlyGlobalScope = 290,

    SelectStateNotFound = 291,

    SelectDuplicateEvent = 292,

    SelectStateParameterCount = 293,

    IfExpressionRequiresElse = 279,

    TraitMixinNotAllowed = 280,

    PublicImplMemberNotInTrait = 281,

    TraitMemberConflict = 282,

    DllImportNotAllowed = 283,

    DuplicateIndexer = 284,

    TypePatternOnlyInIs = 285
}

public enum CompilerWarning
{
    UserWarning = 300,

    FunctionDeprecated = 301,

    UnreachableMatchEntry = 302,

    PatternNeverMatch = 303,

    AssignmentSameVariable = 304
}
