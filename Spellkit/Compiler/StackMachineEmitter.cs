using Spellkit.Runtime.Types;
using System.Collections.Generic;

namespace Spellkit.Compiler;

public struct Label
{
    public static readonly Label Empty = new(EmptyLabel);
    internal const int EmptyLabel = -1;
    private readonly int index;

    public Label(int index) => this.index = index;

    public override string ToString() => "label:" + index.ToString();

    public bool IsEmpty() => index == EmptyLabel;

    public int GetIndex() => index;
}

internal sealed class StackMachineEmitter
{
    private readonly FastList<Op> ops;
    private readonly Stack<StackSize> locals;
    private readonly Unit frame;
    private readonly FastList<int> labels;
    private readonly FastList<int> fixups;
    private readonly Dictionary<string, int> strings;
    private readonly Dictionary<SpkObject, int> objects;

    private sealed class StackSize
    {
        internal int Counter;
        internal int Max;
    }

    private StackMachineEmitter(StackMachineEmitter cw, Unit unit)
    {
        ops = unit.Ops;
        locals = new(cw.locals.ToArray());
        labels = new(cw.labels.ToArray());
        fixups = new(cw.fixups.ToArray());
        strings = cw.strings;
        objects = cw.objects;
        frame = unit;
    }

    public StackMachineEmitter(Unit frame)
    {
        this.frame = frame;
        ops = frame.Ops;
        strings = new();
        objects = new();
        locals = new();
        labels = new();
        fixups = new();
    }

    public StackMachineEmitter Clone(Unit frame) => new(this, frame);

    public void CompileOpList()
    {
        foreach (var i in fixups)
        {
            ops[i].Data = labels[ops[i].Data];
        }

        fixups.Clear();
        labels.Clear();
    }

    public Label DefineLabel()
    {
        var lab = new Label(labels.Count);
        labels.Add(Label.EmptyLabel);
        return lab;
    }

    public void MarkLabel(Label label) =>
        labels[label.GetIndex()] = ops.Count;

    public void Emit(OpCode op, Label label)
    {
        if (!label.IsEmpty())
        {
            fixups.Add(ops.Count);
            Emit(new(op, label.GetIndex()));
        }
        else
        {
            Emit(new(op, 0));
        }
    }

    private void Emit(Op op) => Emit(op, op.Code.GetStack());

    private void Emit(Op op, int size)
    {
        var ss = locals.Peek();
        ss.Counter += size;

        if (ss.Counter < 0)
        {
            ss.Counter = 0;
        }

        if (ss.Counter > ss.Max)
        {
            ss.Max = ss.Counter;
        }

        ops.Add(op);
    }

    public void StartFrame() => locals.Push(new());

    public int FinishFrame() => locals.Pop().Max;

    public int Offset => ops.Count;

    private int IndexString(string val)
    {
        if (!strings.TryGetValue(val, out var idx))
        {
            frame.Strings.Add(new(val));
            idx = frame.Strings.Count - 1;
            strings.Add(val, idx);
        }

        return idx;
    }

    private int IndexObject(SpkObject val)
    {
        if (!objects.TryGetValue(val, out var idx))
        {
            frame.Objects.Add(val);
            idx = frame.Objects.Count - 1;
            objects.Add(val, idx);
        }

        return idx;
    }

    public void Push(double val)
    {
        var op = SelectInlineFloatInstruction(val);

        if (op is not null)
        {
            Emit(op);
        }
        else
        {
            Push(new SpkFloat(val));
        }
    }

    public void Push(long val)
    {
        var op = SelectInlineIntegerInstruction(val);

        if (op is not null)
        {
            Emit(op);
        }
        else
        {
            Push(new SpkInteger(val));
        }
    }

    public void Push(bool val)
    {
        Emit(SelectInlineBooleanInstruction(val));
    }

    private static Op? SelectInlineFloatInstruction(double value) =>
        value switch
        {
            0D => Op.LoadFloat0,
            1D => Op.LoadFloat1,
            _ => null
        };

    private static Op? SelectInlineIntegerInstruction(long value) =>
        value switch
        {
            0L => Op.LoadInt0,
            1L => Op.LoadInt1,
            _ => null
        };

    private static Op SelectInlineBooleanInstruction(bool value) =>
        value ? Op.LoadTrue : Op.LoadFalse;

    public void Push(string val) => Push(new SpkString(val));

    public void Push(char val) => Push(new SpkChar(val));

    public void Push(SpkObject val) => Emit(new(OpCode.LoadConst, IndexObject(val)));

    public void LoadVariable(ScopeVar sv)
    {
        var (code, data) = SelectLoadVariableInstruction(sv);
        Emit(new(code, data));
    }

    public void LoadEnvironment(string name) =>
        Emit(new(OpCode.LoadEnvironment, IndexString(name)));

    public void StoreVariable(int address)
    {
        var (code, data) = SelectStoreVariableInstruction(address);
        Emit(new(code, data));
    }

    private static (OpCode Code, int Data) SelectLoadVariableInstruction(ScopeVar sv)
    {
        if ((sv.Data & VarFlags.External) == VarFlags.External)
        {
            return (OpCode.LoadExternal, sv.Address);
        }

        return IsLocalSlotAddress(sv.Address)
            ? (OpCode.LoadLocal, LocalSlotIndex(sv.Address))
            : (OpCode.LoadCaptured, sv.Address);
    }

    private static (OpCode Code, int Data) SelectStoreVariableInstruction(int address) =>
        IsLocalSlotAddress(address)
            ? (OpCode.StoreLocal, LocalSlotIndex(address))
            : (OpCode.StoreCaptured, address);

    private static bool IsLocalSlotAddress(int address) =>
        (address & byte.MaxValue) == 0;

    private static int LocalSlotIndex(int address) =>
        address >> 8;

    public void CreateLabel(string CreateLabel)
    {
        var idx = IndexString(CreateLabel);
        Emit(new(OpCode.CreateLabel, idx));
    }

    public void TailCall(int args)
    {
        if (args == 0)
        {
            Emit(Op.TailCall0, 0);
        }
        else if (args == 1)
        {
            Emit(Op.TailCall1, -1);
        }
        else
        {
            Emit(new(OpCode.TailCall, args), -args);
        }
    }

    public void LoadPrivateMember(string name) => Emit(new(OpCode.LoadPrivateMember, IndexString(name)));
    public void StorePrivateMember(string name) => Emit(new(OpCode.StorePrivateMember, IndexString(name)));

    public void CheckContains(string field) => Emit(new(OpCode.CheckContains, IndexObject(new SpkString(field))));

    public void StoreMember(string member) => Emit(new(OpCode.StoreMember, IndexString(member)));
    public void StoreStaticMember(string member) => Emit(new(OpCode.StoreStaticMember, IndexString(member)));

    public void LoadType(int code) => Emit(new(OpCode.LoadType, code));

    public void PrepareCall(int argCount) => Emit(new(OpCode.PrepareCall, argCount));
    public void SetCallArgument(int index) => Emit(new(OpCode.SetCallArgument, index));
    public void SetNamedCallArgument(string name) => Emit(new(OpCode.SetNamedCallArgument, IndexString(name)));
    public void InvokePreparedCall(int argCount) => Emit(new(OpCode.InvokePreparedCall, argCount));
    public void Call(int argCount)
    {
        if (argCount == 0)
        {
            Emit(Op.Call0);
        }
        else if (argCount == 1)
        {
            Emit(Op.Call1);
        }
        else
        {
            Emit(new(OpCode.Call, argCount), -argCount);
        }
    }

    public void CallMember(string name, int argCount)
    {
        Emit(new(OpCode.CallMember, IndexString(name), argCount), -argCount);
    }

    public void CallStatic(string name, int argCount)
    {
        Emit(new(OpCode.CallStatic, IndexString(name), argCount), -argCount);
    }
    public void CheckConstructor(string ctor) => Emit(new(OpCode.CheckConstructor, IndexString(ctor)));

    public void CreateArguments(int Length) => Emit(new(OpCode.CreateArguments, Length), -Length + 1);
    public void CreateDictionary(int Length) => Emit(new(OpCode.CreateDictionary, Length), -Length + 1);
    public void CreateTuple(int Length) => Emit(new(OpCode.CreateTuple, Length), -Length + 1);
    public void CreateFunction(int funHandle) => Emit(new(OpCode.CreateFunction, funHandle));
    public void CreateVariadicFunction(int funHandle, int variadicIndex) =>
        Emit(new(OpCode.CreateVariadicFunction, funHandle, variadicIndex));
    public void SetFunctionAttribute(int attr) => Emit(new(OpCode.SetFunctionAttribute, attr));
    public void CreateIterator(int funHandle) => Emit(new(OpCode.CreateIterator, funHandle));
    public void CreateSelectFactory(SpkObject definition, int closureCount) =>
        Emit(new(OpCode.CreateSelectFactory, IndexObject(definition), closureCount), -closureCount + 1);
    public void Jump(Label lab) => Emit(OpCode.Jump, lab);
    public void JumpIfTrue(Label lab) => Emit(OpCode.JumpIfTrue, lab);
    public void JumpIfFalse(Label lab) => Emit(OpCode.JumpIfFalse, lab);
    public void JumpIfTerminator(Label lab) => Emit(OpCode.JumpIfTerminator, lab);
    public void JumpIfIteratorValue(Label lab) => Emit(OpCode.JumpIfIteratorValue, lab);
    public void LoadMember(string name) => Emit(new(OpCode.LoadMember, IndexString(name)));
    public void HasMember(string name) => Emit(new(OpCode.HasMember, IndexString(name)));
    public void LoadModule(int code) => Emit(new(OpCode.LoadModule, code));
    public void EnterTry(Label lab) => Emit(OpCode.EnterTry, lab);
    public void CreateObject(string ctor) => Emit(new(OpCode.CreateObject, IndexString(ctor)));
    public void CreateType(string name) => Emit(new(OpCode.CreateType, IndexString(name)));

    public void LoadIteratorFunction() => Emit(Op.LoadIteratorFunction);
    public void LeaveTry() => Emit(Op.LeaveTry);
    public void Yield() => Emit(Op.Yield);
    public void EndIterator() => Emit(Op.EndIterator);
    public void ConvertToString() => Emit(Op.ConvertToString);
    public void LoadIndex() => Emit(Op.LoadIndex);
    public void StoreIndex() => Emit(Op.StoreIndex);
    public void LoadRawIndex() => Emit(Op.LoadRawIndex);
    public void StoreRawIndex() => Emit(Op.StoreRawIndex);
    public void LoadThis() => Emit(Op.LoadThis);
    public void LoadType() => Emit(Op.LoadType);
    public void LoadNil() => Emit(Op.LoadNil);
    public void LoadTerminator() => Emit(Op.LoadTerminator);
    public void NoOperation() => Emit(Op.NoOperation);
    public void Suspend() => Emit(Op.Suspend);
    public void SuspendSelect() => Emit(Op.SuspendSelect);
    public void Drop() => Emit(Op.Drop);
    public void Add() => Emit(Op.Add);
    public void Sub() => Emit(Op.Sub);
    public void Mul() => Emit(Op.Mul);
    public void Div() => Emit(Op.Div);
    public void Remainder() => Emit(Op.Remainder);
    public void Negate() => Emit(Op.Negate);
    public void Plus() => Emit(Op.Plus);
    public void Not() => Emit(Op.Not);
    public void Length() => Emit(Op.Length);
    public void GreaterThan() => Emit(Op.GreaterThan);
    public void LessThan() => Emit(Op.LessThan);
    public void Equal() => Emit(Op.Equal);
    public void NotEqual() => Emit(Op.NotEqual);
    public void GreaterThanOrEqual() => Emit(Op.GreaterThanOrEqual);
    public void LessThanOrEqual() => Emit(Op.LessThanOrEqual);
    public void Return() => Emit(Op.Return);
    public void Duplicate() => Emit(Op.Duplicate);
    public void Throw() => Emit(Op.Throw);
    public void FinishModule() => Emit(Op.FinishModule);
    public void CheckNull() => Emit(Op.CheckNull);
    public void MarkMutable() => Emit(Op.MarkMutable);
    public void AddTypeAnnotation() => Emit(Op.AddTypeAnnotation);
    public void CheckType() => Emit(Op.CheckType);
    public void CreateCast() => Emit(Op.CreateCast);
    public void ApplyCast() => Emit(Op.ApplyCast);
    public void ApplyMixin() => Emit(Op.ApplyMixin);
    public void Debug() => Emit(Op.Debug);
}
