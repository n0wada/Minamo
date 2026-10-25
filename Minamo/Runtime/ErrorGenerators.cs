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

    private static MinamoObject SetError(ExecutionContext ctx, MinamoExceptionObject error)
    {
        ctx.Error = error;
        return Nil;
    }

    public static MinamoObject CustomError(this ExecutionContext ctx, string constructor) =>
        SetError(ctx, RuntimeException(constructor));

    public static MinamoObject Failure(this ExecutionContext ctx, string detail) =>
        SetError(ctx, RuntimeException(MinamoError.Failure, detail));

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

        return SetError(ctx, RuntimeException(MinamoError.OverloadProhibited, name));
    }

    public static MinamoObject IOFailed(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.IOFailed));

    public static MinamoObject IOFailed(this ExecutionContext ctx, string detail) =>
        SetError(ctx, RuntimeException(MinamoError.IOFailed, detail));

    public static MinamoObject TypeClosed(this ExecutionContext ctx, MinamoTypeInfo typeInfo) =>
        SetError(ctx, RuntimeException(MinamoError.TypeClosed, typeInfo.ReflectedTypeName));

    public static MinamoObject Overflow(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.Overflow));

    public static MinamoObject InvalidOperation(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidOperation));

    public static MinamoObject NotImplemented(this ExecutionContext ctx, string op) =>
        SetError(ctx, RuntimeException(MinamoError.NotImplemented, Builtins.NameToOperator(op)));

    public static MinamoObject ParsingFailed(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.ParsingFailed));

    public static MinamoObject ParsingFailed(this ExecutionContext ctx, string detail) =>
        SetError(ctx, RuntimeException(MinamoError.ParsingFailed, detail));

    public static MinamoObject Timeout(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.Timeout));

    public static MinamoObject ValueMissing(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.ValueMissing));

    public static MinamoObject InvalidOverload(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidOverload));

    public static MinamoObject InvalidOverload(this ExecutionContext ctx, object func) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidOverload, func));

    public static MinamoObject ConstructorFailed(this ExecutionContext ctx, object[]? args, Type type, Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append("new(");
        ProcessArguments(sb, args);
        sb.Append(')');
        return SetError(ctx, RuntimeException(MinamoError.ConstructorFailed, sb.ToString(), type.FullName ?? type.Name, ex.Message));
    }

    public static MinamoObject InvalidValue(this ExecutionContext ctx, object val1) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidValue, val1));

    public static MinamoObject InvalidValue(this ExecutionContext ctx, object val1, object val2) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidValue, val1, val2));

    public static MinamoObject InvalidValue(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidValue));

    public static MinamoObject PrivateAccess(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.PrivateAccess));

    public static MinamoObject IndexReadOnly(this ExecutionContext ctx, object obj) =>
        SetError(ctx, RuntimeException(MinamoError.IndexReadOnly, obj));

    public static MinamoObject IndexReadOnly(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.IndexReadOnly));

    public static MinamoObject MultipleValuesForArgument(this ExecutionContext ctx, string funName, string argName) =>
        SetError(ctx, RuntimeException(MinamoError.MultipleValuesForArgument, funName, argName));

    public static MinamoObject CollectionModified(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.CollectionModified));

    public static MinamoObject AssertionFailed(this ExecutionContext ctx, string reason) =>
        SetError(ctx, RuntimeException(MinamoError.AssertionFailed, reason));

    public static MinamoObject PrivateNameAccess(this ExecutionContext ctx, string name) =>
        SetError(ctx, RuntimeException(MinamoError.PrivateNameAccess, name));

    public static MinamoObject OperationNotSupported(this ExecutionContext ctx, string op, MinamoObject obj) =>
        SetError(ctx, RuntimeException(MinamoError.OperationNotSupported, Builtins.NameToOperator(op), obj.TypeName));

    public static MinamoObject OperationNotSupported(this ExecutionContext ctx, string op, int typeId)
    {
        var typeName = ctx.RuntimeContext.Types[typeId].ReflectedTypeName;
        return SetError(ctx, RuntimeException(MinamoError.OperationNotSupported, Builtins.NameToOperator(op), typeName, 0, 0));
    }

    public static MinamoObject StaticOperationNotSupported(this ExecutionContext ctx, string op, int typeId)
    {
        var typeName = ctx.RuntimeContext.Types[typeId].ReflectedTypeName;
        //Small hack to get OperationNotSupported.4. It allows to use the same general code of "OperationNotSupported",
        //but a different text for a case of a static operation
        return SetError(ctx, RuntimeException(MinamoError.OperationNotSupported, Builtins.NameToOperator(op), typeName, 0, 0));
    }

    public static MinamoObject OperationNotSupported(this ExecutionContext ctx, string op, MinamoObject obj1, MinamoObject obj2) =>
        SetError(ctx, RuntimeException(MinamoError.OperationNotSupported, Builtins.NameToOperator(op), obj1.TypeName, obj2.TypeName));

    public static MinamoObject InvalidCast(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidCast));

    public static MinamoObject InvalidCast(this ExecutionContext ctx, string type1, string type2) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidCast, type1, type2));

    public static MinamoObject IndexOutOfRange(this ExecutionContext ctx, object obj) =>
        SetError(ctx, RuntimeException(MinamoError.IndexOutOfRange, obj));

    public static MinamoObject IndexOutOfRange(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.IndexOutOfRange));

    public static MinamoObject KeyNotFound(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.KeyNotFound));

    public static MinamoObject KeyNotFound(this ExecutionContext ctx, object key) =>
        SetError(ctx, RuntimeException(MinamoError.KeyNotFound, key));

    public static MinamoObject KeyAlreadyPresent(this ExecutionContext ctx, object key) =>
        SetError(ctx, RuntimeException(MinamoError.KeyAlreadyPresent, key));

    public static MinamoObject KeyAlreadyPresent(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.KeyAlreadyPresent));

    public static MinamoObject InvalidType(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidType));

    public static MinamoObject InvalidType(this ExecutionContext ctx, MinamoObject value) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidType, value.TypeName));

    public static MinamoObject InvalidType(this ExecutionContext ctx, int expected, MinamoObject got) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidType, ctx.RuntimeContext.Types[expected].ReflectedTypeName, got.TypeName));

    public static MinamoObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, MinamoObject got) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName, ctx.RuntimeContext.Types[got.TypeId].ReflectedTypeName));

    public static MinamoObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, int expected3, MinamoObject got)
    {
        return SetError(ctx, RuntimeException(MinamoError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName,
            ctx.RuntimeContext.Types[expected3].ReflectedTypeName, got.TypeName));
    }

    public static MinamoObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, int expected3, int expected4, MinamoObject got)
    {
        return SetError(ctx, RuntimeException(MinamoError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName,
            ctx.RuntimeContext.Types[expected3].ReflectedTypeName, ctx.RuntimeContext.Types[expected4].ReflectedTypeName, got.TypeName));
    }

    public static MinamoObject InvalidType(this ExecutionContext ctx, string typeName) =>
        SetError(ctx, RuntimeException(MinamoError.InvalidType, typeName));

    public static MinamoObject ExternalFunctionFailure(this ExecutionContext ctx, MinamoFunction func, string error)
    {
        var functionName = func.Self is null ? func.FunctionName
            : $"{func.Self.TypeName}.{func.FunctionName}";
        return SetError(ctx, RuntimeException(MinamoError.ExternalFunctionFailure, functionName, error));
    }

    public static MinamoObject DivideByZero(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.DivideByZero));

    public static MinamoObject TooManyArguments(this ExecutionContext ctx) =>
        SetError(ctx, RuntimeException(MinamoError.TooManyArguments));

    public static MinamoObject TooManyArguments(this ExecutionContext ctx, string functionName, int functionArguments, int passedArguments) =>
        SetError(ctx, RuntimeException(MinamoError.TooManyArguments, functionName, functionArguments, passedArguments));

    public static MinamoObject RequiredArgumentMissing(this ExecutionContext ctx, string functionName, string argumentName) =>
        SetError(ctx, RuntimeException(MinamoError.RequiredArgumentMissing, functionName, argumentName));

    public static MinamoObject ArgumentNotFound(this ExecutionContext ctx, string functionName, string argumentName) =>
        SetError(ctx, RuntimeException(MinamoError.ArgumentNotFound, functionName, argumentName));

    public static MinamoObject MethodNotFound(this ExecutionContext ctx, string name, Type type, MinamoObject[]? args)
    {
        var sb = new StringBuilder();
        sb.Append(type.FullName ?? type.Name);
        sb.Append('.');
        sb.Append(name);
        sb.Append('(');
        ProcessArguments(sb, args);
        sb.Append(')');
        return SetError(ctx, RuntimeException(MinamoError.MethodNotFound, sb.ToString()));
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
