using Spellkit.Debug;
using Spellkit.Runtime.Types;
using Spellkit.Compiler;
using Spellkit.Diagnostics;
using System.Linq;
using System.Text;

namespace Spellkit.Runtime;
public static class ErrorGenerators
{
    internal static SpkExceptionObject RuntimeException(SpkError code, params object[] args) =>
        RuntimeException(code.ToString(), args);

    internal static SpkExceptionObject RuntimeException(string constructor, params object[] args)
    {
        var arr = args.Length == 0
            ? SpkTuple.Empty
            : new SpkTuple(args.Select(TypeConverter.ConvertFrom).ToArray());

        return RuntimeException(constructor, arr);
    }

    internal static SpkExceptionObject RuntimeException(string constructor, SpkTuple data) =>
        new(constructor, GetErrorDescription(constructor, data), data);

    public static SpkObject CustomError(this ExecutionContext ctx, string constructor)
    {
        ctx.Error = RuntimeException(constructor);
        return Nil;
    }

    public static SpkObject Failure(this ExecutionContext ctx, string detail)
    {
        ctx.Error = RuntimeException(SpkError.Failure, detail);
        return Nil;
    }

    public static SpkObject OverloadProhibited(this ExecutionContext ctx, SpkTypeInfo typeInfo, string name)
    {
        name = Builtins.NameToOperator(name);

        if (Builtins.IsSetter(name))
        {
            name = $"set {typeInfo.ReflectedTypeName}.{name}";
        }
        else if (name == Builtins.Get)
        {
            name = $"{typeInfo.ReflectedTypeName}[]";
        }
        else if (name == Builtins.Set)
        {
            name = $"set {typeInfo.ReflectedTypeName}[]";
        }
        else if (name.IndexOfAny(Builtins.OperatorSymbols.ToCharArray()) != -1)
        {
            name = $"{typeInfo.ReflectedTypeName} {name}";
        }
        else
        {
            name = $"{typeInfo.ReflectedTypeName}.{name}";
        }

        ctx.Error = RuntimeException(SpkError.OverloadProhibited, name);
        return Nil;
    }

    public static SpkObject IOFailed(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.IOFailed);
        return Nil;
    }

    public static SpkObject IOFailed(this ExecutionContext ctx, string detail)
    {
        ctx.Error = RuntimeException(SpkError.IOFailed, detail);
        return Nil;
    }

    public static SpkObject TypeClosed(this ExecutionContext ctx, SpkTypeInfo typeInfo)
    {
        ctx.Error = RuntimeException(SpkError.TypeClosed, typeInfo.ReflectedTypeName);
        return Nil;
    }

    public static SpkObject Overflow(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.Overflow);
        return Nil;
    }

    public static SpkObject InvalidOperation(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.InvalidOperation);
        return Nil;
    }

    public static SpkObject NotImplemented(this ExecutionContext ctx, string op)
    {
        ctx.Error = RuntimeException(SpkError.NotImplemented, Builtins.NameToOperator(op));
        return Nil;
    }

    public static SpkObject ParsingFailed(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.ParsingFailed);
        return Nil;
    }

    public static SpkObject ParsingFailed(this ExecutionContext ctx, string detail)
    {
        ctx.Error = RuntimeException(SpkError.ParsingFailed, detail);
        return Nil;
    }

    public static SpkObject Timeout(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.Timeout);
        return Nil;
    }

    public static SpkObject ValueMissing(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.ValueMissing);
        return Nil;
    }

    public static SpkObject InvalidOverload(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.InvalidOverload);
        return Nil;
    }

    public static SpkObject InvalidOverload(this ExecutionContext ctx, object func)
    {
        ctx.Error = RuntimeException(SpkError.InvalidOverload, func);
        return Nil;
    }

    public static SpkObject ConstructorFailed(this ExecutionContext ctx, object[]? args, Type type, Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append("new(");
        ProcessArguments(sb, args);
        sb.Append(')');
        ctx.Error = RuntimeException(SpkError.ConstructorFailed, sb.ToString(), type.FullName ?? type.Name, ex.Message);
        return Nil;
    }

    public static SpkObject InvalidValue(this ExecutionContext ctx, object val1)
    {
        ctx.Error = RuntimeException(SpkError.InvalidValue, val1);
        return Nil;
    }

    public static SpkObject InvalidValue(this ExecutionContext ctx, object val1, object val2)
    {
        ctx.Error = RuntimeException(SpkError.InvalidValue, val1, val2);
        return Nil;
    }

    public static SpkObject InvalidValue(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.InvalidValue);
        return Nil;
    }

    public static SpkObject PrivateAccess(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.PrivateAccess);
        return Nil;
    }

    public static SpkObject IndexReadOnly(this ExecutionContext ctx, object obj)
    {
        ctx.Error = RuntimeException(SpkError.IndexReadOnly, obj);
        return Nil;
    }

    public static SpkObject IndexReadOnly(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.IndexReadOnly);
        return Nil;
    }

    public static SpkObject MultipleValuesForArgument(this ExecutionContext ctx, string funName, string argName)
    {
        ctx.Error = RuntimeException(SpkError.MultipleValuesForArgument, funName, argName);
        return Nil;
    }

    public static SpkObject CollectionModified(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.CollectionModified);
        return Nil;
    }

    public static SpkObject AssertionFailed(this ExecutionContext ctx, string reason)
    {
        ctx.Error = RuntimeException(SpkError.AssertionFailed, reason);
        return Nil;
    }

    public static SpkObject PrivateNameAccess(this ExecutionContext ctx, string name)
    {
        ctx.Error = RuntimeException(SpkError.PrivateNameAccess, name);
        return Nil;
    }

    public static SpkObject OperationNotSupported(this ExecutionContext ctx, string op, SpkObject obj)
    {
        ctx.Error = RuntimeException(SpkError.OperationNotSupported, Builtins.NameToOperator(op), obj.TypeName);
        return Nil;
    }

    public static SpkObject OperationNotSupported(this ExecutionContext ctx, string op, int typeId)
    {
        var typeName = ctx.RuntimeContext.Types[typeId].ReflectedTypeName;
        ctx.Error = RuntimeException(SpkError.OperationNotSupported, Builtins.NameToOperator(op), typeName, 0, 0);
        return Nil;
    }

    public static SpkObject StaticOperationNotSupported(this ExecutionContext ctx, string op, int typeId)
    {
        var typeName = ctx.RuntimeContext.Types[typeId].ReflectedTypeName;
        //Small hack to get OperationNotSupported.4. It allows to use the same general code of "OperationNotSupported",
        //but a different text for a case of a static operation
        ctx.Error = RuntimeException(SpkError.OperationNotSupported, Builtins.NameToOperator(op), typeName, 0, 0);
        return Nil;
    }

    public static SpkObject OperationNotSupported(this ExecutionContext ctx, string op, SpkObject obj1, SpkObject obj2)
    {
        ctx.Error = RuntimeException(SpkError.OperationNotSupported, Builtins.NameToOperator(op), obj1.TypeName, obj2.TypeName);
        return Nil;
    }

    public static SpkObject InvalidCast(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.InvalidCast);
        return Nil;
    }

    public static SpkObject InvalidCast(this ExecutionContext ctx, string type1, string type2)
    {
        ctx.Error = RuntimeException(SpkError.InvalidCast, type1, type2);
        return Nil;
    }

    public static SpkObject IndexOutOfRange(this ExecutionContext ctx, object obj)
    {
        ctx.Error = RuntimeException(SpkError.IndexOutOfRange, obj);
        return Nil;
    }

    public static SpkObject IndexOutOfRange(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.IndexOutOfRange);
        return Nil;
    }

    public static SpkObject KeyNotFound(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.KeyNotFound);
        return Nil;
    }

    public static SpkObject KeyNotFound(this ExecutionContext ctx, object key)
    {
        ctx.Error = RuntimeException(SpkError.KeyNotFound, key);
        return Nil;
    }

    public static SpkObject KeyAlreadyPresent(this ExecutionContext ctx, object key)
    {
        ctx.Error = RuntimeException(SpkError.KeyAlreadyPresent, key);
        return Nil;
    }

    public static SpkObject KeyAlreadyPresent(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.KeyAlreadyPresent);
        return Nil;
    }

    public static SpkObject InvalidType(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.InvalidType);
        return Nil;
    }

    public static SpkObject InvalidType(this ExecutionContext ctx, SpkObject value)
    {
        ctx.Error = RuntimeException(SpkError.InvalidType, value.TypeName);
        return Nil;
    }

    public static SpkObject InvalidType(this ExecutionContext ctx, int expected, SpkObject got)
    {
        ctx.Error = RuntimeException(SpkError.InvalidType, ctx.RuntimeContext.Types[expected].ReflectedTypeName, got.TypeName);
        return Nil;
    }

    public static SpkObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, SpkObject got)
    {
        ctx.Error = RuntimeException(SpkError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName, ctx.RuntimeContext.Types[got.TypeId].ReflectedTypeName);
        return Nil;
    }

    public static SpkObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, int expected3, SpkObject got)
    {
        ctx.Error = RuntimeException(SpkError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName,
            ctx.RuntimeContext.Types[expected3].ReflectedTypeName, got.TypeName);
        return Nil;
    }

    public static SpkObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, int expected3, int expected4, SpkObject got)
    {
        ctx.Error = RuntimeException(SpkError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName,
            ctx.RuntimeContext.Types[expected3].ReflectedTypeName, ctx.RuntimeContext.Types[expected4].ReflectedTypeName, got.TypeName);
        return Nil;
    }

    public static SpkObject InvalidType(this ExecutionContext ctx, string typeName)
    {
        ctx.Error = RuntimeException(SpkError.InvalidType, typeName);
        return Nil;
    }

    public static SpkObject ExternalFunctionFailure(this ExecutionContext ctx, SpkFunction func, string error)
    {
        var functionName = func.Self is null ? func.FunctionName
            : $"{func.Self.TypeName}.{func.FunctionName}";
        ctx.Error = RuntimeException(SpkError.ExternalFunctionFailure, functionName, error);
        return Nil;
    }

    public static SpkObject DivideByZero(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.DivideByZero);
        return Nil;
    }

    public static SpkObject TooManyArguments(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpkError.TooManyArguments);
        return Nil;
    }

    public static SpkObject TooManyArguments(this ExecutionContext ctx, string functionName, int functionArguments, int passedArguments)
    {
        ctx.Error = RuntimeException(SpkError.TooManyArguments, functionName, functionArguments, passedArguments);
        return Nil;
    }

    public static SpkObject RequiredArgumentMissing(this ExecutionContext ctx, string functionName, string argumentName)
    {
        ctx.Error = RuntimeException(SpkError.RequiredArgumentMissing, functionName, argumentName);
        return Nil;
    }

    public static SpkObject ArgumentNotFound(this ExecutionContext ctx, string functionName, string argumentName)
    {
        ctx.Error = RuntimeException(SpkError.ArgumentNotFound, functionName, argumentName);
        return Nil;
    }

    public static SpkObject MethodNotFound(this ExecutionContext ctx, string name, Type type, SpkObject[]? args)
    {
        var sb = new StringBuilder();
        sb.Append(type.FullName ?? type.Name);
        sb.Append('.');
        sb.Append(name);
        sb.Append('(');
        ProcessArguments(sb, args);
        sb.Append(')');
        ctx.Error = RuntimeException(SpkError.MethodNotFound, sb.ToString());
        return Nil;
    }

    public static SpkError GetErrorCode(SpkObject err)
    {
        if (err is SpkExceptionObject ex2 && Enum.TryParse<SpkError>(ex2.Name, true, out var exCode))
        {
            return exCode;
        }

        return SpkError.UnexpectedError;
    }

    public static string GetErrorDescription(SpkObject err)
    {
        if (err is SpkExceptionObject ex)
        {
            return ex.Message;
        }

        return err.ToString() ?? string.Empty;
    }

    private static string GetErrorDescription(string constructor, SpkTuple data)
    {
        if (!Enum.TryParse<SpkError>(constructor, true, out _))
        {
            if (data.Count > 0)
            {
                var dat = data[0].ToString();

                if (dat is not null)
                {
                    return constructor + $"({dat})";
                }
            }

            return constructor;
        }

        var idx = data.Count;
        var str = MessageCatalog.Find(MessageGroup.Runtime, constructor + "." + idx);

        if (str is not null && data.Count > 0)
        {
            var vals = data.ToArray()
                .Select(v => v is SpkTypeInfo t ? t.ReflectedTypeName : (v.ToString() ?? ""))
                .ToArray();
            str = string.Format(str, vals);
        }
        else
        {
            str ??= MessageCatalog.Find(MessageGroup.Runtime, constructor + ".0");
        }

        return str ?? constructor;
    }

    private static void ProcessArguments(StringBuilder sb, object[]? args)
    {
        if (args is not null)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                var tt = (args[i] is SpkObject obj ? obj.ToObject() : args[i])?.GetType();

                if (tt is null)
                {
                    sb.Append("<null>");
                }
                else
                {
                    sb.Append(tt.FullName ?? tt.Name);
                }
            }
        }
    }
}
