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
    private readonly SpkTuple? startupArguments;
    private SpellkitHostRootTypeInfo hostRootType = null!;

    public Lang(bool exposeHostObject = true)
    {
        FileName = "lang";
        if (exposeHostObject)
        {
            Add("host", new SpellkitHostRoot(hostRootType));
        }
    }

    public Lang(SpkTuple? args, bool exposeHostObject = true) : this(exposeHostObject) =>
        startupArguments = args;

    protected override void InitializeTypes()
    {
        AddType<SpkOptionTypeInfo>();
        AddType<SpkResultTypeInfo>();
        hostRootType = AddType<SpellkitHostRootTypeInfo>();
    }

    protected override void Execute(ExecutionContext ctx) => Add("args", startupArguments ?? Nil);

    [SpkStaticMethod("referenceEquals")]
    public static bool Equals(SpkObject value, SpkObject other) => ReferenceEquals(value, other);

    [SpkStaticMethod("isCallable")]
    public static bool IsCallable(ExecutionContext ctx, SpkObject value) => value.TryGetFunction(ctx, out _);

    [SpkStaticMethod("alias")]
    public static void Alias(ExecutionContext ctx, SpkObject select, string name)
    {
        SpellkitSelectAliases.Register(ctx, select, name);
    }

    [SpkStaticMethod("print")]
    public static void Print(ExecutionContext ctx, [VarArg]SpkTuple values, [Default(",")]string separator, [Default("\n")]SpkObject terminator)
    {
        var fst = true;
        
        foreach (var a in values)
        {
            if (!fst && !string.IsNullOrEmpty(separator))
            {
                WriteOutput(ctx, separator);
            }

            if (a is SpkString s)
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

        if (terminator.TypeId is Spk.String or Spk.Char)
        {
            WriteOutput(ctx, terminator.ToString());
        }
        else if (terminator.TypeId is not Spk.Nil)
        {
            throw new SpkCodeException(SpkError.InvalidType, terminator);
        }
    }

    [SpkStaticMethod("fmt")]
    public static SpkObject Format(ExecutionContext ctx, [VarArg]SpkTuple values)
    {
        if (values.Count == 0)
        {
            return ctx.InvalidType("String");
        }

        var template = values[0];

        if (template.TypeId is not Spk.String and not Spk.Char)
        {
            return ctx.InvalidType(Spk.String, template);
        }

        var result = SpkStringTypeInfo.Format(ctx, template.ToString(), values.ToArray()[1..]);
        return ctx.HasErrors ? Nil : SpkString.Get(result!);
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

    [SpkStaticMethod("constructorName")]
    public static string? GetConstructorName(SpkObject value) => value is IProduction c ? c.Constructor : null;

    [SpkStaticMethod("Exception")]
    public static SpkObject CreateException(string name, [VarArg]SpkTuple? data = null)
    {
        var payload = data ?? SpkTuple.Empty;
        var message = payload.Count == 0 ? string.Empty : payload[0].ToString() ?? string.Empty;
        return new SpkExceptionObject(name, message, payload);
    }

    [SpkStaticMethod("typeName")]
    public static string GetTypeName(SpkObject value)
    {
        if (value.TypeId is Spk.TypeInfo)
        {
            return ((SpkTypeInfo)value).ReflectedTypeName;
        }
        else
        {
            return value.TypeName;
        }
    }

    [SpkStaticMethod("caller")]
    public static SpkObject GetCaller(ExecutionContext ctx)
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

    [SpkStaticMethod("assert")]
    public static void Assert(ExecutionContext ctx, [Default(true)]SpkObject expect, SpkObject got, string? errorText = null)
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

        if (x is SpkObject xa && y is SpkObject ba)
        {
            return xa.Equals(ba, ctx);
        }

        return Equals(x, y);
    }

}
