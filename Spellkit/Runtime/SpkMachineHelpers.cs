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
    internal static bool ExecuteBinaryOperation(OpCode code, EvalStack evalStack, FastList<SpkTypeInfo> types,
        ExecutionContext ctx, out SpkObject second)
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

    internal static bool ExecuteUnaryOperation(OpCode code, EvalStack evalStack, FastList<SpkTypeInfo> types,
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

    internal static bool ExecuteNamedMemberAccess(OpCode code, EvalStack evalStack, FastList<SpkTypeInfo> types,
        HashString memberName, ExecutionContext ctx, out SpkObject target)
    {
        target = evalStack.Peek();

        switch (code)
        {
            case OpCode.HasMember:
                if (target.TypeId == Spk.TypeInfo)
                {
                    evalStack.Replace(((SpkTypeInfo)target).HasStaticMember(memberName, ctx));
                }
                else
                {
                    evalStack.Replace(types[target.TypeId].HasInstanceMember(target, memberName, ctx));
                }

                break;
            case OpCode.LoadMember:
                if (target.TypeId == Spk.TypeInfo)
                {
                    evalStack.Replace(((SpkTypeInfo)target).GetStaticMember(memberName, ctx));
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
        HashString memberName, out SpkObject first, out SpkObject second)
    {
        first = evalStack.Pop();
        second = evalStack.Pop();

        switch (code)
        {
            case OpCode.StoreStaticMember:
                ((SpkTypeInfo)first).SetStaticMember(ctx, memberName, (SpkFunction)second);
                break;
            case OpCode.StoreMember:
                ((SpkTypeInfo)first).SetInstanceMember(ctx, memberName, (SpkFunction)second);
                break;
            case OpCode.ApplyMixin:
                ((SpkTypeInfo)second).Mixin(ctx, (SpkTypeInfo)first);
                break;
            default:
                throw new InvalidOperationException($"Unsupported type-member mutation opcode: {code}.");
        }

        return ctx.Error is null;
    }

    internal static bool ExecuteIndexedAccess(EvalStack evalStack, FastList<SpkTypeInfo> types,
        ExecutionContext ctx, out SpkObject first, out SpkObject second)
    {
        second = evalStack.Pop();
        first = evalStack.Pop();
        evalStack.Push(types[first.TypeId].Get(ctx, first, second));
        return ctx.Error is null;
    }

    internal static bool ExecuteRawIndexedAccess(EvalStack evalStack, FastList<SpkTypeInfo> types,
        ExecutionContext ctx, out SpkObject first, out SpkObject second)
    {
        second = evalStack.Pop();
        first = evalStack.Pop();
        evalStack.Push(first is SpkClass cls
            ? cls.Fields.GetItem(ctx, second)
            : types[first.TypeId].RawGet(ctx, first, second));
        return ctx.Error is null;
    }

    internal static bool ExecutePrivateGet(EvalStack evalStack, string memberName, ExecutionContext ctx,
        out SpkObject target)
    {
        target = evalStack.Peek();

        if (target is SpkClass cls)
        {
            evalStack.Replace(cls.GetPrivate(ctx, memberName));
        }
        else
        {
            ctx.IndexOutOfRange(memberName);
        }

        return ctx.Error is null;
    }

    internal static bool ExecuteIndexedSet(EvalStack evalStack, FastList<SpkTypeInfo> types,
        ExecutionContext ctx, out SpkObject first, out SpkObject second, out SpkObject third)
    {
        second = evalStack.Pop();
        first = evalStack.Pop();
        third = evalStack.Pop();
        evalStack.Push(types[first.TypeId].Set(ctx, first, second, third));
        return ctx.Error is null;
    }

    internal static bool ExecuteRawIndexedSet(EvalStack evalStack, FastList<SpkTypeInfo> types,
        ExecutionContext ctx, out SpkObject first, out SpkObject second, out SpkObject third)
    {
        second = evalStack.Pop();
        first = evalStack.Pop();
        third = evalStack.Pop();
        if (first is SpkClass cls)
        {
            cls.Fields.SetItem(ctx, second, third);
            evalStack.Push(SpkNil.Instance);
        }
        else
        {
            evalStack.Push(types[first.TypeId].RawSet(ctx, first, second, third));
        }

        return ctx.Error is null;
    }

    internal static bool ExecutePrivateSet(EvalStack evalStack, string memberName, ExecutionContext ctx,
        out SpkObject value, out SpkObject target)
    {
        target = evalStack.Pop();
        value = evalStack.Peek();

        if (target is SpkClass cls)
        {
            evalStack.Replace(cls.SetPrivate(ctx, memberName, value));
        }
        else
        {
            ctx.IndexOutOfRange(memberName);
        }

        return ctx.Error is null;
    }

    internal static bool ExecuteContains(EvalStack evalStack, FastList<SpkTypeInfo> types,
        ExecutionContext ctx, SpkObject member, out SpkObject first)
    {
        first = evalStack.Peek();
        evalStack.Replace(types[first.TypeId].In(ctx, first, member));
        return ctx.Error is null;
    }

    internal static bool ExecuteStringConversion(EvalStack evalStack, FastList<SpkTypeInfo> types,
        ExecutionContext ctx)
    {
        var first = evalStack.Peek();
        evalStack.Replace(types[first.TypeId].ToString(ctx, first));
        return ctx.Error is null;
    }

    internal static void ExecuteTypeCheck(EvalStack evalStack, FastList<SpkTypeInfo> types,
        out SpkObject first, out SpkObject second)
    {
        first = evalStack.Pop();
        second = evalStack.Pop();
        evalStack.Push(types[second.TypeId].CheckType((SpkTypeInfo)first));
    }

    internal static void ExecuteConstructorCheck(EvalStack evalStack, HashString constructorName, out SpkObject target)
    {
        target = evalStack.Peek();
        evalStack.Replace(target is IProduction production && production.Constructor == constructorName);
    }

    internal static SpkIterator ExecuteIteratorCreation(SpkNativeFunction function, int functionId, SpkObject[] locals) =>
        SpkIterator.Create(function.UnitId, functionId, function.Captures, locals);

    internal static SpkNativeFunction ExecuteFunctionCreation(Unit unit, SpkNativeFunction function, SpkObject[] locals,
        int functionId, int? defaultArgIndex = null) =>
        defaultArgIndex is int variadicIndex
            ? SpkNativeFunction.Create(unit.Symbols.Functions[functionId], unit.Id, functionId, function.Captures, locals, variadicIndex)
            : SpkNativeFunction.Create(unit.Symbols.Functions[functionId], unit.Id, functionId, function.Captures, locals);

    internal static void ExecuteObjectCreation(EvalStack evalStack, Unit unit, string constructorName,
        out SpkObject first, out SpkObject second, out SpkObject third)
    {
        second = evalStack.Pop();
        first = evalStack.Pop();
        third = evalStack.Pop();
        evalStack.Push(new SpkClass((SpkClassInfo)second, constructorName, (SpkTuple)first, (SpkTuple)third, unit));
    }

    internal static SpkClassInfo ExecuteTypeCreation(FastList<SpkTypeInfo> types, string typeName)
    {
        var clsInfo = new SpkClassInfo(typeName, types.Count);
        types.Add(clsInfo);
        return clsInfo;
    }

    internal static void ExecuteCastRegistration(EvalStack evalStack, out SpkObject sourceType, out SpkObject targetType)
    {
        sourceType = evalStack.Pop();
        targetType = evalStack.Pop();
        ((SpkTypeInfo)sourceType).SetCastFunction((SpkTypeInfo)targetType, (SpkFunction)evalStack.Pop());
    }

    internal static bool ExecuteCast(EvalStack evalStack, FastList<SpkTypeInfo> types,
        ExecutionContext ctx, out SpkObject sourceType, out SpkObject target)
    {
        sourceType = evalStack.Pop();
        target = evalStack.Peek();
        evalStack.Replace(types[sourceType.TypeId].Cast(ctx, sourceType, target));
        return ctx.Error is null;
    }

    internal static SpkObject MakeTuple(EvalStack stack, int size, bool vararg)
    {
        var arr = new SpkObject[size];
        var mutable = false;

        for (var i = 0; i < size; i++)
        {
            var e = stack.Pop();
            arr[arr.Length - i - 1] = e;

            if (!mutable && e is SpkLabel la && la.Mutable)
            {
                mutable = true;
            }
        }

        return new SpkTuple(arr, mutable, vararg);
    }

    internal static SpkObject MakeDictionary(EvalStack stack, int size)
    {
        var dict = new SpkDictionary();

        for (var i = 0; i < size; i++)
        {
            if (stack.Pop() is SpkLabel lab)
            {
                dict[new SpkString(lab.Label)] = lab.Value;
            }
        }

        return dict;
    }

}
