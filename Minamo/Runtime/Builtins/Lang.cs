using Minamo.Codegen;
using Minamo.Compiler;
using Minamo.Hosting;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections;

namespace Minamo.Linker;

[GeneratedModule]
internal sealed partial class Lang : ForeignUnit
{
    private readonly MinamoTuple? startupArguments;
    private MinamoHostRootTypeInfo hostRootType = null!;

    public Lang(bool exposeHostObject = true)
    {
        FileName = "lang";
        if (exposeHostObject)
        {
            Add("host", new MinamoHostRoot(hostRootType));
        }
    }

    public Lang(MinamoTuple? args, bool exposeHostObject = true) : this(exposeHostObject) =>
        startupArguments = args;

    protected override void InitializeTypes()
    {
        AddType<MinamoOptionTypeInfo>();
        AddType<MinamoResultTypeInfo>();
        AddType<MinamoByteArrayTypeInfo>();
        AddType<MinamoJsonTypeInfo>();
        hostRootType = AddType<MinamoHostRootTypeInfo>();
    }

    protected override void Execute(ExecutionContext ctx) => Add("args", startupArguments ?? Nil);

    [MinamoStaticMethod("referenceEquals")]
    public static bool Equals(MinamoObject value, MinamoObject other) => ReferenceEquals(value, other);

    [MinamoStaticMethod("isCallable")]
    public static bool IsCallable(ExecutionContext ctx, MinamoObject value) => value.TryGetFunction(ctx, out _);

    [MinamoStaticMethod("alias")]
    public static void Alias(ExecutionContext ctx, MinamoObject select, string name)
    {
        MinamoSelectAliases.Register(ctx, select, name);
    }

    [MinamoStaticMethod("request")]
    public static MinamoObject Request(
        ExecutionContext ctx,
        string kind,
        [Default] MinamoObject payload)
    {
        if (!ctx.SelectRequestsAllowed)
        {
            return ctx.InvalidOperation();
        }
        if (string.IsNullOrWhiteSpace(kind))
        {
            return ctx.InvalidValue(MinamoString.Get(kind));
        }

        return new MinamoSelectRequestAwaitable(kind, payload ?? Nil);
    }

    [MinamoStaticMethod("print")]
    public static void Print(ExecutionContext ctx, [VarArg]MinamoTuple values, [Default(",")]string separator, [Default("\n")]MinamoObject terminator)
    {
        var fst = true;
        
        foreach (var a in values)
        {
            if (!fst && !string.IsNullOrEmpty(separator))
            {
                WriteOutput(ctx, separator);
            }

            if (a is MinamoString s)
            {
                WriteOutput(ctx, s.Value);
            }
            else
            {
                WriteOutput(ctx, a.ToString(ctx).Value);
            }

            fst = false;

            if (ctx.Error is not null)
            {
                break;
            }
        }

        if (terminator.TypeId is MinamoTypeCodes.String or MinamoTypeCodes.Char)
        {
            WriteOutput(ctx, terminator.ToString());
        }
        else if (terminator.TypeId is not MinamoTypeCodes.Nil)
        {
            throw new MinamoCodeException(MinamoError.InvalidType, terminator);
        }
    }

    [MinamoStaticMethod("fmt")]
    public static MinamoObject Format(ExecutionContext ctx, [VarArg]MinamoTuple values)
    {
        if (values.Count == 0)
        {
            return ctx.InvalidType("String");
        }

        var template = values[0];

        if (template.TypeId is not MinamoTypeCodes.String and not MinamoTypeCodes.Char)
        {
            return ctx.InvalidType(MinamoTypeCodes.String, template);
        }

        var result = MinamoStringTypeInfo.Format(ctx, template.ToString(), values.ToArray()[1..]);
        return ctx.HasErrors ? Nil : MinamoString.Get(result!);
    }

    private static void WriteOutput(ExecutionContext ctx, string value)
    {
        var environment = ctx.GetContextVariable<MinamoEnvironment>(MinamoEnvironment.ContextKey);
        if (environment is not null)
        {
            environment.Write(value);
            return;
        }

        Console.Write(value);
    }

    [MinamoStaticMethod("constructorName")]
    public static string? GetConstructorName(MinamoObject value) => value is IProduction c ? c.Constructor : null;

    [MinamoStaticMethod("Exception")]
    public static MinamoObject CreateException(string name, [VarArg]MinamoTuple? data = null)
    {
        var payload = data ?? MinamoTuple.Empty;
        var message = payload.Count == 0 ? string.Empty : payload[0].ToString() ?? string.Empty;
        return new MinamoExceptionObject(name, message, payload);
    }

    [MinamoStaticMethod("typeName")]
    public static string GetTypeName(MinamoObject value)
    {
        if (value.TypeId is MinamoTypeCodes.TypeInfo)
        {
            return ((MinamoTypeInfo)value).ReflectedTypeName;
        }
        else
        {
            return value.TypeName;
        }
    }

    [MinamoStaticMethod("caller")]
    public static MinamoObject GetCaller(ExecutionContext ctx)
    {
        if (ctx.CallStack.Count > 2)
        {
            var cp = ctx.CallStack[^2];
            if (!ReferenceEquals(cp, Caller.External))
            {
                return cp.Function;
            }
        }

        return Nil;
    }

    [MinamoStaticMethod("assert")]
    public static void Assert(ExecutionContext ctx, [Default(true)]MinamoObject expect, MinamoObject got, string? errorText = null)
    {
        if (!Eq(ctx, expect.ToObject(), got.ToObject()))
        {
            if (errorText is not null)
            {
                ctx.AssertionFailed(errorText);
            }
            else
            {
                ctx.AssertionFailed($"Expected \"{expect.ToString(ctx)}\" :: {expect.TypeName}, got \"{got.ToString(ctx)}\" :: {got.TypeName}.");
            }
        }
    }

    private static bool Eq(ExecutionContext ctx, object? x, object? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is string a && y is string b)
        {
            a = a.Replace("\r\n", "\n");
            b = b.Replace("\r\n", "\n");
            return Equals(a, b);
        }

        if (x is IList xs && y is IList ys)
        {
            if (xs.Count != ys.Count)
            {
                return false;
            }

            for (var i = 0; i < xs.Count; i++)
            {
                if (!Eq(ctx, xs[i], ys[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (x is MinamoObject xa && y is MinamoObject ba)
        {
            return xa.Equals(ba, ctx);
        }

        return Equals(x, y);
    }

}
