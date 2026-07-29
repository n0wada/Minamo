using Spellkit.Compiler;
using Spellkit.Debug;
using Spellkit.Linker;
using Spellkit.Runtime.Types;
using Spellkit.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Spellkit.Runtime;

internal static partial class SpellkitMachineHelpers
{
    internal static bool TryPrepareFunction(EvalStack evalStack, FastList<SpellkitTypeInfo> types, ExecutionContext ctx,
        int argumentCount, out SpellkitFunction callFun)
    {
        SpellkitObject candidate;

        while (true)
        {
            candidate = evalStack.Peek();

            if (candidate.TypeId == SpellkitTypeCodes.Function)
            {
                callFun = (SpellkitFunction)candidate;
                break;
            }

            if (candidate.TypeId == SpellkitTypeCodes.TypeInfo && candidate is SpellkitTypeInfo typeInfo)
            {
                candidate = typeInfo.GetStaticMember(typeInfo.ReflectedTypeName, ctx);
            }
            else
            {
                candidate = types[candidate.TypeId].GetInstanceMember(candidate, Builtins.Call, ctx);

                if (!ctx.HasErrors && candidate.TypeId != SpellkitTypeCodes.Function)
                {
                    ctx.InvalidType(SpellkitTypeCodes.Function, candidate);
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
                varArgs: argumentCount == 0 ? null : new SpellkitObject[argumentCount]
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
        SpellkitFunction function, string argumentName, ExecutionContext ctx)
    {
        var index = function.GetParameterIndex(argumentName);

        if (index == -1)
        {
            if (arguments.VarArgsIndex > -1 && function.Parameters.Length == 1)
            {
                arguments.VarArgs![arguments.VarArgsSize++] = new SpellkitLabel(argumentName, evalStack.Pop());
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

    internal static void PrepareStandardCall(EvalStack evalStack, ExecutionContext ctx, SpellkitNativeFunction function,
        int offset, SpellkitObject[] locals, int argumentCount, out SpellkitNativeFunction nextFunction, out SpellkitObject[] nextLocals)
    {
        ctx.CallStack.Push(new Caller(function, offset, evalStack, locals));
        nextFunction = (SpellkitNativeFunction)evalStack.Pop();
        nextLocals = nextFunction.CreateLocals(ctx);

        for (var i = 0; i < argumentCount; i++)
        {
            nextLocals[i] = evalStack.Pop();
        }
    }

    internal static bool ExecuteFunctionCall(EvalStack evalStack, ExecutionContext ctx, SpellkitNativeFunction function,
        int offset, SpellkitObject[] locals, int argumentCount, out SpellkitNativeFunction? nextFunction, out SpellkitObject[]? nextLocals)
    {
        var callFun = (SpellkitFunction)evalStack.Pop();

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
            nextFunction = (SpellkitNativeFunction)callFun;
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

    internal static bool ExecutePositionalFunctionCall(EvalStack evalStack, FastList<SpellkitTypeInfo> types, ExecutionContext ctx,
        SpellkitNativeFunction function, int offset, SpellkitObject[] locals, int argumentCount,
        out SpellkitNativeFunction? nextFunction, out SpellkitObject[]? nextLocals)
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

    internal static bool ExecuteNamedMemberCall(OpCode code, EvalStack evalStack, FastList<SpellkitTypeInfo> types,
        HashString memberName, ExecutionContext ctx, SpellkitNativeFunction function, int offset, SpellkitObject[] locals,
        int argumentCount, out SpellkitNativeFunction? nextFunction, out SpellkitObject[]? nextLocals)
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

    private static SpellkitObject[] PopPositionalArguments(EvalStack evalStack, int argumentCount)
    {
        if (argumentCount == 0)
        {
            return Array.Empty<SpellkitObject>();
        }

        var arguments = new SpellkitObject[argumentCount];
        for (var i = argumentCount - 1; i >= 0; i--)
        {
            arguments[i] = evalStack.Pop();
        }

        return arguments;
    }

    private static void AssignIndexedArgument(ExecutionContext.ArgContainer arguments, SpellkitObject value, int index)
    {
        if (arguments.VarArgsIndex > -1 && index >= arguments.VarArgsIndex)
        {
            arguments.VarArgs![arguments.VarArgsSize++] = value;
            return;
        }

        arguments.Locals[index] = value;
    }

    internal static bool TryResumeCaller(ExecutionContext ctx, EvalStack evalStack,
        out SpellkitObject result, out SpellkitNativeFunction nextFunction, out SpellkitObject[] nextLocals,
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

    internal static void PushArgument(ExecutionContext.ArgContainer container, SpellkitObject value, ExecutionContext ctx)
    {
        if (container.VarArgsSize != 0)
        {
            ctx.TooManyArguments();
        }

        if (value.TypeId is SpellkitTypeCodes.Array)
        {
            var xs = (SpellkitCollection)value;
            container.VarArgs = xs.ToArray();
            container.VarArgsSize = container.VarArgs.Length;
        }
        else if (value.TypeId is SpellkitTypeCodes.Tuple)
        {
            var xs = (SpellkitTuple)value;
            container.VarArgs = xs.IsVarArg ? xs.UnsafeAccess() : xs.GetValuesWithLabels();
            container.VarArgsSize = container.VarArgs.Length;
        }
        else if (value.TypeId is SpellkitTypeCodes.Iterator or SpellkitTypeCodes.Set)
        {
            var xs = SpellkitIterator.ToEnumerable(ctx, value).ToArray();

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

    internal static SpellkitObject CallExternalFunction(SpellkitFunction func, ExecutionContext ctx)
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
        catch (SpellkitExecutionLimitException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            (ctx.Error, ctx.Trace) = SpellkitMachine.GetErrorInformation(func, ex);
            return SpellkitNil.Instance;
        }
    }

    internal static void FillDefaults(ExecutionContext.ArgContainer cont, SpellkitFunction callFun, ExecutionContext ctx)
    {
        var locals = cont.Locals;

        if (callFun.VarArgIndex > -1)
        {
            locals[callFun.VarArgIndex] = cont.VarArgs is null ? SpellkitTuple.Empty
                : new SpellkitTuple(cont.VarArgs, cont.VarArgsSize);
        }

        FillDefaults(cont.Locals, callFun, ctx);
    }

    internal static void FillDefaults(SpellkitObject[] locals, SpellkitFunction callFun, ExecutionContext ctx)
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
