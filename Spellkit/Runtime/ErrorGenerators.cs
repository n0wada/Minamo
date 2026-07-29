using Spellkit.Debug;
using Spellkit.Runtime.Types;
using Spellkit.Compiler;
using Spellkit.Diagnostics;
using System.Linq;
using System.Text;

namespace Spellkit.Runtime;
public static class ErrorGenerators
{
    internal static SpellkitExceptionObject RuntimeException(SpellkitError code, params object[] args) =>
        RuntimeException(code.ToString(), args);

    internal static SpellkitExceptionObject RuntimeException(string constructor, params object[] args)
    {
        var arr = args.Length == 0
            ? SpellkitTuple.Empty
            : new SpellkitTuple(args.Select(TypeConverter.ConvertFrom).ToArray());

        return RuntimeException(constructor, arr);
    }

    internal static SpellkitExceptionObject RuntimeException(string constructor, SpellkitTuple data) =>
        new(constructor, GetErrorDescription(constructor, data), data);

    public static SpellkitObject CustomError(this ExecutionContext ctx, string constructor)
    {
        ctx.Error = RuntimeException(constructor);
        return Nil;
    }

    public static SpellkitObject Failure(this ExecutionContext ctx, string detail)
    {
        ctx.Error = RuntimeException(SpellkitError.Failure, detail);
        return Nil;
    }

    public static SpellkitObject OverloadProhibited(this ExecutionContext ctx, SpellkitTypeInfo typeInfo, string name)
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

        ctx.Error = RuntimeException(SpellkitError.OverloadProhibited, name);
        return Nil;
    }

    public static SpellkitObject IOFailed(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.IOFailed);
        return Nil;
    }

    public static SpellkitObject IOFailed(this ExecutionContext ctx, string detail)
    {
        ctx.Error = RuntimeException(SpellkitError.IOFailed, detail);
        return Nil;
    }

    public static SpellkitObject TypeClosed(this ExecutionContext ctx, SpellkitTypeInfo typeInfo)
    {
        ctx.Error = RuntimeException(SpellkitError.TypeClosed, typeInfo.ReflectedTypeName);
        return Nil;
    }

    public static SpellkitObject Overflow(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.Overflow);
        return Nil;
    }

    public static SpellkitObject InvalidOperation(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidOperation);
        return Nil;
    }

    public static SpellkitObject NotImplemented(this ExecutionContext ctx, string op)
    {
        ctx.Error = RuntimeException(SpellkitError.NotImplemented, Builtins.NameToOperator(op));
        return Nil;
    }

    public static SpellkitObject ParsingFailed(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.ParsingFailed);
        return Nil;
    }

    public static SpellkitObject ParsingFailed(this ExecutionContext ctx, string detail)
    {
        ctx.Error = RuntimeException(SpellkitError.ParsingFailed, detail);
        return Nil;
    }

    public static SpellkitObject Timeout(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.Timeout);
        return Nil;
    }

    public static SpellkitObject ValueMissing(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.ValueMissing);
        return Nil;
    }

    public static SpellkitObject InvalidOverload(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidOverload);
        return Nil;
    }

    public static SpellkitObject InvalidOverload(this ExecutionContext ctx, object func)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidOverload, func);
        return Nil;
    }

    public static SpellkitObject ConstructorFailed(this ExecutionContext ctx, object[]? args, Type type, Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append("new(");
        ProcessArguments(sb, args);
        sb.Append(')');
        ctx.Error = RuntimeException(SpellkitError.ConstructorFailed, sb.ToString(), type.FullName ?? type.Name, ex.Message);
        return Nil;
    }

    public static SpellkitObject InvalidValue(this ExecutionContext ctx, object val1)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidValue, val1);
        return Nil;
    }

    public static SpellkitObject InvalidValue(this ExecutionContext ctx, object val1, object val2)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidValue, val1, val2);
        return Nil;
    }

    public static SpellkitObject InvalidValue(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidValue);
        return Nil;
    }

    public static SpellkitObject PrivateAccess(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.PrivateAccess);
        return Nil;
    }

    public static SpellkitObject IndexReadOnly(this ExecutionContext ctx, object obj)
    {
        ctx.Error = RuntimeException(SpellkitError.IndexReadOnly, obj);
        return Nil;
    }

    public static SpellkitObject IndexReadOnly(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.IndexReadOnly);
        return Nil;
    }

    public static SpellkitObject MultipleValuesForArgument(this ExecutionContext ctx, string funName, string argName)
    {
        ctx.Error = RuntimeException(SpellkitError.MultipleValuesForArgument, funName, argName);
        return Nil;
    }

    public static SpellkitObject CollectionModified(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.CollectionModified);
        return Nil;
    }

    public static SpellkitObject AssertionFailed(this ExecutionContext ctx, string reason)
    {
        ctx.Error = RuntimeException(SpellkitError.AssertionFailed, reason);
        return Nil;
    }

    public static SpellkitObject PrivateNameAccess(this ExecutionContext ctx, string name)
    {
        ctx.Error = RuntimeException(SpellkitError.PrivateNameAccess, name);
        return Nil;
    }

    public static SpellkitObject OperationNotSupported(this ExecutionContext ctx, string op, SpellkitObject obj)
    {
        ctx.Error = RuntimeException(SpellkitError.OperationNotSupported, Builtins.NameToOperator(op), obj.TypeName);
        return Nil;
    }

    public static SpellkitObject OperationNotSupported(this ExecutionContext ctx, string op, int typeId)
    {
        var typeName = ctx.RuntimeContext.Types[typeId].ReflectedTypeName;
        ctx.Error = RuntimeException(SpellkitError.OperationNotSupported, Builtins.NameToOperator(op), typeName, 0, 0);
        return Nil;
    }

    public static SpellkitObject StaticOperationNotSupported(this ExecutionContext ctx, string op, int typeId)
    {
        var typeName = ctx.RuntimeContext.Types[typeId].ReflectedTypeName;
        //Small hack to get OperationNotSupported.4. It allows to use the same general code of "OperationNotSupported",
        //but a different text for a case of a static operation
        ctx.Error = RuntimeException(SpellkitError.OperationNotSupported, Builtins.NameToOperator(op), typeName, 0, 0);
        return Nil;
    }

    public static SpellkitObject OperationNotSupported(this ExecutionContext ctx, string op, SpellkitObject obj1, SpellkitObject obj2)
    {
        ctx.Error = RuntimeException(SpellkitError.OperationNotSupported, Builtins.NameToOperator(op), obj1.TypeName, obj2.TypeName);
        return Nil;
    }

    public static SpellkitObject InvalidCast(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidCast);
        return Nil;
    }

    public static SpellkitObject InvalidCast(this ExecutionContext ctx, string type1, string type2)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidCast, type1, type2);
        return Nil;
    }

    public static SpellkitObject IndexOutOfRange(this ExecutionContext ctx, object obj)
    {
        ctx.Error = RuntimeException(SpellkitError.IndexOutOfRange, obj);
        return Nil;
    }

    public static SpellkitObject IndexOutOfRange(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.IndexOutOfRange);
        return Nil;
    }

    public static SpellkitObject KeyNotFound(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.KeyNotFound);
        return Nil;
    }

    public static SpellkitObject KeyNotFound(this ExecutionContext ctx, object key)
    {
        ctx.Error = RuntimeException(SpellkitError.KeyNotFound, key);
        return Nil;
    }

    public static SpellkitObject KeyAlreadyPresent(this ExecutionContext ctx, object key)
    {
        ctx.Error = RuntimeException(SpellkitError.KeyAlreadyPresent, key);
        return Nil;
    }

    public static SpellkitObject KeyAlreadyPresent(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.KeyAlreadyPresent);
        return Nil;
    }

    public static SpellkitObject InvalidType(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidType);
        return Nil;
    }

    public static SpellkitObject InvalidType(this ExecutionContext ctx, SpellkitObject value)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidType, value.TypeName);
        return Nil;
    }

    public static SpellkitObject InvalidType(this ExecutionContext ctx, int expected, SpellkitObject got)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidType, ctx.RuntimeContext.Types[expected].ReflectedTypeName, got.TypeName);
        return Nil;
    }

    public static SpellkitObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, SpellkitObject got)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName, ctx.RuntimeContext.Types[got.TypeId].ReflectedTypeName);
        return Nil;
    }

    public static SpellkitObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, int expected3, SpellkitObject got)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName,
            ctx.RuntimeContext.Types[expected3].ReflectedTypeName, got.TypeName);
        return Nil;
    }

    public static SpellkitObject InvalidType(this ExecutionContext ctx, int expected1, int exptected2, int expected3, int expected4, SpellkitObject got)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidType, ctx.RuntimeContext.Types[expected1].ReflectedTypeName, ctx.RuntimeContext.Types[exptected2].ReflectedTypeName,
            ctx.RuntimeContext.Types[expected3].ReflectedTypeName, ctx.RuntimeContext.Types[expected4].ReflectedTypeName, got.TypeName);
        return Nil;
    }

    public static SpellkitObject InvalidType(this ExecutionContext ctx, string typeName)
    {
        ctx.Error = RuntimeException(SpellkitError.InvalidType, typeName);
        return Nil;
    }

    public static SpellkitObject ExternalFunctionFailure(this ExecutionContext ctx, SpellkitFunction func, string error)
    {
        var functionName = func.Self is null ? func.FunctionName
            : $"{func.Self.TypeName}.{func.FunctionName}";
        ctx.Error = RuntimeException(SpellkitError.ExternalFunctionFailure, functionName, error);
        return Nil;
    }

    public static SpellkitObject DivideByZero(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.DivideByZero);
        return Nil;
    }

    public static SpellkitObject TooManyArguments(this ExecutionContext ctx)
    {
        ctx.Error = RuntimeException(SpellkitError.TooManyArguments);
        return Nil;
    }

    public static SpellkitObject TooManyArguments(this ExecutionContext ctx, string functionName, int functionArguments, int passedArguments)
    {
        ctx.Error = RuntimeException(SpellkitError.TooManyArguments, functionName, functionArguments, passedArguments);
        return Nil;
    }

    public static SpellkitObject RequiredArgumentMissing(this ExecutionContext ctx, string functionName, string argumentName)
    {
        ctx.Error = RuntimeException(SpellkitError.RequiredArgumentMissing, functionName, argumentName);
        return Nil;
    }

    public static SpellkitObject ArgumentNotFound(this ExecutionContext ctx, string functionName, string argumentName)
    {
        ctx.Error = RuntimeException(SpellkitError.ArgumentNotFound, functionName, argumentName);
        return Nil;
    }

    public static SpellkitObject MethodNotFound(this ExecutionContext ctx, string name, Type type, SpellkitObject[]? args)
    {
        var sb = new StringBuilder();
        sb.Append(type.FullName ?? type.Name);
        sb.Append('.');
        sb.Append(name);
        sb.Append('(');
        ProcessArguments(sb, args);
        sb.Append(')');
        ctx.Error = RuntimeException(SpellkitError.MethodNotFound, sb.ToString());
        return Nil;
    }

    public static SpellkitError GetErrorCode(SpellkitObject err)
    {
        if (err is SpellkitExceptionObject ex2 && Enum.TryParse<SpellkitError>(ex2.Name, true, out var exCode))
        {
            return exCode;
        }

        return SpellkitError.UnexpectedError;
    }

    public static string GetErrorDescription(SpellkitObject err)
    {
        if (err is SpellkitExceptionObject ex)
        {
            return ex.Message;
        }

        return err.ToString() ?? string.Empty;
    }

    private static string GetErrorDescription(string constructor, SpellkitTuple data)
    {
        if (!Enum.TryParse<SpellkitError>(constructor, true, out _))
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
                .Select(v => v is SpellkitTypeInfo t ? t.ReflectedTypeName : (v.ToString() ?? ""))
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

                var tt = (args[i] is SpellkitObject obj ? obj.ToObject() : args[i])?.GetType();

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
