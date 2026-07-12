using Spellkit.Compiler;
using Spellkit.Debug;
using Spellkit.Linker;
using Spellkit.Runtime.Types;
using Spellkit.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Spellkit.Runtime;

internal static partial class SpkMachineHelpers
{
    internal static bool TryPrepareFunction(EvalStack evalStack, FastList<SpkTypeInfo> types, ExecutionContext ctx,
        int argumentCount, out SpkFunction callFun)
    {
        SpkObject candidate;

        while (true)
        {
            candidate = evalStack.Peek();

            if (candidate.TypeId == Spk.Function)
            {
                callFun = (SpkFunction)candidate;
                break;
            }

            if (candidate.TypeId == Spk.TypeInfo && candidate is SpkTypeInfo typeInfo)
            {
                candidate = typeInfo.GetStaticMember(typeInfo.ReflectedTypeName, ctx);
            }
            else
            {
                candidate = types[candidate.TypeId].GetInstanceMember(candidate, Builtins.Call, ctx);

                if (!ctx.HasErrors && candidate.TypeId != Spk.Function)
                {
                    ctx.InvalidType(Spk.Function, candidate);
                }
            }

            if (ctx.Error is not null)
            {
                callFun = null!;
                return false;
            }

            evalStack.Replace(candidate);
        }

        if (callFun.VarArgIndex == -1)
        {
            if (argumentCount > callFun.Parameters.Length)
            {
                ctx.TooManyArguments(callFun.FunctionName, callFun.Parameters.Length, argumentCount);
                return false;
            }

            ctx.PushArguments(callFun.CreateLocals(ctx), -1);
        }
        else
        {
            ctx.PushArguments(
                locals: callFun.CreateLocals(ctx),
                varArgsIndex: callFun.VarArgIndex,
                varArgs: argumentCount == 0 ? null : new SpkObject[argumentCount]
            );
        }

        return true;
    }

    internal static void AssignIndexedArgument(ExecutionContext.ArgContainer arguments, EvalStack evalStack, int index)
    {
        if (arguments.VarArgsIndex > -1 && index >= arguments.VarArgsIndex)
        {
            arguments.VarArgs![arguments.VarArgsSize++] = evalStack.Pop();
            return;
        }

        arguments.Locals[index] = evalStack.Pop();
    }

    internal static bool TryAssignNamedArgument(ExecutionContext.ArgContainer arguments, EvalStack evalStack,
        SpkFunction function, string argumentName, ExecutionContext ctx)
    {
        var index = function.GetParameterIndex(argumentName);

        if (index == -1)
        {
            if (arguments.VarArgsIndex > -1 && function.Parameters.Length == 1)
            {
                arguments.VarArgs![arguments.VarArgsSize++] = new SpkLabel(argumentName, evalStack.Pop());
                return true;
            }

            ctx.ArgumentNotFound(function.FunctionName, argumentName);
            return false;
        }

        if (index == arguments.VarArgsIndex)
        {
            PushArgument(arguments, evalStack.Pop(), ctx);
            return ctx.Error is null;
        }

        if (arguments.Locals[index] is not null)
        {
            ctx.MultipleValuesForArgument(function.FunctionName, argumentName);
            return false;
        }

        arguments.Locals[index] = evalStack.Pop();
        return true;
    }

    internal static void PrepareStandardCall(EvalStack evalStack, ExecutionContext ctx, SpkNativeFunction function,
        int offset, SpkObject[] locals, int argumentCount, out SpkNativeFunction nextFunction, out SpkObject[] nextLocals)
    {
        ctx.CallStack.Push(new Caller(function, offset, evalStack, locals));
        nextFunction = (SpkNativeFunction)evalStack.Pop();
        nextLocals = nextFunction.CreateLocals(ctx);

        for (var i = 0; i < argumentCount; i++)
        {
            nextLocals[i] = evalStack.Pop();
        }
    }

    internal static bool ExecuteFunctionCall(EvalStack evalStack, ExecutionContext ctx, SpkNativeFunction function,
        int offset, SpkObject[] locals, int argumentCount, out SpkNativeFunction? nextFunction, out SpkObject[]? nextLocals)
    {
        var callFun = (SpkFunction)evalStack.Pop();

        if (argumentCount != callFun.Parameters.Length || callFun.VarArgIndex > -1)
        {
            FillDefaults(ctx.PeekArguments(), callFun, ctx);
            if (ctx.Error is not null)
            {
                nextFunction = null;
                nextLocals = null;
                return false;
            }
        }

        ctx.CallStack.Push(new Caller(function, offset, evalStack, locals));

        if (!callFun.IsExternal)
        {
            nextFunction = (SpkNativeFunction)callFun;
            nextLocals = ctx.PopArguments().Locals;
            return true;
        }

        var result = CallExternalFunction(callFun, ctx);
        if (ctx.Error is null)
        {
            evalStack.Push(result);
            ctx.CallStack.Pop();
        }

        nextFunction = null;
        nextLocals = null;
        return false;
    }

    internal static bool ExecutePositionalFunctionCall(EvalStack evalStack, FastList<SpkTypeInfo> types, ExecutionContext ctx,
        SpkNativeFunction function, int offset, SpkObject[] locals, int argumentCount,
        out SpkNativeFunction? nextFunction, out SpkObject[]? nextLocals)
    {
        var arguments = PopPositionalArguments(evalStack, argumentCount);

        if (!TryPrepareFunction(evalStack, types, ctx, argumentCount, out _))
        {
            nextFunction = null;
            nextLocals = null;
            return false;
        }

        var container = ctx.PeekArguments();
        for (var i = 0; i < arguments.Length; i++)
        {
            AssignIndexedArgument(container, arguments[i], i);
        }

        return ExecuteFunctionCall(evalStack, ctx, function, offset, locals, argumentCount, out nextFunction, out nextLocals);
    }

    internal static bool ExecuteNamedMemberCall(OpCode code, EvalStack evalStack, FastList<SpkTypeInfo> types,
        HashString memberName, ExecutionContext ctx, SpkNativeFunction function, int offset, SpkObject[] locals,
        int argumentCount, out SpkNativeFunction? nextFunction, out SpkObject[]? nextLocals)
    {
        if (code is not (OpCode.CallMember or OpCode.CallStatic))
        {
            throw new InvalidOperationException($"Unsupported member call opcode: {code}.");
        }

        var arguments = PopPositionalArguments(evalStack, argumentCount);

        if (!ExecuteNamedMemberAccess(OpCode.LoadMember, evalStack, types, memberName, ctx, out _))
        {
            nextFunction = null;
            nextLocals = null;
            return false;
        }

        if (!TryPrepareFunction(evalStack, types, ctx, argumentCount, out _))
        {
            nextFunction = null;
            nextLocals = null;
            return false;
        }

        var container = ctx.PeekArguments();
        for (var i = 0; i < arguments.Length; i++)
        {
            AssignIndexedArgument(container, arguments[i], i);
        }

        return ExecuteFunctionCall(evalStack, ctx, function, offset, locals, argumentCount, out nextFunction, out nextLocals);
    }

    private static SpkObject[] PopPositionalArguments(EvalStack evalStack, int argumentCount)
    {
        if (argumentCount == 0)
        {
            return Array.Empty<SpkObject>();
        }

        var arguments = new SpkObject[argumentCount];
        for (var i = argumentCount - 1; i >= 0; i--)
        {
            arguments[i] = evalStack.Pop();
        }

        return arguments;
    }

    private static void AssignIndexedArgument(ExecutionContext.ArgContainer arguments, SpkObject value, int index)
    {
        if (arguments.VarArgsIndex > -1 && index >= arguments.VarArgsIndex)
        {
            arguments.VarArgs![arguments.VarArgsSize++] = value;
            return;
        }

        arguments.Locals[index] = value;
    }

    internal static bool TryResumeCaller(ExecutionContext ctx, EvalStack evalStack,
        out SpkObject result, out SpkNativeFunction nextFunction, out SpkObject[] nextLocals,
        out int nextOffset, out EvalStack nextEvalStack)
    {
        result = evalStack.Pop();
        nextFunction = null!;
        nextLocals = null!;
        nextOffset = 0;
        nextEvalStack = null!;

        if (ctx.CallStack.Count == 0)
        {
            return false;
        }

        var caller = ctx.CallStack.Pop();
        if (ReferenceEquals(caller, Caller.External))
        {
            return false;
        }

        caller.EvalStack.Push(result);
        nextFunction = caller.Function;
        nextLocals = caller.Locals;
        nextOffset = caller.Offset;
        nextEvalStack = caller.EvalStack;
        return true;
    }

    internal static void PushArgument(ExecutionContext.ArgContainer container, SpkObject value, ExecutionContext ctx)
    {
        if (container.VarArgsSize != 0)
        {
            ctx.TooManyArguments();
        }

        if (value.TypeId is Spk.Array)
        {
            var xs = (SpkCollection)value;
            container.VarArgs = xs.ToArray();
            container.VarArgsSize = container.VarArgs.Length;
        }
        else if (value.TypeId is Spk.Tuple)
        {
            var xs = (SpkTuple)value;
            container.VarArgs = xs.IsVarArg ? xs.UnsafeAccess() : xs.GetValuesWithLabels();
            container.VarArgsSize = container.VarArgs.Length;
        }
        else if (value.TypeId is Spk.Iterator or Spk.Set)
        {
            var xs = SpkIterator.ToEnumerable(ctx, value).ToArray();

            if (ctx.HasErrors)
            {
                return;
            }

            container.VarArgs = xs;
            container.VarArgsSize = xs.Length;
        }
        else
        {
            container.VarArgs![container.VarArgsSize++] = value;
        }
    }

    internal static SpkObject CallExternalFunction(SpkFunction func, ExecutionContext ctx)
    {
        try
        {
            return func.FastCall(ctx, ctx.PopArguments().Locals);
        }
        catch (IterationException)
        {
            return ctx.CollectionModified();
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
        catch (TimeoutException)
        {
            return ctx.Timeout();
        }
        catch (SpkExecutionLimitException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            (ctx.Error, ctx.Trace) = SpkMachine.GetErrorInformation(func, ex);
            return SpkNil.Instance;
        }
    }

    internal static void FillDefaults(ExecutionContext.ArgContainer cont, SpkFunction callFun, ExecutionContext ctx)
    {
        var locals = cont.Locals;

        if (callFun.VarArgIndex > -1)
        {
            locals[callFun.VarArgIndex] = cont.VarArgs is null ? SpkTuple.Empty
                : new SpkTuple(cont.VarArgs, cont.VarArgsSize);
        }

        FillDefaults(cont.Locals, callFun, ctx);
    }

    internal static void FillDefaults(SpkObject[] locals, SpkFunction callFun, ExecutionContext ctx)
    {
        var pars = callFun.Parameters;

        for (var i = 0; i < pars.Length; i++)
        {
            if (locals[i] is null)
            {
                var v = pars[i].Value;

                if (v is null)
                {
                    ctx.RequiredArgumentMissing(callFun.FunctionName, pars[i].Name);
                    return;
                }

                locals[i] = v;
            }
        }
    }
}
