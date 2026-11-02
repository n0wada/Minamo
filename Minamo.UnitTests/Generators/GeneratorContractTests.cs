using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Minamo.Debug;
using Minamo.Generators;
using Minamo.Hosting;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Reflection;
using Xunit;
using ExecutionContext = Minamo.Runtime.ExecutionContext;

namespace Minamo.UnitTesting.Generators;

[Trait("Suite", "Generator")]
public sealed class GeneratorContractTests
{
    private const string SnapshotSource = """
        using Minamo.Hosting;
        using System.Linq;

        namespace GeneratorFixture;

        [MinamoModule("snapshot")]
        public static class SnapshotCommands
        {
            [MinamoCommand("echo", Description = "Echoes text.", Capability = "snapshot.read")]
            public static string Echo(string value, int count = 2) =>
                string.Concat(Enumerable.Repeat(value, count));

            [MinamoCommand]
            public static int? Maybe(int? value) => value;
        }
        """;

    [Fact]
    public void GeneratedSourceMatchesApprovedSnapshot()
    {
        var first = Generate(SnapshotSource);
        var second = Generate(SnapshotSource);

        AssertNoErrors(first.Diagnostics);
        AssertNoErrors(first.Compilation.GetDiagnostics());
        Assert.Equal(first.GeneratedSource, second.GeneratedSource);
        AssertSnapshot("SnapshotCommands.generated.cs", first.GeneratedSource);
    }

    [Fact]
    public void GeneratedAssemblyRegistersAndExecutesCommands()
    {
        const string source = """
            using Minamo.Hosting;
            using System.Linq;

            namespace GeneratorFixture;

            [MinamoModule("sample")]
            public sealed class SampleCommands
            {
                [MinamoProperty(Description = "Current count.", Capability = "sample.write")]
                public int Count { get; set; } = 1;

                [MinamoProperty]
                public string Name => "sample";

                [MinamoCommand("echo")]
                public string Echo(MinamoCommandContext context, string value, int count = 2) =>
                    string.Concat(Enumerable.Repeat(value, count));
            }
            """;

        var generated = Generate(source, "Minamo.GeneratorExecutionFixture");
        AssertNoErrors(generated.Diagnostics);
        AssertNoErrors(generated.Compilation.GetDiagnostics());

        using var stream = new MemoryStream();
        var emit = generated.Compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        var assembly = Assembly.Load(stream.ToArray());
        var commandsType = assembly.GetType("GeneratorFixture.SampleCommands")!;
        var extensionsType = assembly.GetType(
            "GeneratorFixture.SampleCommandsHostingExtensions")!;
        Assert.Null(extensionsType.GetMethod(
            "AddSampleCommands",
            BindingFlags.Public | BindingFlags.Static));
        var addCommands = extensionsType.GetMethod(
            "AddModule",
            BindingFlags.Public | BindingFlags.Static)!;
        var instance = Activator.CreateInstance(commandsType)!;
        var host = (MinamoHost)addCommands.Invoke(
            null,
            new[] { new MinamoHost(), instance })!;

        using var session = host.CreateInstance();
        var result = session.Execute("""
            import sample
            assert("oneone", sample.echo("one"))
            assert(true, isCallable(sample.echo))
            assert(1, sample.Count)
            sample.Count = 3
            assert(3, sample.Count)
            assert("sample", sample.Name)
            """);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("sample.Count", session.Environment.Commands.Describe("sample.Count")!.Name);
        Assert.Null(session.Environment.Commands.Describe("sample.set_Count"));
        Assert.False(session.Execute("host.Commands").Success);
        var readOnly = session.Execute("""
            import sample
            sample.Name = "changed"
            """);
        Assert.False(readOnly.Success);

        var restrictedHost = (MinamoHost)addCommands.Invoke(
            null,
            new object[]
            {
                new MinamoHost().AddCapabilities("other"),
                Activator.CreateInstance(commandsType)!
            })!;
        using var restricted = restrictedHost.CreateInstance();
        Assert.False(restricted.Execute("import sample\nsample.Count").Success);
        Assert.False(restricted.Execute("import sample\nsample.Count = 2").Success);
    }

    [Fact]
    public void FailedCommandConversionsDoNotInvokeCommandsOrPropertySetters()
    {
        const string source = """
            using Minamo.Hosting;
            using Minamo.Runtime.Types;
            using System.Threading.Tasks;

            namespace GeneratorFixture;

            [MinamoModule("probe")]
            public static class GuardCommands
            {
                private static int calls;
                private static int count = 7;

                [MinamoProperty]
                public static int Calls => calls;

                [MinamoProperty]
                public static int Count
                {
                    get => count;
                    set { calls++; count = value; }
                }

                [MinamoCommand]
                public static int Touch(int first, int second) { calls++; return first + second; }
                [MinamoCommand]
                public static void TouchVoid(int value) { calls++; }
                [MinamoCommand]
                public static MinamoObject TouchObject(int value) { calls++; return MinamoInteger.Get(value); }
                [MinamoCommand]
                public static Task<int> TouchTask(int value) { calls++; return Task.FromResult(value); }
                [MinamoCommand]
                public static ValueTask<int> TouchValueTask(int value) { calls++; return new ValueTask<int>(value); }
            }
            """;

        using var session = CreateInstance(Generate(source));
        Assert.True(session.Execute("import probe").Success);
        foreach (var expression in new[]
        {
            "probe.Touch(\"invalid\", 2)",
            "probe.Touch(1, \"invalid\")",
            "probe.Touch(1, 2147483648)",
            "probe.TouchVoid(\"invalid\")",
            "probe.TouchObject(\"invalid\")",
            "probe.TouchTask(\"invalid\")",
            "probe.TouchValueTask(\"invalid\")",
            "probe.Count = \"invalid\""
        })
        {
            Assert.False(session.Execute(expression).Success);
            var state = session.Execute("assert(0, probe.Calls)\nassert(7, probe.Count)");
            Assert.True(state.Success, state.Failure?.Message);
        }

        var valid = session.Execute("""
            assert(3, probe.Touch(1, 2))
            assert(nil, probe.TouchVoid(5))
            assert(4, probe.TouchObject(4))
            assert(6, probe.TouchTask(6))
            assert(7, probe.TouchValueTask(7))
            probe.Count = 9
            assert(9, probe.Count)
            assert(6, probe.Calls)
            """);
        Assert.True(valid.Success, valid.Failure?.Message);
    }

    [Fact]
    public void ExtensionAssemblyRegistersGeneratedModulesWithoutALibraryInterface()
    {
        var generated = Generate(SnapshotSource, "Minamo.ExtensionLoadingFixture");
        AssertNoErrors(generated.Diagnostics);
        AssertNoErrors(generated.Compilation.GetDiagnostics());

        using var stream = new MemoryStream();
        var emit = generated.Compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

        var assembly = Assembly.Load(stream.ToArray());
        var host = new MinamoHost();
        ExtensionLibraryLoader.RegisterAssembly(host, assembly, assembly.GetName().Name!);
        using var instance = host.CreateInstance();
        var result = instance.Execute("import snapshot\nsnapshot.echo(\"Minamo\", 1)");

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("Minamo", result.GetValue<string>());
    }

    [Fact]
    public void DiagnosticsIncludeIdLocationAndMessageArguments()
    {
        const string source = """
            using Minamo.Hosting;

            namespace GeneratorFixture;

            [MinamoModule("")]
            public static class EmptyName { }

            [MinamoModule("duplicate")]
            public static class DuplicateCommands
            {
                [MinamoCommand("same")]
                public static void First() { }

                [MinamoCommand("same")]
                public static void Second() { }
            }

            [MinamoModule("invalid")]
            public static class InvalidParameter
            {
                [MinamoCommand]
                public static void Ref(ref int value) { }
            }

            [MinamoModule("invalid-property")]
            public sealed class InvalidProperty
            {
                [MinamoProperty]
                public int this[int index] => index;
            }
            """;

        var diagnostics = Errors(Generate(source));

        AssertDiagnostic(
            diagnostics,
            "MinamoH001",
            "EmptyName",
            "MinamoModule on 'EmptyName' requires a non-empty module name.");
        AssertDiagnostic(
            diagnostics,
            "MinamoH003",
            "Second",
            "Module 'duplicate' contains more than one command named 'same'.");
        AssertDiagnostic(
            diagnostics,
            "MinamoH004",
            "value",
            "Parameter 'value' on command 'Ref' is not supported: "
                + "ref, in, and out parameters are not supported");
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "MinamoH007"
            && diagnostic.GetMessage().Contains(
                "indexers are not supported",
                StringComparison.Ordinal));
    }

    [Fact]
    public void NullableValueAndReferenceTypesGenerateValidBindings()
    {
        const string source = """
            #nullable enable
            using Minamo.Hosting;

            namespace GeneratorFixture;

            [MinamoModule("nullable")]
            public static class NullableCommands
            {
                [MinamoCommand]
                public static int? Number(int? value) => value;

                [MinamoCommand]
                public static string? Text(string? value) => value;
            }
            """;

        var result = Generate(source);

        AssertNoErrors(result.Diagnostics);
        AssertNoErrors(result.Compilation.GetDiagnostics());
        Assert.Contains("result.HasValue", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains(
            "MinamoCommandParameter.Required<int?>(\"value\")",
            result.GeneratedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "MinamoCommandParameter.Required<string>(\"value\")",
            result.GeneratedSource,
            StringComparison.Ordinal);

        using var session = CreateInstance(result);
        var execution = session.Execute("""
            import nullable
            assert(nil, nullable.Number(nil))
            assert(nil, nullable.Text(nil))
            assert(42, nullable.Number(42))
            assert("text", nullable.Text("text"))
            """);
        Assert.True(execution.Success, execution.Failure?.Message);
    }

    [Fact]
    public void GenericModulesAndMethodsAreRejected()
    {
        const string source = """
            using Minamo.Hosting;

            namespace GeneratorFixture;

            [MinamoModule("generic-module")]
            public static class GenericModule<T>
            {
                [MinamoCommand]
                public static int Echo(int value) => value;
            }

            [MinamoModule("generic-method")]
            public static class GenericMethodModule
            {
                [MinamoCommand]
                public static T Echo<T>(T value) => value;
            }
            """;

        var diagnostics = Errors(Generate(source));

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "MinamoH002"
            && diagnostic.GetMessage().Contains(
                "generic module classes are not supported",
                StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "MinamoH002"
            && diagnostic.GetMessage().Contains(
                "generic methods are not supported",
                StringComparison.Ordinal));
    }

    [Fact]
    public void OverloadsRequireDistinctExposedCommandNames()
    {
        const string validSource = """
            using Minamo.Hosting;

            namespace GeneratorFixture;

            [MinamoModule("overloads")]
            public static class OverloadCommands
            {
                [MinamoCommand("integer")]
                public static int Echo(int value) => value;

                [MinamoCommand("text")]
                public static string Echo(string value) => value;
            }
            """;
        const string invalidSource = """
            using Minamo.Hosting;

            namespace GeneratorFixture;

            [MinamoModule("overloads")]
            public static class OverloadCommands
            {
                [MinamoCommand("echo")]
                public static int First(int value) => value;

                [MinamoCommand("echo")]
                public static string Second(string value) => value;
            }
            """;

        var valid = Generate(validSource);
        var invalid = Errors(Generate(invalidSource));

        AssertNoErrors(valid.Diagnostics);
        AssertNoErrors(valid.Compilation.GetDiagnostics());
        Assert.Contains("RawCommand(\"integer\"", valid.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("RawCommand(\"text\"", valid.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains(invalid, diagnostic => diagnostic.Id == "MinamoH003");
    }

    [Fact]
    public void TaskAndValueTaskCommandsAreGenerated()
    {
        const string source = """
            using Minamo.Hosting;
            using System.Threading.Tasks;

            namespace GeneratorFixture;

            [MinamoModule("async")]
            public static class AsyncCommands
            {
                [MinamoCommand]
                public static Task<int> TaskCommand() => Task.FromResult(1);
            }

            [MinamoModule("value-task")]
            public static class ValueTaskCommands
            {
                [MinamoCommand]
                public static ValueTask<int> ValueTaskCommand() => ValueTask.FromResult(1);
            }
            """;

        var result = Generate(source);

        AssertNoErrors(result.Diagnostics);
        Assert.Contains("FromAwaitable", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueTaskTypeMethodsAreGenerated()
    {
        const string source = """
            using Minamo.Codegen;
            using Minamo.Runtime.Types;
            using System.Threading.Tasks;

            namespace GeneratorFixture;

            [MinamoType]
            public sealed partial class AsyncTypeInfo : MinamoForeignTypeInfo
            {
                public override string ReflectedTypeName => "Async";

                [MinamoStaticMethod]
                public static ValueTask<int> Value() => ValueTask.FromResult(1);
            }
            """;

        var result = Generate(source);

        AssertNoErrors(result.Diagnostics);
        AssertNoErrors(result.Compilation.GetDiagnostics());
        Assert.Contains("FromAwaitable", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void NullableBooleanTypeBindingsPreserveNullAndTruthValues()
    {
        const string source = """
            using Minamo.Codegen;
            using Minamo.Runtime;
            using Minamo.Runtime.Types;
            namespace GeneratorFixture;
            [MinamoType]
            public sealed partial class BooleanTypeInfo : MinamoForeignTypeInfo
            {
                public override string ReflectedTypeName => "BooleanFixture";
                [MinamoStaticMethod]
                public static bool? Optional(bool? value = null) => value;
                [MinamoStaticMethod]
                public static bool? Required(bool? value) => value;
            }
            """;
        var (_, info) = CreateTypeBindingFixture(source, "GeneratorFixture.BooleanTypeInfo");
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        var optional = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "Optional"));
        var required = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "Required"));

        Assert.Same(MinamoNil.Instance, optional.Call(context));
        Assert.Same(MinamoNil.Instance, optional.Call(context, MinamoNil.Instance));
        foreach (var function in new[] { optional, required })
        {
            Assert.Same(MinamoBool.True, function.Call(context, MinamoBool.True));
            Assert.Same(MinamoBool.False, function.Call(context, MinamoBool.False));
            Assert.Same(MinamoBool.True, function.Call(context, MinamoInteger.Zero));
        }
        Assert.False(context.HasErrors);
    }

    [Fact]
    public void SinglePrecisionTypeBindingsSupportValuesAndNullableReturns()
    {
        const string source = """
            using Minamo.Codegen;
            using Minamo.Runtime;
            using Minamo.Runtime.Types;
            namespace GeneratorFixture;
            [MinamoType]
            public sealed partial class FloatTypeInfo : MinamoForeignTypeInfo
            {
                public override string ReflectedTypeName => "FloatFixture";
                [MinamoStaticMethod]
                public static float Value() => 1.5f;
                [MinamoStaticMethod]
                public static float? Optional(float? value = null) => value;
            }
            """;
        var (_, info) = CreateTypeBindingFixture(source, "GeneratorFixture.FloatTypeInfo");
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        var value = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "Value"));
        var optional = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "Optional"));

        Assert.Equal(1.5, Assert.IsType<MinamoFloat>(value.Call(context)).Value);
        Assert.Same(MinamoNil.Instance, optional.Call(context));
        Assert.Same(MinamoNil.Instance, optional.Call(context, MinamoNil.Instance));
        Assert.Equal(2.5, Assert.IsType<MinamoFloat>(optional.Call(context, new MinamoFloat(2.5))).Value);
        Assert.Equal(1.0, Assert.IsType<MinamoFloat>(optional.Call(context, MinamoInteger.One)).Value);
        Assert.False(context.HasErrors);
    }

    [Fact]
    public void MultipleArrayTypeArgumentsSupportCollectionsIteratorsAndConversionErrors()
    {
        const string source = """
            using Minamo.Codegen;
            using Minamo.Runtime;
            using Minamo.Runtime.Types;
            using ExecutionContext = Minamo.Runtime.ExecutionContext;
            namespace GeneratorFixture;
            [MinamoType]
            public sealed partial class ArraysTypeInfo : MinamoForeignTypeInfo
            {
                public override string ReflectedTypeName => "ArraysFixture";
                public static int Calls { get; private set; }
                [MinamoStaticMethod]
                public static int Count(int[] first, int[] second) { Calls++; return first.Length + second.Length; }
                [MinamoStaticMethod]
                public static int CountWithContext(ExecutionContext execution, int[] first, int[] second) =>
                    Count(first, second);
                [MinamoStaticMethod]
                public static int Optional(int[] values = null) => values is null ? -1 : values.Length;
            }
            """;
        var (_, info) = CreateTypeBindingFixture(source, "GeneratorFixture.ArraysTypeInfo");
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        var items = new MinamoObject[] { MinamoInteger.One, MinamoInteger.Two };
        var array = new MinamoArray(items);
        var functions = new[] { "Count", "CountWithContext" }.Select(name =>
            Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, name))).ToArray();

        foreach (var function in functions)
        {
            Assert.Equal(4, Assert.IsType<MinamoInteger>(function.Call(context, array, array)).Value);
            Assert.Equal(4, Assert.IsType<MinamoInteger>(function.Call(context,
                MinamoIterator.Create(items), MinamoIterator.Create(items))).Value);
            Assert.False(context.HasErrors);
            foreach (var invalid in new MinamoObject[]
            {
                MinamoBool.True,
                new MinamoArray(new MinamoObject[] { new MinamoString("invalid") })
            })
            {
                Assert.Same(MinamoNil.Instance, function.Call(context, array, invalid));
                Assert.True(context.HasErrors);
                context.Error = null;
            }
        }

        Assert.Equal(4, Assert.IsType<int>(info.GetType().GetProperty("Calls")!.GetValue(null)));
        var optional = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "Optional"));
        Assert.Equal(-1, Assert.IsType<MinamoInteger>(optional.Call(context)).Value);
        Assert.Equal(-1, Assert.IsType<MinamoInteger>(optional.Call(context, MinamoNil.Instance)).Value);
        Assert.Equal(2, Assert.IsType<MinamoInteger>(optional.Call(context, array)).Value);
        Assert.False(context.HasErrors);
    }

    [Fact]
    public void TypeMethodsShareInternalWrappersAcrossTypesAndKeepArgumentFallbacks()
    {
        var (assembly, info) = CreateTypeBindingFixture();
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        MinamoFunction Function(string name) =>
            Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, name));

        var zero = Function("Zero");
        var echo = Function("Echo");
        var second = Function("Second");
        var third = Function("Third");
        var optional = Function("Optional");
        var count = Function("Count");
        var arrayCount = Function("ArrayCount");
        var otherInfo = (MinamoTypeInfo)Activator.CreateInstance(
            assembly.GetType("GeneratorFixture.OtherTypeInfo")!)!;
        var otherEcho = Assert.IsAssignableFrom<MinamoFunction>(
            otherInfo.LookupStaticMember(context, "Echo"));

        Assert.Same(echo.GetType(), Function("EchoAgain").GetType());
        Assert.Same(echo.GetType(), Function("ContextEcho").GetType());
        Assert.Same(echo.GetType(), otherEcho.GetType());
        Assert.Same(third.GetType(), optional.GetType());
        Assert.Same(third.GetType(), count.GetType());
        Assert.Same(third.GetType(), arrayCount.GetType());
        Assert.NotSame(zero.GetType(), echo.GetType());
        Assert.NotSame(echo.GetType(), second.GetType());
        var wrappers = assembly.GetTypes().Where(type =>
            type.IsSealed && type.IsSubclassOf(typeof(MinamoForeignFunction))).ToArray();
        Assert.Equal(4, wrappers.Length);
        Assert.All(wrappers, type => Assert.False(type.IsVisible));

        Assert.Same(MinamoInteger.One, zero.Call(context));
        Assert.Same(MinamoInteger.One, echo.Call(context, MinamoInteger.One));
        Assert.Same(MinamoInteger.One, Function("ContextEcho").Call(context, MinamoInteger.One));
        Assert.Same(MinamoInteger.Two, second.Call(context, MinamoInteger.One, MinamoInteger.Two));
        Assert.Same(MinamoInteger.One, echo.Call(context, new MinamoObject[] { MinamoInteger.One }));
        Assert.Same(MinamoInteger.Two, second.Call(context,
            new MinamoObject[] { MinamoInteger.One, MinamoInteger.Two }));
        Assert.Same(MinamoInteger.One, third.Call(context,
            new MinamoObject[] { MinamoInteger.Two, MinamoInteger.Two, MinamoInteger.One }));
        Assert.Equal(7, Assert.IsType<MinamoInteger>(optional.Call(context)).Value);
        Assert.Equal(9, Assert.IsType<MinamoInteger>(Function("AttributeDefault").Call(context)).Value);
        Assert.Same(MinamoNil.Instance, Function("Nullable").Call(context));
        Assert.Equal(1, Assert.IsType<MinamoInteger>(Function("Nullable").Call(context, MinamoInteger.One)).Value);
        var values = new MinamoTuple(new MinamoObject[] { MinamoInteger.One, MinamoInteger.Two });
        Assert.Equal(2, Assert.IsType<MinamoInteger>(count.Call(context, values)).Value);
        Assert.Equal(2, Assert.IsType<MinamoInteger>(arrayCount.Call(context, values)).Value);
        Assert.False(context.HasErrors);
    }

    [Fact]
    public void SharedTypeBindingsPreserveFunctionIdentityCloningAndProperties()
    {
        var (assembly, info) = CreateTypeBindingFixture();
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        var first = new MinamoArray(new MinamoObject[] { MinamoInteger.One });
        var other = new MinamoArray(new MinamoObject[] { MinamoInteger.One });
        var template = info.LookupInstanceMember(context, first, "Set")!;
        var bound = Assert.IsAssignableFrom<MinamoFunction>(info.GetInstanceMember(first, "Set", context));
        var same = Assert.IsAssignableFrom<MinamoFunction>(info.GetInstanceMember(first, "Set", context));
        var different = Assert.IsAssignableFrom<MinamoFunction>(info.GetInstanceMember(other, "Set", context));
        var staticSet = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "Set"));

        Assert.True(bound.Equals(same));
        Assert.False(bound.Equals(different));
        Assert.False(staticSet.Equals(template));
        var echo = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "Echo"));
        var echoAgain = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "EchoAgain"));
        var otherInfo = (MinamoTypeInfo)Activator.CreateInstance(
            assembly.GetType("GeneratorFixture.OtherTypeInfo")!)!;
        var otherEcho = Assert.IsAssignableFrom<MinamoFunction>(otherInfo.LookupStaticMember(context, "Echo"));
        Assert.False(echo.Equals(echoAgain));
        Assert.False(echo.Equals(otherEcho));

        var clone = Assert.IsAssignableFrom<MinamoFunction>(bound.Clone());
        Assert.True(bound.Equals(clone));
        Assert.Same(first, clone.Self);
        Assert.Same(bound.Parameters, clone.Parameters);
        Assert.Same(MinamoInteger.Two, clone.Call(context, MinamoInteger.Two));
        Assert.Same(MinamoInteger.Two, first[0]);
        var callable = Assert.IsType<Func<ExecutionContext, MinamoObject[], MinamoObject>>(bound.ToObject());
        Assert.Same(MinamoInteger.One, callable(context, new MinamoObject[] { MinamoInteger.One }));
        Assert.Same(MinamoInteger.One, first[0]);

        clone.Parameters = new[] { new Par("value", MinamoInteger.Two) };
        Assert.Same(MinamoInteger.Two, clone.Call(context));
        var rebound = clone.BindToInstance(context, other);
        Assert.Same(other, rebound.Self);
        Assert.Null(rebound.Parameters[0].Value);
        Assert.NotSame(template.Parameters, rebound.Parameters);
        Assert.NotSame(clone.Parameters, rebound.Parameters);
        Assert.Equal("Set", rebound.FunctionName);
        Assert.Equal(1, Assert.IsType<MinamoInteger>(info.GetInstanceMember(first, "Size", context)).Value);
        Assert.Equal(42, Assert.IsType<MinamoInteger>(info.GetStaticMember("Default", context)).Value);
        var get = Assert.IsAssignableFrom<MinamoFunction>(info.GetInstanceMember(first, "Get", context));
        Assert.Same(first[0], get.Call(context));
        Assert.False(context.HasErrors);
    }

    [Fact]
    public void TypePropertiesKeepNormalCallsAndClonesWithAndWithoutContext()
    {
        var (_, info) = CreateTypeBindingFixture();
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        var self = new MinamoArray(new MinamoObject[] { MinamoInteger.One });

        foreach (var (name, isStatic, expected) in new[]
        {
            ("Size", false, 1L),
            ("Default", true, 42L),
            ("First", false, 1L),
            ("DefaultWithContext", true, 2L)
        })
        {
            var template = isStatic
                ? Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, name))
                : info.LookupInstanceMember(context, self, name)!;
            var callable = isStatic ? template : template.BindToInstance(context, self);
            var property = isStatic
                ? info.GetStaticMember(name, context)
                : info.GetInstanceMember(self, name, context);

            Assert.Equal(name, callable.FunctionName);
            Assert.Equal(expected, Assert.IsType<MinamoInteger>(property).Value);
            Assert.Equal(expected, Assert.IsType<MinamoInteger>(callable.Call(context)).Value);
            var clone = Assert.IsAssignableFrom<MinamoFunction>(callable.Clone());
            Assert.True(callable.Equals(clone));
            Assert.Equal(expected, Assert.IsType<MinamoInteger>(clone.Call(context)).Value);
        }

        Assert.False(context.HasErrors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FixedArityTypeCallsDoNotAllocateArgumentArrays(int arity)
    {
        var (_, info) = CreateTypeBindingFixture();
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        var function = Assert.IsAssignableFrom<MinamoFunction>(
            info.LookupStaticMember(context, arity == 1 ? "Echo" : "Second"));
        MinamoObject? result = null;
        for (var i = 0; i < 10_000; i++)
        {
            result = arity == 1
                ? function.Call(context, MinamoInteger.One)
                : function.Call(context, MinamoInteger.One, MinamoInteger.Two);
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            result = arity == 1
                ? function.Call(context, MinamoInteger.One)
                : function.Call(context, MinamoInteger.One, MinamoInteger.Two);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(0L, allocated);
        Assert.Same(arity == 1 ? MinamoInteger.One : MinamoInteger.Two, result);
        Assert.False(context.HasErrors);
    }

    [Fact]
    public void FixedArityTypeCallsKeepArgumentErrorsAndExistingContextErrors()
    {
        var (_, info) = CreateTypeBindingFixture();
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        var echo = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "Echo"));
        var second = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "Second"));
        Assert.Same(MinamoNil.Instance, echo.Call(context, MinamoInteger.One, MinamoInteger.Two));
        Assert.True(context.HasErrors);
        context.Error = null;
        Assert.Same(MinamoNil.Instance, second.Call(context, MinamoInteger.One));
        Assert.True(context.HasErrors);
        context.Error = null;

        var touch = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "Touch"));
        context.InvalidOperation();
        var error = context.Error;
        Assert.Same(MinamoNil.Instance, touch.Call(context, MinamoInteger.One));
        Assert.Same(MinamoNil.Instance, second.Call(context, MinamoInteger.One, MinamoInteger.Two));
        Assert.Same(error, context.Error);
        Assert.Equal(0, Assert.IsType<int>(info.GetType().GetProperty("Invocations")!.GetValue(null)));
    }

    [Fact]
    public void IntegerTypeConversionsRejectOverflowBeforeInvokingTheTarget()
    {
        var (_, info) = CreateTypeBindingFixture(BoundarySource, "GeneratorFixture.BoundaryTypeInfo");
        using var instance = new MinamoHost().CreateInstance();
        foreach (var name in new[] { "Integer", "NullableInteger", "Integers" })
        {
            foreach (var number in new[] { (long)int.MinValue, int.MaxValue, (long)int.MinValue - 1, (long)int.MaxValue + 1, 4294967296L })
            {
                var context = CreateTypeBindingContext(instance);
                var fn = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, name));
                var integer = new MinamoInteger(number);
                MinamoObject argument = name == "Integers" ? new MinamoArray(new MinamoObject[] { integer }) : integer;
                var result = fn.Call(context, argument);
                if (number >= int.MinValue && number <= int.MaxValue)
                {
                    Assert.Equal(number, Assert.IsType<MinamoInteger>(result).Value);
                    Assert.False(context.HasErrors);
                }
                else
                {
                    Assert.Same(MinamoNil.Instance, result);
                    Assert.True(context.HasErrors);
                }
            }
        }
        Assert.Equal(6, Assert.IsType<int>(info.GetType().GetProperty("Calls")!.GetValue(null)));
        var script = instance.Execute("""
            let xs = [10, 20]
            mut failed = false
            try { xs.RemoveAt(4294967296) } catch { failed = true }
            assert(true, failed)
            assert([10, 20], xs)
            """);
        Assert.True(script.Success, script.Failure?.Message);
    }

    [Fact]
    public void TypeDefaultsPreserveLargeIntegersQuotedCharactersAndSpecialFloats()
    {
        var (_, info) = CreateTypeBindingFixture(BoundarySource, "GeneratorFixture.BoundaryTypeInfo");
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        MinamoObject Call(string name) => Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, name)).Call(context);
        Assert.Equal(4294967296L, Assert.IsType<MinamoInteger>(Call("Large")).Value);
        Assert.Equal(long.MinValue, Assert.IsType<MinamoInteger>(Call("Minimum")).Value);
        Assert.Equal('\'', Assert.IsType<MinamoChar>(Call("Quote")).Value);
        Assert.True(double.IsNaN(Assert.IsType<MinamoFloat>(Call("NotANumber")).Value));
        Assert.Equal(double.PositiveInfinity, Assert.IsType<MinamoFloat>(Call("Infinity")).Value);
        Assert.Equal(double.NegativeInfinity, Assert.IsType<MinamoFloat>(Call("SingleInfinity")).Value);
        Assert.False(context.HasErrors);
    }

    [Fact]
    public void NullableObjectAndArrayReturnsBecomeLanguageNil()
    {
        var (_, info) = CreateTypeBindingFixture(BoundarySource, "GeneratorFixture.BoundaryTypeInfo");
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        foreach (var name in new[] { "MissingObject", "MissingBase", "MissingArray", "MissingObjects" })
        {
            var fn = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, name));
            Assert.Same(MinamoNil.Instance, fn.Call(context));
        }
        var present = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, "PresentArray"));
        Assert.Equal(1, Assert.IsType<MinamoArray>(present.Call(context)).Count);
        Assert.False(context.HasErrors);
    }

    [Fact]
    public void GeneratedCommandsAcceptSpecialFloatingPointDefaults()
    {
        const string source = """
            using Minamo.Hosting;
            namespace GeneratorFixture;
            [MinamoModule("numbers")]
            public static class NumbersCommands
            {
                [MinamoCommand] public static bool Nan(double value = double.NaN) => double.IsNaN(value);
                [MinamoCommand] public static bool Infinity(float value = float.PositiveInfinity) => float.IsPositiveInfinity(value);
                [MinamoCommand] public static bool Negative(double value = double.NegativeInfinity) => double.IsNegativeInfinity(value);
            }
            """;
        using var instance = CreateInstance(Generate(source));
        var result = instance.Execute("import numbers\nassert(true, numbers.Nan())\nassert(true, numbers.Infinity())\nassert(true, numbers.Negative())");
        Assert.True(result.Success, result.Failure?.Message);
    }

    [Fact]
    public void TypeBindingsKeepDistinctNamespacesPartialDeclarationsAndEscapedNames()
    {
        const string source = """
            using Minamo.Codegen;
            using Minamo.Runtime;
            using Minamo.Runtime.Types;
            namespace First.Part
            {
                [MinamoType]
                public sealed partial class SharedTypeInfo : MinamoForeignTypeInfo
                {
                    public override string ReflectedTypeName => "First";
                    [MinamoStaticMethod] public static int @class(int @event) => @event;
                    [MinamoStaticMethod("a-b")] public static int Dash(int __ret) => __ret;
                    [MinamoStaticMethod("a_002Db")] public static int Underscore() => 2;
                    [MinamoStaticMethod("quoted\"name")] public static int Quote() => 3;
                }
                [System.Obsolete]
                public sealed partial class SharedTypeInfo { }
                [MinamoType]
                public sealed partial class @namespace : MinamoForeignTypeInfo
                {
                    public override string ReflectedTypeName => "Keyword";
                    [MinamoStaticMethod] public static int Value() => 4;
                }
            }
            namespace Second.Part
            {
                [MinamoType]
                public sealed partial class SharedTypeInfo : MinamoForeignTypeInfo
                {
                    public override string ReflectedTypeName => "Second";
                    [MinamoStaticMethod] public static int Value() => 5;
                }
            }
            """;
        Assert.Equal(Generate(source).GeneratedSource, Generate(source).GeneratedSource);
        var (assembly, info) = CreateTypeBindingFixture(source, "First.Part.SharedTypeInfo");
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        foreach (var name in new[] { "class", "a-b" })
        {
            var fn = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, name));
            Assert.Equal(name, fn.FunctionName);
            Assert.Equal(1, Assert.IsType<MinamoInteger>(fn.Call(context, MinamoInteger.One)).Value);
        }
        foreach (var (name, expected) in new[] { ("a_002Db", 2L), ("quoted\"name", 3L) })
        {
            var fn = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, name));
            Assert.Equal(name, fn.FunctionName);
            Assert.Equal(expected, Assert.IsType<MinamoInteger>(fn.Call(context)).Value);
        }
        foreach (var (typeName, expected) in new[] { ("First.Part.namespace", 4L), ("Second.Part.SharedTypeInfo", 5L) })
        {
            var other = (MinamoTypeInfo)Activator.CreateInstance(assembly.GetType(typeName)!)!;
            var fn = Assert.IsAssignableFrom<MinamoFunction>(other.LookupStaticMember(context, "Value"));
            Assert.Equal(expected, Assert.IsType<MinamoInteger>(fn.Call(context)).Value);
        }
        Assert.False(context.HasErrors);
    }

    [Fact]
    public void PartialCommandModulesAreGeneratedOnce()
    {
        const string source = """
            using Minamo.Hosting;
            namespace GeneratorFixture;
            [MinamoModule("partialmodule")]
            public static partial class PartialCommands
            {
                [MinamoCommand] public static int @class(int @event) => @event;
            }
            [System.Obsolete]
            public static partial class PartialCommands { }
            """;
        using var instance = CreateInstance(Generate(source));
        var result = instance.Execute("import partialmodule\npartialmodule.class(42)");
        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(42L, result.GetValue<long>());
    }

    [Fact]
    public void TypeBindingsSupportPartialDeclarationsInDifferentFiles()
    {
        const string first = "namespace GeneratorFixture; [System.Obsolete] public sealed partial class SplitTypeInfo { }";
        const string second = """
            using Minamo.Codegen;
            using Minamo.Runtime.Types;
            namespace GeneratorFixture;
            [MinamoType]
            public sealed partial class SplitTypeInfo : MinamoForeignTypeInfo
            {
                public override string ReflectedTypeName => "Split";
                [MinamoStaticMethod] public static int Echo(int value) => value;
            }
            """;
        var generated = Generate(first, additionalSources: new[] { second });
        AssertNoErrors(generated.Diagnostics);
        AssertNoErrors(generated.Compilation.GetDiagnostics());
        using var stream = new MemoryStream();
        Assert.True(generated.Compilation.Emit(stream).Success);
    }

    [Fact]
    public void RebindingAllWrapperAritiesRecreatesTheCorrectFunction()
    {
        var (_, info) = CreateTypeBindingFixture();
        using var instance = new MinamoHost().CreateInstance();
        var context = CreateTypeBindingContext(instance);
        foreach (var (name, arguments, expected) in new[]
        {
            ("Zero", Array.Empty<MinamoObject>(), 1L),
            ("Echo", new MinamoObject[] { MinamoInteger.Two }, 2L),
            ("Second", new MinamoObject[] { MinamoInteger.One, MinamoInteger.Two }, 2L),
            ("Third", new MinamoObject[] { MinamoInteger.One, MinamoInteger.Two, MinamoInteger.Three }, 3L),
            ("Optional", Array.Empty<MinamoObject>(), 7L),
            ("Default", Array.Empty<MinamoObject>(), 42L)
        })
        {
            var original = Assert.IsAssignableFrom<MinamoFunction>(info.LookupStaticMember(context, name));
            var rebound = original.BindToInstance(context, MinamoInteger.One);
            Assert.Equal(name, rebound.FunctionName);
            Assert.Equal(original.GetType(), rebound.GetType());
            if (original.Parameters.Length > 0)
            {
                Assert.NotSame(original.Parameters, rebound.Parameters);
            }
            Assert.Equal(expected, Assert.IsType<MinamoInteger>(rebound.Call(context, arguments)).Value);
            var clone = Assert.IsAssignableFrom<MinamoFunction>(rebound.Clone());
            Assert.True(rebound.Equals(clone));
            Assert.Equal(expected, Assert.IsType<MinamoInteger>(clone.Call(context, arguments)).Value);
        }
        Assert.False(context.HasErrors);
    }

    private const string BoundarySource = """
        #nullable enable
        using Minamo.Codegen;
        using Minamo.Runtime;
        using Minamo.Runtime.Types;
        namespace GeneratorFixture;
        [MinamoType]
        public sealed partial class BoundaryTypeInfo : MinamoForeignTypeInfo
        {
            public override string ReflectedTypeName => "BoundaryFixture";
            public static int Calls { get; private set; }
            [MinamoStaticMethod] public static int Integer(int value) { Calls++; return value; }
            [MinamoStaticMethod] public static int? NullableInteger(int? value = null) { Calls++; return value; }
            [MinamoStaticMethod] public static int Integers(int[] values) { Calls++; return values[0]; }
            [MinamoStaticMethod] public static long Large(long value = 4294967296L) => value;
            [MinamoStaticMethod] public static long Minimum(long value = long.MinValue) => value;
            [MinamoStaticMethod] public static char Quote(char value = '\'') => value;
            [MinamoStaticMethod] public static double NotANumber(double value = double.NaN) => value;
            [MinamoStaticMethod] public static double Infinity(double value = double.PositiveInfinity) => value;
            [MinamoStaticMethod] public static float SingleInfinity(float value = float.NegativeInfinity) => value;
            [MinamoStaticMethod] public static MinamoInteger? MissingObject() => null;
            [MinamoStaticMethod] public static MinamoObject? MissingBase() => null;
            [MinamoStaticMethod] public static int[]? MissingArray() => null;
            [MinamoStaticMethod] public static MinamoInteger[]? MissingObjects() => null;
            [MinamoStaticMethod] public static int[] PresentArray() => new[] { 1 };
        }
        """;

    private const string TypeBindingSource = """
        using Minamo.Codegen;
        using Minamo.Runtime;
        using Minamo.Runtime.Types;
        using ExecutionContext = Minamo.Runtime.ExecutionContext;

        namespace GeneratorFixture;

        [MinamoType]
        public sealed partial class SharedTypeInfo : MinamoForeignTypeInfo
        {
            public override string ReflectedTypeName => "SharedFixture";
            public static int Invocations { get; private set; }

            [MinamoStaticMethod]
            public static MinamoObject Zero() => MinamoInteger.One;
            [MinamoStaticMethod]
            public static MinamoObject Echo(MinamoObject value) => value;
            [MinamoStaticMethod]
            public static MinamoObject EchoAgain(MinamoObject value) => value;
            [MinamoStaticMethod]
            public static MinamoObject ContextEcho(ExecutionContext context, MinamoObject value) =>
                context.HasErrors ? MinamoNil.Instance : value;
            [MinamoStaticMethod]
            public static MinamoObject Second(MinamoObject first, MinamoObject second) => second;
            [MinamoStaticMethod]
            public static MinamoObject Third(MinamoObject first, MinamoObject second, MinamoObject third) => third;
            [MinamoStaticMethod]
            public static int Optional(int value = 7) => value;
            [MinamoStaticMethod]
            public static int AttributeDefault([Default(9)] int value) => value;
            [MinamoStaticMethod]
            public static int? Nullable([Default] int? value) => value;
            [MinamoStaticMethod]
            public static int Count([VarArg] MinamoTuple values) => values.Count;
            [MinamoStaticMethod]
            public static int ArrayCount(params MinamoObject[] values) => values.Length;
            [MinamoStaticProperty("Default")]
            public static int Default() => 42;
            [MinamoStaticProperty("DefaultWithContext")]
            public static MinamoObject ContextDefault(ExecutionContext context) =>
                context.HasErrors ? MinamoNil.Instance : MinamoInteger.Two;
            [MinamoStaticMethod("Set")]
            public static MinamoObject StaticSet(MinamoObject value) => value;
            [MinamoStaticMethod]
            public static MinamoObject Touch(MinamoObject value)
            {
                Invocations++;
                return value;
            }
            [MinamoMethod("Set")]
            public static MinamoObject Set(MinamoArray self, MinamoObject value)
            {
                self[0] = value;
                return value;
            }
            [MinamoMethod("Get")]
            public static MinamoObject Get(MinamoArray self) => self[0];
            [MinamoProperty("Size")]
            public static int Size(MinamoArray self) => self.Count;
            [MinamoProperty("First")]
            public static MinamoObject ContextFirst(ExecutionContext context, MinamoArray self) =>
                context.HasErrors ? MinamoNil.Instance : self[0];
        }

        [MinamoType]
        public sealed partial class OtherTypeInfo : MinamoForeignTypeInfo
        {
            public override string ReflectedTypeName => "OtherFixture";
            [MinamoStaticMethod]
            public static MinamoObject Echo(MinamoObject value) => value;
        }
        """;

    private static (Assembly Assembly, MinamoTypeInfo Info) CreateTypeBindingFixture(
        string source = TypeBindingSource,
        string typeName = "GeneratorFixture.SharedTypeInfo")
    {
        var generated = Generate(source, "Minamo.TypeBindingFixture." + Guid.NewGuid().ToString("N"));
        AssertNoErrors(generated.Diagnostics);
        AssertNoErrors(generated.Compilation.GetDiagnostics());
        using var stream = new MemoryStream();
        var emit = generated.Compilation.Emit(stream);
        AssertNoErrors(emit.Diagnostics);
        Assert.True(emit.Success);
        var assembly = Assembly.Load(stream.ToArray());
        var info = (MinamoTypeInfo)Activator.CreateInstance(
            assembly.GetType(typeName)!)!;
        return (assembly, info);
    }

    private static ExecutionContext CreateTypeBindingContext(MinamoInstance instance)
    {
        Assert.True(instance.Execute("nil").Success);
        return MinamoMachine.CreateExecutionContext(instance.RuntimeContext!);
    }

    private static GeneratorResult Generate(
        string source,
        string assemblyName = "Minamo.GeneratorFixture",
        string[]? additionalSources = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            parseOptions,
            path: "GeneratorFixture.cs");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree }.Concat((additionalSources ?? Array.Empty<string>()).Select((part, index) =>
                CSharpSyntaxTree.ParseText(part, parseOptions, path: $"GeneratorFixture.Part{index}.cs"))),
            References(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[]
            {
                new MinamoCommandGenerator().AsSourceGenerator(),
                new MinamoTypeGenerator().AsSourceGenerator()
            },
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics);
        Assert.All(driver.GetRunResult().Results, result => Assert.Null(result.Exception));
        var generatedSources = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .OrderBy(result => result.HintName, StringComparer.Ordinal)
            .Select(result => result.SourceText.ToString())
            .ToArray();

        return new(
            outputCompilation,
            diagnostics,
            string.Join("\n", generatedSources));
    }

    private static MinamoInstance CreateInstance(GeneratorResult generated, object? hostContext = null)
    {
        AssertNoErrors(generated.Diagnostics);
        AssertNoErrors(generated.Compilation.GetDiagnostics());

        using var stream = new MemoryStream();
        var emit = generated.Compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        var assembly = Assembly.Load(stream.ToArray());
        var extensionType = assembly.GetTypes().Single(type =>
            type.IsAbstract
            && type.IsSealed
            && type.Name.EndsWith("HostingExtensions", StringComparison.Ordinal));
        var addCommands = extensionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name.StartsWith("Add", StringComparison.Ordinal));
        var host = (MinamoHost)addCommands.Invoke(null, new object[] { new MinamoHost() })!;
        return host.CreateInstance(hostContext);
    }

    private static IReadOnlyList<Diagnostic> Errors(GeneratorResult result) =>
        result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .OrderBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<MetadataReference> References()
    {
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var paths = trustedAssemblies.Split(Path.PathSeparator)
            .Append(typeof(MinamoHost).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return paths.Select(path => MetadataReference.CreateFromFile(path));
    }

    private static void AssertDiagnostic(
        IEnumerable<Diagnostic> diagnostics,
        string id,
        string sourceText,
        string message)
    {
        var diagnostic = Assert.Single(diagnostics, item => item.Id == id);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Equal("GeneratorFixture.cs", diagnostic.Location.SourceTree?.FilePath);
        Assert.Equal(
            sourceText,
            diagnostic.Location.SourceTree?.GetText()
                .GetSubText(diagnostic.Location.SourceSpan)
                .ToString());
        Assert.Equal(message, diagnostic.GetMessage());
        Assert.True(diagnostic.Location.GetLineSpan().StartLinePosition.Line >= 0);
    }

    private static void AssertNoErrors(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
    }

    private static void AssertSnapshot(string name, string actual)
    {
        var path = Path.GetFullPath(Path.Combine(
            TestRepository.Root,
            "Minamo.UnitTests",
            "Generators",
            "Snapshots",
            name));
        actual = Normalize(actual);

        if (string.Equals(
            Environment.GetEnvironmentVariable("MINAMO_UPDATE_SNAPSHOTS"),
            "1",
            StringComparison.Ordinal))
        {
            File.WriteAllText(path, actual);
        }

        Assert.Equal(Normalize(File.ReadAllText(path)), actual);
    }

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";

    private sealed record GeneratorResult(
        Compilation Compilation,
        IEnumerable<Diagnostic> Diagnostics,
        string GeneratedSource);
}
