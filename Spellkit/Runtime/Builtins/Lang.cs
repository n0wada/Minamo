using Spellkit.Codegen;
using Spellkit.Compiler;
using Spellkit.Hosting;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections;

namespace Spellkit.Linker;

[GeneratedModule]
internal sealed partial class Lang : ForeignUnit
{
    private readonly SpellkitTuple? startupArguments;
    private SpellkitHostRootTypeInfo hostRootType = null!;

    public Lang(bool exposeHostObject = true)
    {
        FileName = "lang";
        if (exposeHostObject)
        {
            Add("host", new SpellkitHostRoot(hostRootType));
        }
    }

    public Lang(SpellkitTuple? args, bool exposeHostObject = true) : this(exposeHostObject) =>
        startupArguments = args;

    protected override void InitializeTypes()
    {
        AddType<SpellkitOptionTypeInfo>();
        AddType<SpellkitResultTypeInfo>();
        AddType<SpellkitByteArrayTypeInfo>();
        AddType<SpellkitJsonTypeInfo>();
        hostRootType = AddType<SpellkitHostRootTypeInfo>();
    }

    protected override void Execute(ExecutionContext ctx) => Add("args", startupArguments ?? Nil);

    [SpellkitStaticMethod("referenceEquals")]
    public static bool Equals(SpellkitObject value, SpellkitObject other) => ReferenceEquals(value, other);

    [SpellkitStaticMethod("isCallable")]
    public static bool IsCallable(ExecutionContext ctx, SpellkitObject value) => value.TryGetFunction(ctx, out _);

    [SpellkitStaticMethod("alias")]
    public static void Alias(ExecutionContext ctx, SpellkitObject select, string name)
    {
        SpellkitSelectAliases.Register(ctx, select, name);
    }

    [SpellkitStaticMethod("print")]
    public static void Print(ExecutionContext ctx, [VarArg]SpellkitTuple values, [Default(",")]string separator, [Default("\n")]SpellkitObject terminator)
    {
        var fst = true;
        
        foreach (var a in values)
        {
            if (!fst && !string.IsNullOrEmpty(separator))
            {
                WriteOutput(ctx, separator);
            }

            if (a is SpellkitString s)
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

        if (terminator.TypeId is SpellkitTypeCodes.String or SpellkitTypeCodes.Char)
        {
            WriteOutput(ctx, terminator.ToString());
        }
        else if (terminator.TypeId is not SpellkitTypeCodes.Nil)
        {
            throw new SpellkitCodeException(SpellkitError.InvalidType, terminator);
        }
    }

    [SpellkitStaticMethod("fmt")]
    public static SpellkitObject Format(ExecutionContext ctx, [VarArg]SpellkitTuple values)
    {
        if (values.Count == 0)
        {
            return ctx.InvalidType("String");
        }

        var template = values[0];

        if (template.TypeId is not SpellkitTypeCodes.String and not SpellkitTypeCodes.Char)
        {
            return ctx.InvalidType(SpellkitTypeCodes.String, template);
        }

        var result = SpellkitStringTypeInfo.Format(ctx, template.ToString(), values.ToArray()[1..]);
        return ctx.HasErrors ? Nil : SpellkitString.Get(result!);
    }

    private static void WriteOutput(ExecutionContext ctx, string value)
    {
        var environment = ctx.GetContextVariable<SpellkitEnvironment>(SpellkitEnvironment.ContextKey);
        if (environment is not null)
        {
            environment.Write(value);
            return;
        }

        Console.Write(value);
    }

    [SpellkitStaticMethod("constructorName")]
    public static string? GetConstructorName(SpellkitObject value) => value is IProduction c ? c.Constructor : null;

    [SpellkitStaticMethod("Exception")]
    public static SpellkitObject CreateException(string name, [VarArg]SpellkitTuple? data = null)
    {
        var payload = data ?? SpellkitTuple.Empty;
        var message = payload.Count == 0 ? string.Empty : payload[0].ToString() ?? string.Empty;
        return new SpellkitExceptionObject(name, message, payload);
    }

    [SpellkitStaticMethod("typeName")]
    public static string GetTypeName(SpellkitObject value)
    {
        if (value.TypeId is SpellkitTypeCodes.TypeInfo)
        {
            return ((SpellkitTypeInfo)value).ReflectedTypeName;
        }
        else
        {
            return value.TypeName;
        }
    }

    [SpellkitStaticMethod("caller")]
    public static SpellkitObject GetCaller(ExecutionContext ctx)
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

    [SpellkitStaticMethod("assert")]
    public static void Assert(ExecutionContext ctx, [Default(true)]SpellkitObject expect, SpellkitObject got, string? errorText = null)
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

        if (x is SpellkitObject xa && y is SpellkitObject ba)
        {
            return xa.Equals(ba, ctx);
        }

        return Equals(x, y);
    }

}
