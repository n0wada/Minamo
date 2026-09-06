using Minamo.Compiler;
using Minamo.Debug;
using Minamo.Linker;
using Minamo.Runtime.Types;
using Minamo.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Minamo.Runtime;

internal static partial class MinamoMachineHelpers
{
    internal static bool ExecuteBinaryOperation(OpCode code, EvalStack evalStack, FastList<MinamoTypeInfo> types,
        ExecutionContext ctx, out MinamoObject second)
    {
        second = evalStack.Pop();
        var first = evalStack.Peek();

        evalStack.Replace(code switch
        {
            OpCode.Add => types[first.TypeId].Add(ctx, first, second),
            OpCode.Sub => types[first.TypeId].Sub(ctx, first, second),
            OpCode.Mul => types[first.TypeId].Mul(ctx, first, second),
            OpCode.Div => types[first.TypeId].Div(ctx, first, second),
            OpCode.Remainder => types[first.TypeId].Rem(ctx, first, second),
            OpCode.Equal => types[first.TypeId].Eq(ctx, first, second),
            OpCode.NotEqual => types[first.TypeId].Neq(ctx, first, second),
            OpCode.GreaterThan => types[first.TypeId].Gt(ctx, first, second),
            OpCode.LessThan => types[first.TypeId].Lt(ctx, first, second),
            OpCode.GreaterThanOrEqual => types[first.TypeId].Gte(ctx, first, second),
            OpCode.LessThanOrEqual => types[first.TypeId].Lte(ctx, first, second),
            _ => throw new InvalidOperationException($"Unsupported binary opcode: {code}.")
        });

        return ctx.Error is null;
    }

    internal static bool ExecuteUnaryOperation(OpCode code, EvalStack evalStack, FastList<MinamoTypeInfo> types,
        ExecutionContext ctx)
    {
        var first = evalStack.Peek();

        evalStack.Replace(code switch
        {
            OpCode.Negate => types[first.TypeId].Neg(ctx, first),
            OpCode.Plus => types[first.TypeId].Plus(ctx, first),
            OpCode.Not => types[first.TypeId].Not(ctx, first),
            OpCode.Length => types[first.TypeId].Length(ctx, first),
            _ => throw new InvalidOperationException($"Unsupported unary opcode: {code}.")
        });

        return ctx.Error is null;
    }

    internal static bool ExecuteNamedMemberAccess(OpCode code, EvalStack evalStack, FastList<MinamoTypeInfo> types,
        HashString memberName, ExecutionContext ctx, out MinamoObject target)
    {
        target = evalStack.Peek();

        switch (code)
        {
            case OpCode.HasMember:
                if (target.TypeId == MinamoTypeCodes.TypeInfo)
                {
                    evalStack.Replace(((MinamoTypeInfo)target).HasStaticMember(memberName, ctx));
                }
                else
                {
                    evalStack.Replace(types[target.TypeId].HasInstanceMember(target, memberName, ctx));
                }

                break;
            case OpCode.LoadMember:
                if (target.TypeId == MinamoTypeCodes.TypeInfo)
                {
                    evalStack.Replace(((MinamoTypeInfo)target).GetStaticMember(memberName, ctx));
                }
                else
                {
                    evalStack.Replace(types[target.TypeId].GetInstanceMember(target, memberName, ctx));
                }

                break;
            default:
                throw new InvalidOperationException($"Unsupported member access opcode: {code}.");
        }

        return ctx.Error is null;
    }

    internal static bool ExecuteTypeMemberMutation(OpCode code, EvalStack evalStack, ExecutionContext ctx,
        HashString memberName, out MinamoObject first, out MinamoObject second)
    {
        first = evalStack.Pop();
        second = evalStack.Pop();

        switch (code)
        {
            case OpCode.StoreStaticMember:
                ((MinamoTypeInfo)first).SetStaticMember(ctx, memberName, (MinamoFunction)second);
                break;
            case OpCode.StoreMember:
                ((MinamoTypeInfo)first).SetInstanceMember(ctx, memberName, (MinamoFunction)second);
                break;
            case OpCode.ApplyMixin:
                ((MinamoTypeInfo)second).Mixin(ctx, (MinamoTypeInfo)first);
                break;
            default:
                throw new InvalidOperationException($"Unsupported type-member mutation opcode: {code}.");
        }

        return ctx.Error is null;
    }

    internal static bool ExecuteIndexedAccess(EvalStack evalStack, FastList<MinamoTypeInfo> types,
        ExecutionContext ctx, out MinamoObject first, out MinamoObject second)
    {
        second = evalStack.Pop();
        first = evalStack.Pop();
        evalStack.Push(types[first.TypeId].Get(ctx, first, second));
        return ctx.Error is null;
    }

    internal static bool ExecuteRawIndexedAccess(EvalStack evalStack, FastList<MinamoTypeInfo> types,
        ExecutionContext ctx, out MinamoObject first, out MinamoObject second)
    {
        second = evalStack.Pop();
        first = evalStack.Pop();
        evalStack.Push(first is MinamoClass cls
            ? cls.Fields.GetItem(ctx, second)
            : types[first.TypeId].RawGet(ctx, first, second));
        return ctx.Error is null;
    }

    internal static bool ExecutePrivateGet(EvalStack evalStack, string memberName, ExecutionContext ctx,
        out MinamoObject target)
    {
        target = evalStack.Peek();

        if (target is MinamoClass cls)
        {
            evalStack.Replace(cls.GetPrivate(ctx, memberName));
        }
        else
        {
            ctx.IndexOutOfRange(memberName);
        }

        return ctx.Error is null;
    }

    internal static bool ExecuteIndexedSet(EvalStack evalStack, FastList<MinamoTypeInfo> types,
        ExecutionContext ctx, out MinamoObject first, out MinamoObject second, out MinamoObject third)
    {
        second = evalStack.Pop();
        first = evalStack.Pop();
        third = evalStack.Pop();
        evalStack.Push(types[first.TypeId].Set(ctx, first, second, third));
        return ctx.Error is null;
    }

    internal static bool ExecuteRawIndexedSet(EvalStack evalStack, FastList<MinamoTypeInfo> types,
        ExecutionContext ctx, out MinamoObject first, out MinamoObject second, out MinamoObject third)
    {
        second = evalStack.Pop();
        first = evalStack.Pop();
        third = evalStack.Pop();
        if (first is MinamoClass cls)
        {
            cls.Fields.SetItem(ctx, second, third);
            evalStack.Push(MinamoNil.Instance);
        }
        else
        {
            evalStack.Push(types[first.TypeId].RawSet(ctx, first, second, third));
        }

        return ctx.Error is null;
    }

    internal static bool ExecutePrivateSet(EvalStack evalStack, string memberName, ExecutionContext ctx,
        out MinamoObject value, out MinamoObject target)
    {
        target = evalStack.Pop();
        value = evalStack.Peek();

        if (target is MinamoClass cls)
        {
            evalStack.Replace(cls.SetPrivate(ctx, memberName, value));
        }
        else
        {
            ctx.IndexOutOfRange(memberName);
        }

        return ctx.Error is null;
    }

    internal static bool ExecuteContains(EvalStack evalStack, FastList<MinamoTypeInfo> types,
        ExecutionContext ctx, MinamoObject member, out MinamoObject first)
    {
        first = evalStack.Peek();
        evalStack.Replace(types[first.TypeId].In(ctx, first, member));
        return ctx.Error is null;
    }

    internal static bool ExecuteStringConversion(EvalStack evalStack, FastList<MinamoTypeInfo> types,
        ExecutionContext ctx)
    {
        var first = evalStack.Peek();
        evalStack.Replace(types[first.TypeId].ToString(ctx, first));
        return ctx.Error is null;
    }

    internal static void ExecuteTypeCheck(EvalStack evalStack, FastList<MinamoTypeInfo> types,
        out MinamoObject first, out MinamoObject second)
    {
        first = evalStack.Pop();
        second = evalStack.Pop();
        evalStack.Push(types[second.TypeId].CheckType((MinamoTypeInfo)first));
    }

    internal static void ExecuteConstructorCheck(EvalStack evalStack, HashString constructorName, out MinamoObject target)
    {
        target = evalStack.Peek();
        evalStack.Replace(target is IProduction production && production.Constructor == constructorName);
    }

    internal static MinamoIterator ExecuteIteratorCreation(MinamoNativeFunction function, int functionId, MinamoObject[] locals) =>
        MinamoIterator.Create(function.UnitId, functionId, function.Captures, locals);

    internal static MinamoNativeFunction ExecuteFunctionCreation(Unit unit, MinamoNativeFunction function, MinamoObject[] locals,
        int functionId, int? defaultArgIndex = null) =>
        defaultArgIndex is int variadicIndex
            ? MinamoNativeFunction.Create(unit.Symbols.Functions[functionId], unit.Id, functionId, function.Captures, locals, variadicIndex)
            : MinamoNativeFunction.Create(unit.Symbols.Functions[functionId], unit.Id, functionId, function.Captures, locals);

    internal static void ExecuteObjectCreation(EvalStack evalStack, Unit unit, string constructorName,
        out MinamoObject first, out MinamoObject second, out MinamoObject third)
    {
        second = evalStack.Pop();
        first = evalStack.Pop();
        third = evalStack.Pop();
        evalStack.Push(new MinamoClass((MinamoClassInfo)second, constructorName, (MinamoTuple)first, (MinamoTuple)third, unit));
    }

    internal static MinamoClassInfo ExecuteTypeCreation(FastList<MinamoTypeInfo> types, string typeName)
    {
        var clsInfo = new MinamoClassInfo(typeName, types.Count);
        types.Add(clsInfo);
        return clsInfo;
    }

    internal static void ExecuteCastRegistration(EvalStack evalStack, out MinamoObject sourceType, out MinamoObject targetType)
    {
        sourceType = evalStack.Pop();
        targetType = evalStack.Pop();
        ((MinamoTypeInfo)sourceType).SetCastFunction((MinamoTypeInfo)targetType, (MinamoFunction)evalStack.Pop());
    }

    internal static bool ExecuteCast(EvalStack evalStack, FastList<MinamoTypeInfo> types,
        ExecutionContext ctx, out MinamoObject sourceType, out MinamoObject target)
    {
        sourceType = evalStack.Pop();
        target = evalStack.Peek();
        evalStack.Replace(types[sourceType.TypeId].Cast(ctx, sourceType, target));
        return ctx.Error is null;
    }

    internal static MinamoObject MakeTuple(EvalStack stack, int size, bool vararg)
    {
        var arr = new MinamoObject[size];
        var mutable = false;

        for (var i = 0; i < size; i++)
        {
            var e = stack.Pop();
            arr[arr.Length - i - 1] = e;

            if (!mutable && e is MinamoLabel la && la.Mutable)
            {
                mutable = true;
            }
        }

        return new MinamoTuple(arr, mutable, vararg);
    }

    internal static MinamoObject MakeDictionary(EvalStack stack, int size)
    {
        var dict = new MinamoDictionary();

        for (var i = 0; i < size; i++)
        {
            if (stack.Pop() is MinamoLabel lab)
            {
                dict[new MinamoString(lab.Label)] = lab.Value;
            }
        }

        return dict;
    }

}
