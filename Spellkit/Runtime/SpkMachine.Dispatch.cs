using Spellkit.Compiler;
using Spellkit.Debug;
using Spellkit.Hosting;
using Spellkit.Linker;
using Spellkit.Runtime.Types;
using Spellkit.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Spellkit.Runtime;

internal static partial class SpkMachine
{
    private static VmDispatchResult Dispatch(Op op, VmState state) =>
        op.Code.GetInfo().Category switch
        {
            OpCategory.Infrastructure => ExecuteInfrastructure(op, state),
            OpCategory.LoadStore => ExecuteLoadStore(op, state),
            OpCategory.ControlFlow => ExecuteControlFlow(op, state),
            OpCategory.Binary => ExecuteBinary(op, state),
            OpCategory.Unary => ExecuteUnary(op, state),
            OpCategory.Function => ExecuteFunction(op, state),
            OpCategory.Member => ExecuteMember(op, state),
            OpCategory.Index => ExecuteIndex(op, state),
            OpCategory.Module => ExecuteModuleInstruction(op, state),
            OpCategory.Iterator => ExecuteIterator(op, state),
            OpCategory.Call => ExecuteCall(op, state),
            OpCategory.Collection => ExecuteCollection(op, state),
            OpCategory.Type => ExecuteType(op, state),
            OpCategory.Exception => ExecuteException(op, state),
            OpCategory.Conversion => ExecuteConversion(op, state),
            OpCategory.Metadata => ExecuteMetadata(op, state),
            OpCategory.Cast => ExecuteCast(op, state),
            _ => throw new InvalidOperationException($"Unsupported opcode category: {op.Code.GetInfo().Category}.")
        };

    private static VmDispatchResult ExecuteInfrastructure(Op op, VmState state)
    {
        if (op.Code is OpCode.Suspend)
        {
            return new(VmStep.Suspend);
        }

        if (op.Code is not (OpCode.NoOperation or OpCode.Debug))
        {
            throw UnexpectedOpcode(op);
        }

        return VmDispatchResult.Continue;
    }

    private static VmDispatchResult ExecuteLoadStore(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.LoadThis:
                state.EvalStack.Push(state.Function.Self!);
                break;
            case OpCode.Drop:
                state.EvalStack.PopVoid();
                break;
            case OpCode.LoadConst:
                state.EvalStack.Push(state.Unit.Objects[op.Data]);
                break;
            case OpCode.LoadNil:
                state.EvalStack.Push(SpkNil.Instance);
                break;
            case OpCode.LoadTrue:
                state.EvalStack.Push(True);
                break;
            case OpCode.LoadFalse:
                state.EvalStack.Push(False);
                break;
            case OpCode.LoadInt0:
                state.EvalStack.Push(SpkInteger.Zero);
                break;
            case OpCode.LoadInt1:
                state.EvalStack.Push(SpkInteger.One);
                break;
            case OpCode.LoadFloat0:
                state.EvalStack.Push(SpkFloat.Zero);
                break;
            case OpCode.LoadFloat1:
                state.EvalStack.Push(SpkFloat.One);
                break;
            case OpCode.StoreLocal:
                state.Locals[op.Data] = state.EvalStack.Pop();
                break;
            case OpCode.LoadLocal:
                state.EvalStack.Push(state.Locals[op.Data]);
                break;
            case OpCode.LoadCaptured:
                state.Second = state.Function.Captures[^(op.Data & byte.MaxValue)][op.Data >> 8];
                state.EvalStack.Push(state.Second);
                break;
            case OpCode.LoadExternal:
                state.Second = state.Context.RuntimeContext.Units[
                    state.Unit.UnitIds[op.Data & byte.MaxValue]][op.Data >> 8];
                if (state.Second is SpkFunction function && function.Auto)
                {
                    state.Second = function.TryInvokeProperty(state.Context, SpkNil.Instance);
                }

                state.EvalStack.Push(state.Second);
                if (state.Context.HasErrors)
                {
                    return new(VmStep.Throw);
                }

                break;
            case OpCode.LoadEnvironment:
                var environmentName = (string)state.Unit.Strings[op.Data];
                var environment = state.Context.GetContextVariable<SpellkitEnvironment>(
                    SpellkitEnvironment.ContextKey);
                if (environment is null || !environment.TryResolve(environmentName, out state.Second))
                {
                    state.Context.Failure(
                        $"Environment name '{environmentName}' is not exposed.");
                    return new(VmStep.Throw);
                }

                state.EvalStack.Push(state.Second);
                break;
            case OpCode.StoreCaptured:
                state.Function.Captures[^(op.Data & byte.MaxValue)][op.Data >> 8] = state.EvalStack.Pop();
                break;
            case OpCode.Duplicate:
                state.EvalStack.Dup();
                break;
            default:
                throw UnexpectedOpcode(op);
        }

        return VmDispatchResult.Continue;
    }

    private static VmDispatchResult ExecuteControlFlow(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.Jump:
                state.Offset = op.Data;
                return VmDispatchResult.Continue;
            case OpCode.JumpIfTrue:
                if (state.EvalStack.Pop().IsTrue())
                {
                    state.Offset = op.Data;
                }

                return VmDispatchResult.Continue;
            case OpCode.JumpIfFalse:
                if (state.EvalStack.Pop().IsFalse())
                {
                    state.Offset = op.Data;
                }

                return VmDispatchResult.Continue;
            case OpCode.Return:
                state.Context.CatchMarks.Pop();
                state.Function.Locals = null;
                return ResumeCallerOrReturn(state);
            default:
                throw UnexpectedOpcode(op);
        }
    }

    private static VmDispatchResult ExecuteBinary(Op op, VmState state) =>
        SpkMachineHelpers.ExecuteBinaryOperation(
            op.Code, state.EvalStack, state.Types, state.Context, out state.Second)
            ? VmDispatchResult.Continue
            : new(VmStep.HandleCallback);

    private static VmDispatchResult ExecuteUnary(Op op, VmState state)
    {
        state.Second = null;
        return SpkMachineHelpers.ExecuteUnaryOperation(op.Code, state.EvalStack, state.Types, state.Context)
            ? VmDispatchResult.Continue
            : new(VmStep.HandleCallback);
    }

    private static VmDispatchResult ExecuteFunction(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.CreateFunction:
                state.EvalStack.Push(SpkMachineHelpers.ExecuteFunctionCreation(
                    state.Unit, state.Function, state.Locals, op.Data));
                break;
            case OpCode.CreateVariadicFunction:
                state.EvalStack.Push(SpkMachineHelpers.ExecuteFunctionCreation(
                    state.Unit, state.Function, state.Locals, op.Data, op.Data2));
                break;
            case OpCode.SetFunctionAttribute:
                ((SpkFunction)state.EvalStack.Peek()).Attr |= op.Data;
                break;
            default:
                throw UnexpectedOpcode(op);
        }

        return VmDispatchResult.Continue;
    }

    private static VmDispatchResult ExecuteMember(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.HasMember:
            case OpCode.LoadMember:
                return SpkMachineHelpers.ExecuteNamedMemberAccess(
                    op.Code, state.EvalStack, state.Types, state.Unit.Strings[op.Data],
                    state.Context, out state.Second)
                    ? VmDispatchResult.Continue
                    : new(VmStep.Throw);
            case OpCode.StoreStaticMember:
            case OpCode.StoreMember:
                return SpkMachineHelpers.ExecuteTypeMemberMutation(
                    op.Code, state.EvalStack, state.Context, state.Unit.Strings[op.Data],
                    out state.First, out state.Second)
                    ? VmDispatchResult.Continue
                    : new(VmStep.Throw);
            case OpCode.LoadPrivateMember:
                return SpkMachineHelpers.ExecutePrivateGet(
                    state.EvalStack, (string)state.Unit.Strings[op.Data], state.Context, out state.Second)
                    ? VmDispatchResult.Continue
                    : new(VmStep.HandleCallback);
            case OpCode.StorePrivateMember:
                return SpkMachineHelpers.ExecutePrivateSet(
                    state.EvalStack, (string)state.Unit.Strings[op.Data], state.Context,
                    out state.First, out state.Second)
                    ? VmDispatchResult.Continue
                    : new(VmStep.HandleCallback);
            default:
                throw UnexpectedOpcode(op);
        }
    }

    private static VmDispatchResult ExecuteIndex(Op op, VmState state)
    {
        var success = op.Code switch
        {
            OpCode.LoadIndex => SpkMachineHelpers.ExecuteIndexedAccess(
                state.EvalStack, state.Types, state.Context, out state.First, out state.Second),
            OpCode.LoadRawIndex => SpkMachineHelpers.ExecuteRawIndexedAccess(
                state.EvalStack, state.Types, state.Context, out state.First, out state.Second),
            OpCode.StoreIndex => SpkMachineHelpers.ExecuteIndexedSet(
                state.EvalStack, state.Types, state.Context,
                out state.First, out state.Second, out state.Third),
            OpCode.StoreRawIndex => SpkMachineHelpers.ExecuteRawIndexedSet(
                state.EvalStack, state.Types, state.Context,
                out state.First, out state.Second, out state.Third),
            _ => throw UnexpectedOpcode(op)
        };

        return success ? VmDispatchResult.Continue : new(VmStep.HandleCallback);
    }

    private static VmDispatchResult ExecuteModuleInstruction(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.LoadModule:
                var unitId = state.Unit.UnitIds[op.Data];
                ExecuteModule(unitId, state.Context);
                state.EvalStack.Push(new SpkModule(
                    state.Context.RuntimeContext.Composition.Units[unitId],
                    state.Context.RuntimeContext.Units[unitId]));
                return VmDispatchResult.Continue;
            case OpCode.FinishModule:
                if (state.EvalStack.Size is > 1 or 0)
                {
                    throw new SpkRuntimeException(MessageCatalog.Get(MessageGroup.Runtime, "StackCorrupted.0"));
                }

                state.Context.RuntimeContext.Units[state.Function.UnitId] = state.Locals;
                state.Context.CallCnt--;
                state.Context.UnitId = state.Context.CallerUnitId;
                return new(VmStep.Return, state.EvalStack.Pop());
            default:
                throw UnexpectedOpcode(op);
        }
    }

    private static VmDispatchResult ExecuteIterator(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.CreateIterator:
                state.EvalStack.Push(SpkMachineHelpers.ExecuteIteratorCreation(
                    state.Function, op.Data, state.Locals));
                break;
            case OpCode.LoadTerminator:
                state.EvalStack.Push(SpkNil.Terminator);
                break;
            case OpCode.Yield:
                state.Function.PreviousOffset = state.Offset++;
                state.Function.Locals = state.Locals;
                state.Function.CatchMarks = state.Context.CatchMarks.Pop();
                return ResumeCallerOrReturn(state);
            case OpCode.EndIterator:
                state.EvalStack.Push(SpkNil.Instance);
                break;
            case OpCode.JumpIfTerminator:
                if (ReferenceEquals(state.EvalStack.Peek(), SpkNil.Terminator))
                {
                    state.Offset = op.Data;
                }

                break;
            case OpCode.JumpIfIteratorValue:
                if (state.EvalStack.Peek().TypeId == Spk.Iterator)
                {
                    state.Offset = op.Data;
                }

                break;
            case OpCode.LoadIteratorFunction:
                state.Second = state.EvalStack.Peek();
                if (state.Second is SpkIterator iterator)
                {
                    state.EvalStack.Replace(iterator.GetIteratorFunction());
                }
                else
                {
                    state.Context.InvalidType(Spk.Iterator, state.Second);
                    return new(VmStep.Throw);
                }
                break;
            default:
                throw UnexpectedOpcode(op);
        }

        return VmDispatchResult.Continue;
    }

    private static VmDispatchResult ExecuteCall(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.PrepareCall:
                return SpkMachineHelpers.TryPrepareFunction(
                    state.EvalStack, state.Types, state.Context, op.Data, out _)
                    ? VmDispatchResult.Continue
                    : new(VmStep.Throw);
            case OpCode.SetCallArgument:
                SpkMachineHelpers.AssignIndexedArgument(
                    state.Context.PeekArguments(), state.EvalStack, op.Data);
                return VmDispatchResult.Continue;
            case OpCode.SetNamedCallArgument:
                var arguments = state.Context.PeekArguments();
                var function = (SpkFunction)state.EvalStack.Peek(2);
                return SpkMachineHelpers.TryAssignNamedArgument(
                    arguments, state.EvalStack, function,
                    (string)state.Unit.Strings[op.Data], state.Context)
                    ? VmDispatchResult.Continue
                    : new(VmStep.Throw);
            case OpCode.TailCall0:
            case OpCode.TailCall1:
            case OpCode.TailCall:
                var tailArgumentCount = op.Code switch
                {
                    OpCode.TailCall0 => 0,
                    OpCode.TailCall1 => 1,
                    _ => op.Data
                };
                SpkMachineHelpers.PrepareStandardCall(
                    state.EvalStack, state.Context, state.Function, state.Offset,
                    state.Locals, tailArgumentCount, out state.Function, out state.Locals);
                return new(VmStep.EnterFunction);
            case OpCode.Call0:
            case OpCode.Call1:
            case OpCode.Call:
                var argumentCount = op.Code switch
                {
                    OpCode.Call0 => 0,
                    OpCode.Call1 => 1,
                    _ => op.Data
                };
                if (SpkMachineHelpers.ExecutePositionalFunctionCall(
                    state.EvalStack, state.Types, state.Context, state.Function,
                    state.Offset, state.Locals, argumentCount,
                    out var nextFunction, out var nextLocals))
                {
                    state.Function = nextFunction!;
                    state.Locals = nextLocals!;
                    return new(VmStep.EnterFunction);
                }
                return state.Context.Error is null
                    ? VmDispatchResult.Continue
                    : new(VmStep.Throw);
            case OpCode.CallMember:
            case OpCode.CallStatic:
                if (SpkMachineHelpers.ExecuteNamedMemberCall(
                    op.Code, state.EvalStack, state.Types, state.Unit.Strings[op.Data],
                    state.Context, state.Function, state.Offset, state.Locals, op.Data2,
                    out nextFunction, out nextLocals))
                {
                    state.Function = nextFunction!;
                    state.Locals = nextLocals!;
                    return new(VmStep.EnterFunction);
                }
                return state.Context.Error is null
                    ? VmDispatchResult.Continue
                    : new(VmStep.Throw);
            case OpCode.InvokePreparedCall:
                if (SpkMachineHelpers.ExecuteFunctionCall(
                    state.EvalStack, state.Context, state.Function, state.Offset,
                    state.Locals, op.Data, out nextFunction, out nextLocals))
                {
                    state.Function = nextFunction!;
                    state.Locals = nextLocals!;
                    return new(VmStep.EnterFunction);
                }
                return state.Context.Error is null
                    ? VmDispatchResult.Continue
                    : new(VmStep.Throw);
            default:
                throw UnexpectedOpcode(op);
        }
    }

    private static VmDispatchResult ExecuteCollection(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.CreateLabel:
                state.EvalStack.Replace(new SpkLabel(
                    (string)state.Unit.Strings[op.Data], state.EvalStack.Peek()));
                return VmDispatchResult.Continue;
            case OpCode.CreateArguments:
                state.EvalStack.Push(op.Data == 0
                    ? SpkTuple.Empty
                    : SpkMachineHelpers.MakeTuple(state.EvalStack, op.Data, true));
                return VmDispatchResult.Continue;
            case OpCode.CreateTuple:
                state.EvalStack.Push(op.Data == 0
                    ? SpkTuple.Empty
                    : SpkMachineHelpers.MakeTuple(state.EvalStack, op.Data, false));
                return VmDispatchResult.Continue;
            case OpCode.CreateDictionary:
                state.EvalStack.Push(SpkMachineHelpers.MakeDictionary(state.EvalStack, op.Data));
                return VmDispatchResult.Continue;
            case OpCode.CheckContains:
                state.Second = state.Unit.Objects[op.Data];
                return SpkMachineHelpers.ExecuteContains(
                    state.EvalStack, state.Types, state.Context, state.Second, out state.First)
                    ? VmDispatchResult.Continue
                    : new(VmStep.HandleCallback);
            default:
                throw UnexpectedOpcode(op);
        }
    }

    private static VmDispatchResult ExecuteType(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.LoadType:
                state.EvalStack.Push(state.Types[op.Data]);
                break;
            case OpCode.CheckType:
                SpkMachineHelpers.ExecuteTypeCheck(
                    state.EvalStack, state.Types, out state.First, out state.Second);
                break;
            case OpCode.CreateObject:
                SpkMachineHelpers.ExecuteObjectCreation(
                    state.EvalStack, state.Unit, (string)state.Unit.Strings[op.Data],
                    out state.First, out state.Second, out state.Third);
                state.Third = null;
                break;
            case OpCode.CreateType:
                state.EvalStack.Push(SpkMachineHelpers.ExecuteTypeCreation(
                    state.Types, (string)state.Unit.Strings[op.Data]));
                break;
            case OpCode.CheckConstructor:
                SpkMachineHelpers.ExecuteConstructorCheck(
                    state.EvalStack, state.Unit.Strings[op.Data], out state.Second);
                break;
            case OpCode.CheckNull:
                state.EvalStack.Push(state.EvalStack.Pop() is null);
                break;
            case OpCode.ApplyMixin:
                return SpkMachineHelpers.ExecuteTypeMemberMutation(
                    op.Code, state.EvalStack, state.Context, state.Unit.Strings[op.Data],
                    out state.First, out state.Second)
                    ? VmDispatchResult.Continue
                    : new(VmStep.Throw);
            default:
                throw UnexpectedOpcode(op);
        }

        return VmDispatchResult.Continue;
    }

    private static VmDispatchResult ExecuteException(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.Throw:
                state.Second = state.EvalStack.Pop();
                state.Context.Error ??= state.Second.ToError();
                return new(VmStep.Throw);
            case OpCode.EnterTry:
                var catchMarks = state.Context.CatchMarks.Peek();
                if (catchMarks is null)
                {
                    state.Context.CatchMarks.Replace(catchMarks = new());
                }

                catchMarks.Push(new(op.Data, state.Context.CallStack.Count));
                return VmDispatchResult.Continue;
            case OpCode.LeaveTry:
                state.Context.CatchMarks.Peek().Pop();
                return VmDispatchResult.Continue;
            default:
                throw UnexpectedOpcode(op);
        }
    }

    private static VmDispatchResult ExecuteConversion(Op op, VmState state)
    {
        if (op.Code is not OpCode.ConvertToString)
        {
            throw UnexpectedOpcode(op);
        }

        state.Second = null;
        return SpkMachineHelpers.ExecuteStringConversion(
            state.EvalStack, state.Types, state.Context)
            ? VmDispatchResult.Continue
            : new(VmStep.HandleCallback);
    }

    private static VmDispatchResult ExecuteMetadata(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.AddTypeAnnotation:
                state.First = state.EvalStack.Pop();
                state.Second = state.EvalStack.Peek();
                ((SpkLabel)state.Second).AddTypeAnnotation((SpkTypeInfo)state.First);
                break;
            case OpCode.MarkMutable:
                ((SpkLabel)state.EvalStack.Peek()).Mutable = true;
                break;
            default:
                throw UnexpectedOpcode(op);
        }

        return VmDispatchResult.Continue;
    }

    private static VmDispatchResult ExecuteCast(Op op, VmState state)
    {
        switch (op.Code)
        {
            case OpCode.CreateCast:
                SpkMachineHelpers.ExecuteCastRegistration(
                    state.EvalStack, out state.First, out state.Second);
                return VmDispatchResult.Continue;
            case OpCode.ApplyCast:
                return SpkMachineHelpers.ExecuteCast(
                    state.EvalStack, state.Types, state.Context,
                    out state.First, out state.Second)
                    ? VmDispatchResult.Continue
                    : new(VmStep.Throw);
            default:
                throw UnexpectedOpcode(op);
        }
    }

}
