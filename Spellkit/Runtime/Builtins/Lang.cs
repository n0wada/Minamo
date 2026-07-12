using Spellkit.Codegen;
using Spellkit.Compiler;
using Spellkit.Hosting;
using Spellkit.Parser;
using Spellkit.Parser.Model;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spellkit.Linker;

[GeneratedModule]
internal sealed partial class Lang : ForeignUnit
{
    private readonly SpkTuple? startupArguments;
    private const string VarConsoleOutput = "sys.ConsoleOutput";
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

    [SpkStaticMethod("mixins")]
    public static SpkObject[] GetMixins(ExecutionContext ctx, SpkObject value)
    {
        var ti = value.GetTypeInfo(ctx);
        return ti.GetMixins().Select(i => ctx.RuntimeContext.Types[i]).ToArray();
    }

    [SpkStaticMethod("toString")]
    public static string DirectToString(SpkObject value) => value.ToString();

    [SpkStaticMethod("length")]
    public static SpkObject GetLength(SpkObject value)
    {
        if (value is IMeasurable seq)
        {
            return new SpkInteger(seq.Count);
        }
        else if (value is SpkClass cls)
        {
            return new SpkInteger(cls.Fields.Count);
        }
        else
        {
            return Nil;
        }
    }

    [SpkStaticMethod("referenceEquals")]
    public static bool Equals(SpkObject value, SpkObject other) => ReferenceEquals(value, other);

    [SpkStaticMethod("clone")]
    public static SpkObject Clone(SpkObject value) => value.Clone() ?? Nil;

    [SpkStaticMethod("print")]
    public static void Print(ExecutionContext ctx, [VarArg]SpkTuple values, [Default(",")]string separator, [Default("\n")]SpkObject terminator)
    {
        var fst = true;
        
        foreach (var a in values)
        {
            if (!fst && !string.IsNullOrEmpty(separator))
            {
                Console.Write(separator);
            }

            if (a is SpkString s)
            {
                Console.Write(s.Value);
            }
            else
            {
                Console.Write(a.ToString(ctx));
            }

            fst = false;

            if (ctx.Error is not null)
            {
                break;
            }
        }

        if (terminator.TypeId is Spk.String or Spk.Char)
        {
            Console.Write(terminator.ToString());
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

    [SpkStaticMethod("setOut")]
    public static void SetOutput(ExecutionContext ctx, SpkObject? output = null)
    {
        if (output is null)
        {
            var outputWriter = ctx.GetContextVariable<TextWriter>(VarConsoleOutput);
            if (outputWriter is not null)
            {
                Console.SetOut(outputWriter);
            }
        }
        else
        {
            if (!ctx.HasContextVariable(VarConsoleOutput))
            {
                ctx.SetContextVariable(VarConsoleOutput, Console.Out);
            }

            Console.SetOut(new ConsoleTextWriter(ctx, output));
        }
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

    [SpkStaticMethod("instanceMember")]
    public static SpkObject GetInstanceMember(ExecutionContext ctx, SpkObject value, string name)
    {
        var member = ctx.RuntimeContext.Types[value.TypeId].LookupInstanceMember(ctx, value, name);

        if (member is not null)
        {
            return member.BindToInstance(ctx, value);
        }

        return Nil;
    }

    [SpkStaticMethod("staticMember")]
    public static SpkObject GetStaticMember(ExecutionContext ctx, SpkObject value, string name)
    {
        var typeId = value is SpkTypeInfo typ ? typ.ReflectedTypeId : value.TypeId;
        var member = ctx.RuntimeContext.Types[typeId].LookupStaticMember(ctx, name);
        return member ?? Nil;
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

    [SpkStaticMethod("current")]
    public static SpkObject Current(ExecutionContext ctx)
    {
        if (ctx.CallStack.Count > 1)
        {
            return ctx.CallStack.Peek().Function;
        }

        return Nil;
    }

    [SpkStaticMethod("readLine")]
    public static string Read() => Console.ReadLine() ?? "";

    [SpkStaticMethod("rnd")]
    public static int Randomize(ExecutionContext ctx, int min = 0, int max = int.MaxValue, int? seed = null)
    {
        if (seed is null)
        {
            var dt = DateTime.UtcNow;
            var dt2 = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0);
            seed = (int)(dt2 - dt).Ticks;
        }

        if (min > max)
        {
            ctx.InvalidValue("min", "max");
            return default;
        }

        var rnd = new Random(seed.Value);
        return rnd.Next(min, max);
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

    [SpkStaticMethod("sqrt")]
    public static double Sqrt(double x) => Math.Sqrt(x);

    [SpkStaticMethod("pow")]
    public static double Pow(double x, double y) => Math.Pow(x, y);

    [SpkStaticMethod("min")]
    public static SpkObject Min(ExecutionContext ctx, SpkObject x, SpkObject y)
    {
        if (x.Lesser(y, ctx))
        {
            return x;
        }
        else
        {
            return y;
        }
    }

    [SpkStaticMethod("max")]
    public static SpkObject Max(ExecutionContext ctx, SpkObject x, SpkObject y)
    {
        if (x.Greater(y, ctx))
        {
            return x;
        }
        else
        {
            return y;
        }
    }

    [SpkStaticMethod("abs")]
    public static SpkObject Abs(ExecutionContext ctx, SpkObject value)
    {
        if (value.Lesser(SpkInteger.Zero, ctx))
        {
            return value.Negate(ctx);
        }

        return value;
    }

    [SpkStaticMethod("round")]
    public static double Round(double number, int digits = 2) => Math.Round(number, digits);

    [SpkStaticMethod("sign")]
    public static SpkObject Sign(ExecutionContext ctx, SpkObject x)
    {
        if (ReferenceEquals(x, SpkInteger.Zero))
        {
            return SpkInteger.Zero;
        }

        if (x.Lesser(SpkInteger.Zero, ctx))
        {
            return SpkInteger.MinusOne;
        }

        return SpkInteger.One;
    }

    [SpkStaticMethod("parse")]
    public static SpkObject Parse(ExecutionContext ctx, string expression)
    {
        var res = SpkParser.Parse(SourceBuffer.FromString(expression));

        if (!res.Success)
        {
            return ctx.ParsingFailed(res.Messages
                .Where(m => m.Type == BuildMessageType.Error).First().ToString());
        }

        if (res.Value!.Root is null || res.Value!.Root.Nodes.Count == 0)
        {
            return ctx.ParsingFailed("Empty expression.");
        }
        else if (res.Value!.Root.Nodes.Count > 1)
        {
            return ctx.ParsingFailed("Only single expressions allowed.");
        }

        return LiteralEvaluator.Eval(res.Value!.Root.Nodes[0]);
    }

    [SpkStaticMethod("__invoke")]
    public static SpkObject Invoke(ExecutionContext ctx, SpkObject functor, params SpkObject[] values) => functor.Invoke(ctx, values);
}

internal static class LiteralEvaluator
{
    public static SpkObject Eval(SyntaxNode node) =>
        node.NodeType switch
        {
            NodeType.ExpressionStatement => Eval(((ExpressionStatementSyntax)node).Expression),
            NodeType.Nil => Nil,
            NodeType.Boolean => ((BooleanLiteralSyntax)node).Value ? True : False,
            NodeType.Char => new SpkChar(((CharLiteralSyntax)node).Value),
            NodeType.String => GetStringValue((StringLiteralSyntax)node),
            NodeType.Integer => SpkInteger.Get(((IntegerLiteralSyntax)node).Value),
            NodeType.Float => new SpkFloat(((FloatLiteralSyntax)node).Value),
            NodeType.Tuple => new SpkArray(GetArray(((TupleLiteralSyntax)node).Elements, out _)),
            NodeType.Array => ProcessArrayLiteral((ArrayLiteralSyntax)node),
            _ => throw new SpkCodeException(SpkError.ParsingFailed, $"Node of type \"{node.NodeType}\" is not supported.")
        };

    private static SpkObject ProcessArrayLiteral(ArrayLiteralSyntax node)
    {
        var arr = GetArray(node.Elements, out var hasLabels);

        if (!hasLabels)
        {
            return new SpkArray(arr);
        }

        var dict = new Dictionary<SpkObject, SpkObject>();

        foreach (var v in arr)
        {
            if (v is SpkLabel lab)
            {
                dict.Add(new SpkString(lab.Label), lab.Value);
            }
            else
            {
                throw new SpkCodeException(SpkError.ParsingFailed, $"Invalid dictionary literal.");
            }
        }

        return new SpkDictionary(dict);
    }

    private static SpkObject GetStringValue(StringLiteralSyntax lit)
    {
        if (lit.Value is null)
        {
            throw new SpkCodeException(SpkError.ParsingFailed, $"Invalid string literal.");
        }

        return SpkString.Get(lit.Value);
    }

    private static SpkObject[] GetArray(List<SyntaxNode> nodes, out bool hasLabels)
    {
        var arr = new SpkObject[nodes.Count];
        hasLabels = false;

        for (var i = 0; i < nodes.Count; i++)
        {
            var e = nodes[i];
            SpkObject obj;

            if (e.NodeType == NodeType.Label)
            {
                var lab = (LabelLiteralSyntax)e;
                obj = new SpkLabel(lab.Label, Eval(lab.Expression));
                hasLabels = true;
            }
            else
            {
                obj = Eval(e);
            }

            arr[i] = obj;
        }

        return arr;
    }
}
