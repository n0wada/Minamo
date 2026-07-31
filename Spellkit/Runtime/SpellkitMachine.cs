using Spellkit.Compiler;
using Spellkit.Debug;
using Spellkit.Linker;
using Spellkit.Runtime.Types;
using Spellkit.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Spellkit.Runtime;

internal static partial class SpellkitMachine
{
    private const int MaxNestedCalls = 200;

    private static SpellkitNativeFunction Global(int unitId) => new(null, unitId, 0, FastList<SpellkitObject[]>.Empty, -1);

    public static ExecutionContext CreateExecutionContext(UnitComposition composition) =>
        new(new(), new(composition));

    public static ExecutionContext CreateExecutionContext(RuntimeContext rtx) => new(new(), rtx);

    public static ExecutionResult Execute(ExecutionContext ctx) => ExecuteModule(0, ctx);

    internal static ExecutionResult Resume(VmContinuation continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        var state = continuation.Take();
        try
        {
            return Run(state, continuation);
        }
        catch
        {
            continuation.Complete();
            throw;
        }
    }

    internal static ExecutionResult Resume(VmContinuation continuation, SpellkitObject value)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        ArgumentNullException.ThrowIfNull(value);
        var state = continuation.Take();
        state.EvalStack.Push(value);
        try
        {
            return Run(state, continuation);
        }
        catch
        {
            continuation.Complete();
            throw;
        }
    }

    internal static ExecutionResult Resume(
        VmContinuation continuation,
        SpellkitAwaitable awaitable)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        ArgumentNullException.ThrowIfNull(awaitable);
        var state = continuation.Take();
        try
        {
            var value = awaitable.Complete(state.Context);
            if (state.Context.Error is null)
            {
                state.EvalStack.Push(value);
                state.Context.CallStack.Pop();
            }
            else
            {
                HandleError(state);
            }

            return Run(state, continuation);
        }
        catch
        {
            continuation.Complete();
            throw;
        }
    }

    private static ExecutionResult ExecuteModule(int unitId, ExecutionContext ctx)
    {
        var unit = ctx.RuntimeContext.Composition.Units[unitId];

        if (unit.Layouts.Count == 0) //This is a foreign module
        {
            if (ctx.RuntimeContext.Units[unitId] is null) //This module is not processed yet
            {
                var foreign = (ForeignUnit)unit;
                foreign.Initialize(ctx);
                ctx.RuntimeContext.Units[unitId] = foreign.Values.ToArray();
            }

            return ExecutionResult.Fetch(0, SpellkitNil.Instance, ctx);
        }

        var lay0 = unit.Layouts[0];

        //if yes, we are in interactive mode and need to check if the size
        //of global layout (for global variables) has changed
        if (ctx.RuntimeContext.Units[0] is not null && lay0.Size > ctx.RuntimeContext.Units[0].Length)
        {
            var mems = new SpellkitObject[lay0.Size];
            Array.Copy(ctx.RuntimeContext.Units[0], mems, ctx.RuntimeContext.Units[0].Length);
            ctx.RuntimeContext.Units[0] = mems;
        }

        //Module is already processed, no need for further actions.
        //However if unitId is 0 and is already processed - it means that we are inside interactive
        //and should execute it one more time.
        if (unitId is not 0 && ctx.RuntimeContext.Units[unitId] is not null)
        {
            return ExecutionResult.Fetch(0, SpellkitNil.Instance, ctx);
        }

        ctx.CatchMarks.Push(null!);
        ctx.RuntimeContext.Units[unitId] = ctx.RuntimeContext.Units[unitId] ?? new SpellkitObject[lay0.Size];
        return ExecuteWithDataResult(Global(unitId), Array.Empty<SpellkitObject>(), ctx);
    }

    internal static SpellkitObject ExecuteWithData(SpellkitNativeFunction function, SpellkitObject[] locals, ExecutionContext ctx)
    {
        var result = ExecuteWithDataResult(function, locals, ctx);
        if (result.Reason is TerminationReason.Suspended)
        {
            throw new InvalidOperationException(
                "A VM continuation cannot suspend through a synchronous function invocation.");
        }

        return result.Value ?? SpellkitNil.Instance;
    }

    internal static ExecutionResult ExecuteWithArguments(
        SpellkitNativeFunction function,
        SpellkitObject[] arguments,
        ExecutionContext ctx)
    {
        var locals = function.CreateLocals(ctx);
        Array.Copy(arguments, locals, arguments.Length);
        return ExecuteWithDataResult(function, locals, ctx);
    }

    internal static ExecutionResult ExecuteWithDataResult(
        SpellkitNativeFunction function,
        SpellkitObject[] locals,
        ExecutionContext ctx)
    {
        ctx.CallCnt++;

        var maxCallDepth = ctx.Control?.MaxCallDepth ?? MaxNestedCalls;
        if (ctx.CallCnt > maxCallDepth)
        {
            if (ctx.Control?.MaxCallDepth is not null)
            {
                throw new SpellkitExecutionLimitException(
                    SpellkitExecutionLimitKind.CallDepth,
                    $"Call depth limit of {maxCallDepth} was exceeded.");
            }

            throw new SpellkitRuntimeException(MessageCatalog.Get(MessageGroup.Runtime, "StackOverflow.0"));
        }
        var state = new VmState(function, locals, ctx);
        EnterCurrentFunction(state);
        return Run(state);
    }

    private static ExecutionResult Run(VmState state, VmContinuation? continuation = null)
    {
        while (true)
        {
            state.Context.Control?.OnInstruction();
            var op = state.Ops[state.Offset++];
            var result = Dispatch(op, state);

            switch (result.Step)
            {
                case VmStep.Continue:
                    break;
                case VmStep.EnterFunction:
                    EnterCurrentFunction(state);
                    break;
                case VmStep.ReloadProgram:
                    LoadProgram(state.Function, state.Context, out state.Unit, out state.Ops);
                    break;
                case VmStep.HandleCallback:
                    state.EvalStack.Pop();
                    if (TryCall(state.Context, state.Offset, ref state.Second, ref state.Third,
                        ref state.Function, ref state.Locals, ref state.EvalStack))
                    {
                        EnterCurrentFunction(state);
                        break;
                    }

                    HandleError(state);
                    break;
                case VmStep.Throw:
                    HandleError(state);
                    break;
                case VmStep.Return:
                    continuation?.Complete();
                    return ExecutionResult.Fetch(0, result.Value, state.Context);
                case VmStep.Suspend:
                    var saved = continuation;
                    if (saved is null)
                    {
                        saved = new VmContinuation(state);
                    }
                    else
                    {
                        saved.Suspend(state);
                    }
                    return ExecutionResult.Suspend(
                        state.Context,
                        saved,
                        result.Suspension ?? new VmSuspension(null));
                default:
                    throw new InvalidOperationException($"Unsupported VM step: {result.Step}.");
            }
        }
    }

    private enum VmStep
    {
        Continue,
        EnterFunction,
        ReloadProgram,
        HandleCallback,
        Throw,
        Return,
        Suspend
    }

    private readonly record struct VmDispatchResult(
        VmStep Step,
        SpellkitObject? Value = null,
        VmSuspension? Suspension = null)
    {
        public static readonly VmDispatchResult Continue = new(VmStep.Continue);
    }

    internal sealed class VmState
    {
        public VmState(SpellkitNativeFunction function, SpellkitObject[] locals, ExecutionContext context)
        {
            Function = function;
            Locals = locals;
            Context = context;
            Types = context.RuntimeContext.Types;
            Unit = null!;
            Ops = null!;
            EvalStack = null!;
        }

        public ExecutionContext Context { get; }
        public FastList<SpellkitTypeInfo> Types { get; }
        public SpellkitNativeFunction Function;
        public SpellkitObject[] Locals;
        public Unit Unit;
        public FastList<Op> Ops;
        public int Offset;
        public EvalStack EvalStack;
        public SpellkitObject? First;
        public SpellkitObject? Second;
        public SpellkitObject? Third;
    }

    internal sealed class VmContinuation
    {
        private readonly object syncRoot = new();
        private VmState? state;
        private bool completed;

        internal VmContinuation(VmState state) => this.state = state;

        internal VmState Take()
        {
            lock (syncRoot)
            {
                if (completed || state is null)
                {
                    throw new InvalidOperationException("The VM continuation is not waiting to be resumed.");
                }

                var result = state;
                state = null;
                return result;
            }
        }

        internal void Suspend(VmState next)
        {
            lock (syncRoot)
            {
                if (completed || state is not null)
                {
                    throw new InvalidOperationException("The VM continuation is already completed or suspended.");
                }

                state = next;
            }
        }

        internal void Complete()
        {
            lock (syncRoot)
            {
                completed = true;
                state = null;
            }
        }
    }

    private static void EnterCurrentFunction(VmState state)
    {
        state.Context.Control?.CheckCallDepth(state.Context.CallStack.Count + 1);
        EnterFunction(state.Function, ref state.Locals, state.Context,
            out state.Unit, out state.Ops, out state.Offset, out state.EvalStack);
    }

    private static void HandleError(VmState state)
    {
        state.Offset = ThrowIf(state.Context, state.Offset, ref state.Function,
            ref state.Locals, ref state.EvalStack);
        state.EvalStack.Clear();
        LoadProgram(state.Function, state.Context, out state.Unit, out state.Ops);
        state.EvalStack.Push(state.Context.Error!);
        state.Context.Error = null;
    }

    private static VmDispatchResult ResumeCallerOrReturn(VmState state)
    {
        if (ResumeCallerOrFinish(
            state.Context, state.EvalStack, ref state.Function, ref state.Locals,
            ref state.Offset, ref state.EvalStack, out var value))
        {
            return new(VmStep.ReloadProgram);
        }

        state.Context.CallCnt--;
        return new(VmStep.Return, value);
    }

    private static InvalidOperationException UnexpectedOpcode(Op op) =>
        new($"Opcode {op.Code} is not valid in category {op.Code.GetInfo().Category}.");

    private static void EnterFunction(SpellkitNativeFunction function, ref SpellkitObject[] locals, ExecutionContext ctx,
        out Unit unit, out FastList<Op> ops, out int offset, out EvalStack evalStack)
    {
        LoadProgram(function, ctx, out unit, out ops);
        ctx.CallerUnitId = ctx.UnitId;
        ctx.UnitId = function.UnitId;

        var layout = unit.Layouts[function.FunctionId];
        offset = layout.Address;
        evalStack = new EvalStack(layout.StackSize);
        ctx.CatchMarks.Push(function.CatchMarks); //Makes sense for iterators

        if (function.FunctionId == 0)
        {
            locals = ctx.RuntimeContext.Units[function.UnitId];
        }
        else if (function.Locals is not null)
        {
            locals = function.Locals;
            offset = function.PreviousOffset;
        }
    }

    private static void LoadProgram(SpellkitNativeFunction function, ExecutionContext ctx, out Unit unit, out FastList<Op> ops)
    {
        unit = ctx.RuntimeContext.Composition.Units[function.UnitId];
        ops = unit.Ops;
    }

    private static bool ResumeCallerOrFinish(ExecutionContext ctx, EvalStack currentStack,
        ref SpellkitNativeFunction function, ref SpellkitObject[] locals, ref int offset, ref EvalStack evalStack,
        out SpellkitObject result)
    {
        if (SpellkitMachineHelpers.TryResumeCaller(ctx, currentStack, out result, out function, out locals, out offset, out evalStack))
        {
            return true;
        }

        ctx.CallCnt--;
        return false;
    }

    internal static void FillDefaults(SpellkitObject[] locals, SpellkitFunction callFun, ExecutionContext ctx) =>
        SpellkitMachineHelpers.FillDefaults(locals, callFun, ctx);

    private static bool TryCall(ExecutionContext ctx, int offset, ref SpellkitObject? arg1, ref SpellkitObject? arg2,
        ref SpellkitNativeFunction function, ref SpellkitObject[] locals, ref EvalStack evalStack)
    {
        if (ReferenceEquals(ctx.Error, SpellkitFunction.CallbackPending))
        {
            ctx.CallStack.Push(new Caller(function, offset, evalStack, locals));
            function = (SpellkitNativeFunction)ctx.CallBackFunction!;
            ctx.CallBackFunction = null;
            ctx.Error = null;
            locals = function.CreateLocals(ctx);
            if (arg1 is not null)
            {
                locals[0] = arg1;
            }

            if (arg2 is not null)
            {
                locals[1] = arg2;
            }

            arg1 = null;
            arg2 = null;
            return true;
        }

        return false;
    }

    private static int ThrowIf(ExecutionContext ctx, int offset, ref SpellkitNativeFunction function, ref SpellkitObject[] locals, ref EvalStack evalStack)
    {
        var err = ctx.Error!;
        var dump = Dump(ctx, offset, function);

        if (FindCatch(ctx, ref function, ref locals, ref evalStack, out var address))
        {
            ctx.ErrorDump = dump;
            var trace = BuildErrorTrace(ctx, ctx.ErrorDump);
            ctx.Error = AttachTrace(err, trace);
            return address;
        }
        else
        {
            var cs = BuildErrorTrace(ctx, dump);
            err = AttachTrace(err, cs);
            ctx.Error = null;
            ctx.ErrorDump = null;
            ctx.Trace = null;
            throw new SpellkitCodeException(err, cs, null);
        }
    }

    private static bool FindCatch(ExecutionContext ctx, ref SpellkitNativeFunction function, ref SpellkitObject[] locals, ref EvalStack evalStack, out int offset)
    {
        CatchMark mark = default;
        Stack<CatchMark> cm;
        var idx = 1;
        offset = 0;

        while (ctx.CatchMarks.TryPeek(idx++, out cm))
        {
            if (cm is not null && cm.Count > 0)
            {
                mark = cm.Peek();
                break;
            }
        }

        if (mark.Offset == 0)
        {
            return false;
        }

        Caller? cp = null;

        while (ctx.CallStack.Count > mark.StackOffset)
        {
            cp = ctx.CallStack.Pop();

            if (ReferenceEquals(cp, Caller.External))
            {
                return false;
            }
        }

        cm.Pop();

        if (cp is not null)
        {
            function = cp.Function;
            locals = cp.Locals;
            evalStack = cp.EvalStack;
        }

        offset = mark.Offset;
        return true;
    }

    private static Stack<StackPoint> Dump(ExecutionContext ctx, int offset, SpellkitNativeFunction function)
    {
        var dump = ctx.ErrorDump;

        if (dump is null)
        {
            var callStack = ctx.CallStack.Clone();
            dump = new Stack<StackPoint>();
            var sp = StackPoint.Empty;

            for (var i = 0; i < callStack.Count; i++)
            {
                var cm = callStack[i];

                if (ReferenceEquals(cm, Caller.Root))
                {
                    continue;
                }

                if (ReferenceEquals(cm, Caller.External))
                {
                    sp = StackPoint.External;
                }
                else
                {
                    sp = new(cm.Offset, cm.Function.UnitId);
                }

                dump.Push(sp);
            }

            if (sp.IsEmpty || sp.Offset != offset || sp.UnitId != function.UnitId)
            {
                dump.Push(new(offset, function.UnitId));
            }
        }

        return dump;
    }

    private static CallStackTrace BuildErrorTrace(ExecutionContext ctx, Stack<StackPoint> dump) =>
        ctx.Trace ?? new SpellkitDebugger(ctx.RuntimeContext.Composition)
            .BuildCallStack(new Stack<StackPoint>(dump.Reverse()));

    private static SpellkitObject AttachTrace(SpellkitObject err, CallStackTrace? trace)
    {
        if (err is SpellkitExceptionObject ex && ex.Trace is null && trace is not null)
        {
            ex.WithTrace(trace);
        }

        return err;
    }

    public static IEnumerable<RuntimeVar> DumpVariables(RuntimeContext rtx)
    {
        foreach (var v in rtx.Composition.Units[0].GlobalScope!.EnumerateVars())
        {
            yield return new(v.Key, rtx.Units[0][v.Value.Address]);
        }
    }

    internal static (SpellkitObject err, CallStackTrace? trace) GetErrorInformation(SpellkitFunction func, Exception ex)
    {
        if (ex is SpellkitCodeException err)
        {
            return (err.Error, err.CallTrace);
        }

        if (ex.InnerException is not null)
        {
            return GetErrorInformation(func, ex.InnerException);
        }

        var functionName = func.Self is null ? func.FunctionName
            : $"{func.Self.TypeName}.{func.FunctionName}";
        return (ErrorGenerators.RuntimeException(SpellkitError.ExternalFunctionFailure, functionName, ex.Message), null);
    }
}

internal record struct RuntimeVar(string Name, SpellkitObject Value);
