using Minamo.Debug;
using Minamo.Runtime.Types;
using Minamo.Compiler;
using Minamo.Diagnostics;
using System.Linq;
using System.Text;

namespace Minamo.Runtime;
public static class ErrorGenerators
{
    internal static MinamoExceptionObject RuntimeException(MinamoError code, params object[] args) =>
        RuntimeException(code.ToString(), args);

    internal static MinamoExceptionObject RuntimeException(string constructor, params object[] args)
    {
        var arr = args.Length == 0
            ? MinamoTuple.Empty
            : new MinamoTuple(args.Select(TypeConverter.ConvertFrom).ToArray());

        return RuntimeException(constructor, arr);
    }

    internal static MinamoExceptionObject RuntimeException(string constructor, MinamoTuple data) =>
        new(constructor, GetErrorDescription(constructor, data), data);

    public static MinamoObject CustomError(this ExecutionContext ctx, string constructor)
    {
        ctx.Error = RuntimeException(constructor);
        return Nil;
    }

    public static MinamoObject Failure(this ExecutionContext ctx, string detail)
    {
        ctx.Error = RuntimeException(MinamoError.Failure, detail);
        return Nil;
    }

    public static MinamoObject OverloadProhibited(this ExecutionContext ctx, MinamoTypeInfo typeInfo, string name)
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

        ctx.Error = RuntimeException(MinamoError.OverloadProhibited, name);
        return Nil;
    }

    public static MinamoObject IOFailed(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.IOFailed);
        return Nil;
    }

    public static MinamoObject IOFailed(this ExecutionContext ctx, string detail)
    {
        ctx.Error = RuntimeException(MinamoError.IOFailed, detail);
        return Nil;
    }

    public static MinamoObject TypeClosed(this ExecutionContext ctx, MinamoTypeInfo typeInfo)
    {
        ctx.Error = RuntimeException(MinamoError.TypeClosed, typeInfo.ReflectedTypeName);
        return Nil;
    }

    public static MinamoObject Overflow(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.Overflow);
        return Nil;
    }

    public static MinamoObject InvalidOperation(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidOperation);
        return Nil;
    }

    public static MinamoObject NotImplemented(this ExecutionContext ctx, string op)
    {
        ctx.Error = RuntimeException(MinamoError.NotImplemented, Builtins.NameToOperator(op));
        return Nil;
    }

    public static MinamoObject ParsingFailed(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.ParsingFailed);
        return Nil;
    }

    public static MinamoObject ParsingFailed(this ExecutionContext ctx, string detail)
    {
        ctx.Error = RuntimeException(MinamoError.ParsingFailed, detail);
        return Nil;
    }

    public static MinamoObject Timeout(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.Timeout);
        return Nil;
    }

    public static MinamoObject ValueMissing(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.ValueMissing);
        return Nil;
    }

    public static MinamoObject InvalidOverload(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidOverload);
        return Nil;
    }

    public static MinamoObject InvalidOverload(this ExecutionContext ctx, object func)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidOverload, func);
        return Nil;
    }

    public static MinamoObject ConstructorFailed(this ExecutionContext ctx, object[]? args, Type type, Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append("new(");
        ProcessArguments(sb, args);
        sb.Append(')');
        ctx.Error = RuntimeException(MinamoError.ConstructorFailed, sb.ToString(), type.FullName ?? type.Name, ex.Message);
        return Nil;
    }

    public static MinamoObject InvalidValue(this ExecutionContext ctx, object val1)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidValue, val1);
        return Nil;
    }

    public static MinamoObject InvalidValue(this ExecutionContext ctx, object val1, object val2)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidValue, val1, val2);
        return Nil;
    }

    public static MinamoObject InvalidValue(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidValue);
        return Nil;
    }

    public static MinamoObject PrivateAccess(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.PrivateAccess);
        return Nil;
    }

    public static MinamoObject IndexReadOnly(this ExecutionContext ctx, object obj)
    {
        ctx.Error = RuntimeException(MinamoError.IndexReadOnly, obj);
        return Nil;
    }

    public static MinamoObject IndexReadOnly(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.IndexReadOnly);
        return Nil;
    }

    public static MinamoObject MultipleValuesForArgument(this ExecutionContext ctx, string funName, string argName)
    {
        ctx.Error = RuntimeException(MinamoError.MultipleValuesForArgument, funName, argName);
        return Nil;
    }

    public static MinamoObject CollectionModified(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.CollectionModified);
        return Nil;
    }

    public static MinamoObject AssertionFailed(this ExecutionContext ctx, string reason)
    {
        ctx.Error = RuntimeException(MinamoError.AssertionFailed, reason);
        return Nil;
    }

    public static MinamoObject PrivateNameAccess(this ExecutionContext ctx, string name)
    {
        ctx.Error = RuntimeException(MinamoError.PrivateNameAccess, name);
        return Nil;
    }

    public static MinamoObject OperationNotSupported(this ExecutionContext ctx, string op, MinamoObject obj)
    {
        ctx.Error = RuntimeException(MinamoError.OperationNotSupported, Builtins.NameToOperator(op), obj.TypeName);
        return Nil;
    }

    public static MinamoObject OperationNotSupported(this ExecutionContext ctx, string op, int typeId)
    {
        var typeName = ctx.RuntimeContext.Types[typeId].ReflectedTypeName;
        ctx.Error = RuntimeException(MinamoError.OperationNotSupported, Builtins.NameToOperator(op), typeName, 0, 0);
        return Nil;
    }

    public static MinamoObject StaticOperationNotSupported(this ExecutionContext ctx, string op, int typeId)
    {
        var typeName = ctx.RuntimeContext.Types[typeId].ReflectedTypeName;
        //Small hack to get OperationNotSupported.4. It allows to use the same general code of "OperationNotSupported",
        //but a different text for a case of a static operation
        ctx.Error = RuntimeException(MinamoError.OperationNotSupported, Builtins.NameToOperator(op), typeName, 0, 0);
        return Nil;
    }

    public static MinamoObject OperationNotSupported(this ExecutionContext ctx, string op, MinamoObject obj1, MinamoObject obj2)
    {
        ctx.Error = RuntimeException(MinamoError.OperationNotSupported, Builtins.NameToOperator(op), obj1.TypeName, obj2.TypeName);
        return Nil;
    }

    public static MinamoObject InvalidCast(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidCast);
        return Nil;
    }

    public static MinamoObject InvalidCast(this ExecutionContext ctx, string type1, string type2)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidCast, type1, type2);
        return Nil;
    }

    public static MinamoObject IndexOutOfRange(this ExecutionContext ctx, object obj)
    {
        ctx.Error = RuntimeException(MinamoError.IndexOutOfRange, obj);
        return Nil;
    }

    public static MinamoObject IndexOutOfRange(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.IndexOutOfRange);
        return Nil;
    }

    public static MinamoObject KeyNotFound(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.KeyNotFound);
        return Nil;
    }

    public static MinamoObject KeyNotFound(this ExecutionContext ctx, object key)
    {
        ctx.Error = RuntimeException(MinamoError.KeyNotFound, key);
        return Nil;
    }

    public static MinamoObject KeyAlreadyPresent(this ExecutionContext ctx, object key)
    {
        ctx.Error = RuntimeException(MinamoError.KeyAlreadyPresent, key);
        return Nil;
    }

    public static MinamoObject KeyAlreadyPresent(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.KeyAlreadyPresent);
        return Nil;
    }

    public static MinamoObject InvalidType(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidType);
        return Nil;
    }

    public static MinamoObject InvalidType(this ExecutionContext ctx, MinamoObject value)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidType, value.TypeName);
        return Nil;
    }

    public static MinamoObject InvalidType(this ExecutionContext ctx, int expected, MinamoObject got)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidType, ctx.RuntimeContext.Types[expected].ReflectedTypeName, got.TypeName);
        return Nil;
    }

    public static MinamoObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, MinamoObject got)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName, ctx.RuntimeContext.Types[got.TypeId].ReflectedTypeName);
        return Nil;
    }

    public static MinamoObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, int expected3, MinamoObject got)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName,
            ctx.RuntimeContext.Types[expected3].ReflectedTypeName, got.TypeName);
        return Nil;
    }

    public static MinamoObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, int expected3, int expected4, MinamoObject got)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName,
            ctx.RuntimeContext.Types[expected3].ReflectedTypeName, ctx.RuntimeContext.Types[expected4].ReflectedTypeName, got.TypeName);
        return Nil;
    }

    public static MinamoObject InvalidType(this ExecutionContext ctx, string typeName)
    {
        ctx.Error = RuntimeException(MinamoError.InvalidType, typeName);
        return Nil;
    }

    public static MinamoObject ExternalFunctionFailure(this ExecutionContext ctx, MinamoFunction func, string error)
    {
        var functionName = func.Self is null ? func.FunctionName
            : $"{func.Self.TypeName}.{func.FunctionName}";
        ctx.Error = RuntimeException(MinamoError.ExternalFunctionFailure, functionName, error);
        return Nil;
    }

    public static MinamoObject DivideByZero(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.DivideByZero);
        return Nil;
    }

    public static MinamoObject TooManyArguments(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(MinamoError.TooManyArguments);
        return Nil;
    }

    public static MinamoObject TooManyArguments(this ExecutionContext ctx, string functionName, int functionArguments, int passedArguments)
    {
        ctx.Error = RuntimeException(MinamoError.TooManyArguments, functionName, functionArguments, passedArguments);
        return Nil;
    }

    public static MinamoObject RequiredArgumentMissing(this ExecutionContext ctx, string functionName, string argumentName)
    {
        ctx.Error = RuntimeException(MinamoError.RequiredArgumentMissing, functionName, argumentName);
        return Nil;
    }

    public static MinamoObject ArgumentNotFound(this ExecutionContext ctx, string functionName, string argumentName)
    {
        ctx.Error = RuntimeException(MinamoError.ArgumentNotFound, functionName, argumentName);
        return Nil;
    }

    public static MinamoObject MethodNotFound(this ExecutionContext ctx, string name, Type type, MinamoObject[]? args)
    {
        var sb = new StringBuilder();
        sb.Append(type.FullName ?? type.Name);
        sb.Append('.');
        sb.Append(name);
        sb.Append('(');
        ProcessArguments(sb, args);
        sb.Append(')');
        ctx.Error = RuntimeException(MinamoError.MethodNotFound, sb.ToString());
        return Nil;
    }

    public static MinamoError GetErrorCode(MinamoObject err)
    {
        if (err is MinamoExceptionObject ex2 && Enum.TryParse<MinamoError>(ex2.Name, true, out var exCode))
        {
            return exCode;
        }

        return MinamoError.UnexpectedError;
    }

    public static string GetErrorDescription(MinamoObject err)
    {
        if (err is MinamoExceptionObject ex)
        {
            return ex.Message;
        }

        return err.ToString() ?? string.Empty;
    }

    private static string GetErrorDescription(string constructor, MinamoTuple data)
    {
        if (!Enum.TryParse<MinamoError>(constructor, true, out _))
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
                .Select(v => v is MinamoTypeInfo t ? t.ReflectedTypeName : (v.ToString() ?? ""))
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

                var tt = (args[i] is MinamoObject obj ? obj.ToObject() : args[i])?.GetType();

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
