namespace Spellkit.Compiler;

internal static class Builtins
{
    private const string SetAccessor = "set_";
    private const string GetAccessor = "get_";

    public const string OperatorSymbols = "?:+-*/%<>=!";
    public const string Add = "op_add";
    public const string Sub = "op_sub";
    public const string Mul = "op_mul";
    public const string Div = "op_div";
    public const string Rem = "op_rem";
    public const string Eq = "op_eq";
    public const string Neq = "op_neq";
    public const string Gt = "op_gt";
    public const string Lt = "op_lt";
    public const string Gte = "op_gte";
    public const string Lte = "op_lte";
    public const string Neg = "op_negate";
    public const string Plus = "op_plus";
    public const string Not = "op_not";
    public const string Get = "op_get";
    public const string Set = "op_set";
    public const string Length = "Length";
    public const string String = "ToString";
    public const string ToTuple = "ToTuple";
    public const string ToArray = "ToArray";
    public const string Iterate = "Iterate";
    public const string Clone = "Clone";
    public const string Has = "Has";
    public const string Type = "GetType";
    public const string Call = "Call";
    public const string Range = "Range";
    public const string Slice = "Slice";
    public const string Dispose = "Dispose";
    public const string In = "In";
    public const string DelMember = "DeleteMember";
    public const string ShiftLeft = "op_shift_left";
    public const string ShiftRight = "op_shift_right";

    public static string Setter(string name) => $"{SetAccessor}{name}";

    public static string Getter(string name) => $"{GetAccessor}{name}";

    public static bool IsSetter(string name) => name.StartsWith(SetAccessor);

    public static string GetSetterName(string name) => name[SetAccessor.Length..];

    public static bool IsSetter(HashString name) => ((string)name).StartsWith(SetAccessor);

    public static string GetSetterName(HashString name) => ((string)name)[SetAccessor.Length..];

    public static string NameToOperator(string op) =>
        op switch
        {
            Add => "+",
            Sub => "-",
            Mul => "*",
            Div => "/",
            Rem => "%",
            Eq => "==",
            Neq => "!=",
            Gt => ">",
            Lt => "<",
            Gte => ">=",
            Lte => "<=",
            Not => "!",
            Neg => "- (unary)",
            Plus => "+ (unary)",
            Get => "get",
            Set => "set",
            ShiftLeft => "<<",
            ShiftRight => ">>",
            _ => op
        };

    public static string OperatorToName(string name) =>
        name switch
        {
            "+" => Builtins.Add,
            "-" => Builtins.Sub,
            "*" => Builtins.Mul,
            "/" => Builtins.Div,
            "%" => Builtins.Rem,
            "==" => Builtins.Eq,
            "!=" => Builtins.Neq,
            ">" => Builtins.Gt,
            "<" => Builtins.Lt,
            ">=" => Builtins.Gte,
            "<=" => Builtins.Lte,
            "!" => Builtins.Not,
            "<<" => Builtins.ShiftLeft,
            ">>" => Builtins.ShiftRight,
            _ => name
        };
}

public enum OpCode
{
    NoOperation = 0,    //0
    ConvertToString,        //0
    LoadThis,   //+1
    Drop,        //-1
    LoadNil,    //+1
    LoadFalse,   //+1
    LoadTrue,   //+1
    LoadInt0,   //+1
    LoadInt1,   //+1
    LoadFloat0,   //+1
    LoadFloat1,   //+1
    LoadConst,  //+1
    Jump,       //0
    JumpIfTrue, //-1
    JumpIfFalse,//-1
    Add,        //-1
    Sub,        //-1
    Mul,        //-1
    Div,        //-1
    Remainder,        //-1
    Negate,        //0
    Plus,       //0
    Not,        //0
    Length,        //0
    GreaterThan,         //-1
    LessThan,         //-1
    Equal,         //-1
    NotEqual,      //-1
    GreaterThanOrEqual,       //-1
    LessThanOrEqual,       //-1
    LoadLocal,  //1
    LoadCaptured, //1
    LoadExternal, //1
    LoadEnvironment, //1
    StoreLocal, //-1
    StoreCaptured, //-1
    Return,        //0
    Duplicate,        //1
    Throw,      //-1
    CreateFunction, //0
    CreateVariadicFunction, //0
    CreateIterator, //0
    StoreMember,//-2
    LoadMember, //-1
    HasMember,  //-1
    StoreStaticMember, //-2
    LoadIndex,  //-1
    StoreIndex, //-3
    LoadRawIndex, //-1
    StoreRawIndex, //-3
    LoadModule,     //+1
    LoadType,       //+1
    CreateLabel,        //0
    FinishModule,       //0
    Yield,      //-1
    LoadTerminator, //1
    JumpIfTerminator, //0
    JumpIfIteratorValue,   //0
    PrepareCall,    //0
    SetCallArgument,   //-1
    SetNamedCallArgument,   //-1
    InvokePreparedCall,    //0
    Call0,      //0
    Call1,      //-1
    Call,       //Dynamic
    CallMember, //Dynamic
    CallStatic, //Dynamic
    CreateTuple, //Dynamic
    CheckContains,   //0
    CheckType,  //-2
    EnterTry,   //0
    LeaveTry,   //0
    CreateObject, //1
    CreateType, //1
    CheckConstructor,  //0
    CheckNull,     //0
    LoadIteratorFunction, //0
    MarkMutable,        //0
    AddTypeAnnotation,      //0
    SetFunctionAttribute,    //0
    CreateCast,    //-3
    ApplyCast,       //0
    ApplyMixin,      //-2
    TailCall0,  //0
    TailCall1,  //-1
    TailCall,   //Dynamic
    EndIterator,//0
    Debug,      //0
    CreateArguments, //Dynamic
    CreateDictionary, //Dynamic
    LoadPrivateMember, //0
    StorePrivateMember, //-1
    Suspend, //0
    SuspendSelect, //0
}

public enum OperandShape
{
    None,
    One,
    Two
}

public enum OpCategory
{
    Infrastructure,
    LoadStore,
    ControlFlow,
    Binary,
    Unary,
    Function,
    Member,
    Index,
    Module,
    Iterator,
    Call,
    Collection,
    Type,
    Exception,
    Conversion,
    Metadata,
    Cast
}

public readonly record struct OpInfo(
    OpCode Code,
    OperandShape Operands,
    int? StackDelta,
    OpCategory Category);

public sealed class Op
{
    public static readonly Op NoOperation = new(OpCode.NoOperation);
    public static readonly Op ConvertToString = new(OpCode.ConvertToString);
    public static readonly Op LoadIndex = new(OpCode.LoadIndex);
    public static readonly Op StoreIndex = new(OpCode.StoreIndex);
    public static readonly Op LoadRawIndex = new(OpCode.LoadRawIndex);
    public static readonly Op StoreRawIndex = new(OpCode.StoreRawIndex);
    public static readonly Op LoadThis = new(OpCode.LoadThis);
    public static readonly Op LoadType = new(OpCode.LoadType);
    public static readonly Op Drop = new(OpCode.Drop);
    public static readonly Op LoadNil = new(OpCode.LoadNil);
    public static readonly Op LoadFalse = new(OpCode.LoadFalse);
    public static readonly Op LoadTrue = new(OpCode.LoadTrue);
    public static readonly Op LoadInt0 = new(OpCode.LoadInt0);
    public static readonly Op LoadInt1 = new(OpCode.LoadInt1);
    public static readonly Op LoadFloat0 = new(OpCode.LoadFloat0);
    public static readonly Op LoadFloat1 = new(OpCode.LoadFloat1);
    public static readonly Op Add = new(OpCode.Add);
    public static readonly Op Sub = new(OpCode.Sub);
    public static readonly Op Mul = new(OpCode.Mul);
    public static readonly Op Div = new(OpCode.Div);
    public static readonly Op Remainder = new(OpCode.Remainder);
    public static readonly Op Negate = new(OpCode.Negate);
    public static readonly Op Plus = new(OpCode.Plus);
    public static readonly Op Not = new(OpCode.Not);
    public static readonly Op Length = new(OpCode.Length);
    public static readonly Op GreaterThan = new(OpCode.GreaterThan);
    public static readonly Op LessThan = new(OpCode.LessThan);
    public static readonly Op Equal = new(OpCode.Equal);
    public static readonly Op NotEqual = new(OpCode.NotEqual);
    public static readonly Op GreaterThanOrEqual = new(OpCode.GreaterThanOrEqual);
    public static readonly Op LessThanOrEqual = new(OpCode.LessThanOrEqual);
    public static readonly Op Return = new(OpCode.Return);
    public static readonly Op Duplicate = new(OpCode.Duplicate);
    public static readonly Op Throw = new(OpCode.Throw);
    public static readonly Op FinishModule = new(OpCode.FinishModule);
    public static readonly Op Yield = new(OpCode.Yield);
    public static readonly Op EndIterator = new(OpCode.EndIterator);
    public static readonly Op Call0 = new(OpCode.Call0);
    public static readonly Op Call1 = new(OpCode.Call1);
    public static readonly Op TailCall0 = new(OpCode.TailCall0);
    public static readonly Op TailCall1 = new(OpCode.TailCall1);
    public static readonly Op LoadTerminator = new(OpCode.LoadTerminator);
    public static readonly Op LeaveTry = new(OpCode.LeaveTry);
    public static readonly Op CheckNull = new(OpCode.CheckNull);
    public static readonly Op LoadIteratorFunction = new(OpCode.LoadIteratorFunction);
    public static readonly Op MarkMutable = new(OpCode.MarkMutable);
    public static readonly Op AddTypeAnnotation = new(OpCode.AddTypeAnnotation);
    public static readonly Op CheckType = new(OpCode.CheckType);
    public static readonly Op CreateCast = new(OpCode.CreateCast);
    public static readonly Op ApplyCast = new(OpCode.ApplyCast);
    public static readonly Op ApplyMixin = new(OpCode.ApplyMixin);
    public static readonly Op Debug = new(OpCode.Debug);
    public static readonly Op Suspend = new(OpCode.Suspend);
    public static readonly Op SuspendSelect = new(OpCode.SuspendSelect);

    internal static readonly OpInfo[] Infos =
    {
        new(OpCode.NoOperation, OperandShape.None, 0, OpCategory.Infrastructure),
        new(OpCode.ConvertToString, OperandShape.None, 0, OpCategory.Conversion),
        new(OpCode.LoadThis, OperandShape.None, 1, OpCategory.LoadStore),
        new(OpCode.Drop, OperandShape.None, -1, OpCategory.LoadStore),
        new(OpCode.LoadNil, OperandShape.None, 1, OpCategory.LoadStore),
        new(OpCode.LoadFalse, OperandShape.None, 1, OpCategory.LoadStore),
        new(OpCode.LoadTrue, OperandShape.None, 1, OpCategory.LoadStore),
        new(OpCode.LoadInt0, OperandShape.None, 1, OpCategory.LoadStore),
        new(OpCode.LoadInt1, OperandShape.None, 1, OpCategory.LoadStore),
        new(OpCode.LoadFloat0, OperandShape.None, 1, OpCategory.LoadStore),
        new(OpCode.LoadFloat1, OperandShape.None, 1, OpCategory.LoadStore),
        new(OpCode.LoadConst, OperandShape.One, 1, OpCategory.LoadStore),
        new(OpCode.Jump, OperandShape.One, 0, OpCategory.ControlFlow),
        new(OpCode.JumpIfTrue, OperandShape.One, -1, OpCategory.ControlFlow),
        new(OpCode.JumpIfFalse, OperandShape.One, -1, OpCategory.ControlFlow),
        new(OpCode.Add, OperandShape.None, -1, OpCategory.Binary),
        new(OpCode.Sub, OperandShape.None, -1, OpCategory.Binary),
        new(OpCode.Mul, OperandShape.None, -1, OpCategory.Binary),
        new(OpCode.Div, OperandShape.None, -1, OpCategory.Binary),
        new(OpCode.Remainder, OperandShape.None, -1, OpCategory.Binary),
        new(OpCode.Negate, OperandShape.None, 0, OpCategory.Unary),
        new(OpCode.Plus, OperandShape.None, 0, OpCategory.Unary),
        new(OpCode.Not, OperandShape.None, 0, OpCategory.Unary),
        new(OpCode.Length, OperandShape.None, 0, OpCategory.Unary),
        new(OpCode.GreaterThan, OperandShape.None, -1, OpCategory.Binary),
        new(OpCode.LessThan, OperandShape.None, -1, OpCategory.Binary),
        new(OpCode.Equal, OperandShape.None, -1, OpCategory.Binary),
        new(OpCode.NotEqual, OperandShape.None, -1, OpCategory.Binary),
        new(OpCode.GreaterThanOrEqual, OperandShape.None, -1, OpCategory.Binary),
        new(OpCode.LessThanOrEqual, OperandShape.None, -1, OpCategory.Binary),
        new(OpCode.LoadLocal, OperandShape.One, 1, OpCategory.LoadStore),
        new(OpCode.LoadCaptured, OperandShape.One, 1, OpCategory.LoadStore),
        new(OpCode.LoadExternal, OperandShape.One, 1, OpCategory.LoadStore),
        new(OpCode.LoadEnvironment, OperandShape.One, 1, OpCategory.LoadStore),
        new(OpCode.StoreLocal, OperandShape.One, -1, OpCategory.LoadStore),
        new(OpCode.StoreCaptured, OperandShape.One, -1, OpCategory.LoadStore),
        new(OpCode.Return, OperandShape.None, -1, OpCategory.ControlFlow),
        new(OpCode.Duplicate, OperandShape.None, 1, OpCategory.LoadStore),
        new(OpCode.Throw, OperandShape.None, 0, OpCategory.Exception),
        new(OpCode.CreateFunction, OperandShape.One, 1, OpCategory.Function),
        new(OpCode.CreateVariadicFunction, OperandShape.Two, 1, OpCategory.Function),
        new(OpCode.CreateIterator, OperandShape.One, 1, OpCategory.Iterator),
        new(OpCode.StoreMember, OperandShape.One, -2, OpCategory.Member),
        new(OpCode.LoadMember, OperandShape.One, 0, OpCategory.Member),
        new(OpCode.HasMember, OperandShape.One, 0, OpCategory.Member),
        new(OpCode.StoreStaticMember, OperandShape.One, -2, OpCategory.Member),
        new(OpCode.LoadIndex, OperandShape.None, -1, OpCategory.Index),
        new(OpCode.StoreIndex, OperandShape.None, -2, OpCategory.Index),
        new(OpCode.LoadRawIndex, OperandShape.None, -1, OpCategory.Index),
        new(OpCode.StoreRawIndex, OperandShape.None, -2, OpCategory.Index),
        new(OpCode.LoadModule, OperandShape.One, 1, OpCategory.Module),
        new(OpCode.LoadType, OperandShape.One, 1, OpCategory.Type),
        new(OpCode.CreateLabel, OperandShape.One, 0, OpCategory.Collection),
        new(OpCode.FinishModule, OperandShape.None, 0, OpCategory.Module),
        new(OpCode.Yield, OperandShape.None, -1, OpCategory.Iterator),
        new(OpCode.LoadTerminator, OperandShape.None, 1, OpCategory.Iterator),
        new(OpCode.JumpIfTerminator, OperandShape.One, 0, OpCategory.Iterator),
        new(OpCode.JumpIfIteratorValue, OperandShape.One, 0, OpCategory.Iterator),
        new(OpCode.PrepareCall, OperandShape.One, 0, OpCategory.Call),
        new(OpCode.SetCallArgument, OperandShape.One, -1, OpCategory.Call),
        new(OpCode.SetNamedCallArgument, OperandShape.One, -1, OpCategory.Call),
        new(OpCode.InvokePreparedCall, OperandShape.One, 0, OpCategory.Call),
        new(OpCode.Call0, OperandShape.None, 0, OpCategory.Call),
        new(OpCode.Call1, OperandShape.None, -1, OpCategory.Call),
        new(OpCode.Call, OperandShape.One, null, OpCategory.Call),
        new(OpCode.CallMember, OperandShape.Two, null, OpCategory.Call),
        new(OpCode.CallStatic, OperandShape.Two, null, OpCategory.Call),
        new(OpCode.CreateTuple, OperandShape.One, null, OpCategory.Collection),
        new(OpCode.CheckContains, OperandShape.One, 0, OpCategory.Collection),
        new(OpCode.CheckType, OperandShape.None, -2, OpCategory.Type),
        new(OpCode.EnterTry, OperandShape.One, 0, OpCategory.Exception),
        new(OpCode.LeaveTry, OperandShape.None, 0, OpCategory.Exception),
        new(OpCode.CreateObject, OperandShape.One, -2, OpCategory.Type),
        new(OpCode.CreateType, OperandShape.One, 1, OpCategory.Type),
        new(OpCode.CheckConstructor, OperandShape.One, 0, OpCategory.Type),
        new(OpCode.CheckNull, OperandShape.None, 0, OpCategory.Type),
        new(OpCode.LoadIteratorFunction, OperandShape.None, 0, OpCategory.Iterator),
        new(OpCode.MarkMutable, OperandShape.None, 0, OpCategory.Metadata),
        new(OpCode.AddTypeAnnotation, OperandShape.None, -1, OpCategory.Metadata),
        new(OpCode.SetFunctionAttribute, OperandShape.One, 0, OpCategory.Function),
        new(OpCode.CreateCast, OperandShape.None, -3, OpCategory.Cast),
        new(OpCode.ApplyCast, OperandShape.None, 0, OpCategory.Cast),
        new(OpCode.ApplyMixin, OperandShape.None, -2, OpCategory.Type),
        new(OpCode.TailCall0, OperandShape.None, 0, OpCategory.Call),
        new(OpCode.TailCall1, OperandShape.None, -1, OpCategory.Call),
        new(OpCode.TailCall, OperandShape.One, null, OpCategory.Call),
        new(OpCode.EndIterator, OperandShape.None, 0, OpCategory.Iterator),
        new(OpCode.Debug, OperandShape.None, 0, OpCategory.Infrastructure),
        new(OpCode.CreateArguments, OperandShape.One, null, OpCategory.Collection),
        new(OpCode.CreateDictionary, OperandShape.One, null, OpCategory.Collection),
        new(OpCode.LoadPrivateMember, OperandShape.One, 0, OpCategory.Member),
        new(OpCode.StorePrivateMember, OperandShape.One, -1, OpCategory.Member),
        new(OpCode.Suspend, OperandShape.None, 0, OpCategory.Infrastructure),
        new(OpCode.SuspendSelect, OperandShape.One, 0, OpCategory.Infrastructure)
    };

    public readonly OpCode Code;

    public int Data;
    public int Data2;

    static Op()
    {
        var codes = Enum.GetValues<OpCode>();
        if (Infos.Length != codes.Length)
        {
            throw new InvalidOperationException("Every opcode must have exactly one metadata entry.");
        }

        for (var i = 0; i < Infos.Length; i++)
        {
            if ((int)Infos[i].Code != i)
            {
                throw new InvalidOperationException($"Opcode metadata is out of order at index {i}.");
            }
        }
    }

    public Op(OpCode code) => Code = code;

    public Op(OpCode code, int data) => (Code, Data) = (code, data);

    public Op(OpCode code, int data, int data2) => (Code, Data, Data2) = (code, data, data2);

    public override string ToString() => Code.ToString();
}

public static class OpExtensions
{
    public static OpInfo GetInfo(this OpCode op)
    {
        var index = (int)op;
        if ((uint)index >= (uint)Op.Infos.Length)
        {
            throw new InvalidOperationException($"Unknown opcode value: {index}.");
        }

        return Op.Infos[index];
    }

    internal static int GetSize(this OpCode op) => (int)op.GetInfo().Operands;

    internal static int GetStack(this OpCode op) =>
        op.GetInfo().StackDelta
        ?? throw new InvalidOperationException($"Opcode {op} has a dynamic stack effect.");

}
