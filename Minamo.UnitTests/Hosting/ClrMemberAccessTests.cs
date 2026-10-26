using Minamo.Hosting;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using Xunit;

namespace Minamo.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
[Trait("Category", "Security")]
public sealed class ClrMemberAccessTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public void AllowsClrMembersByDefaultAndWhenExplicitlyEnabled(bool? allow)
    {
        var value = new ClrValue();
        var options = allow.HasValue
            ? new MinamoHostOptions { AllowClrMemberAccess = allow.Value }
            : null;
        using var instance = new MinamoHost(options).CreateInstance(
            new MinamoEnvironment().Expose("value", value).Expose("clrType", typeof(ClrValue)));

        var result = instance.Execute("""
            assert("value", value.Read())
            assert("value", value.Name)
            assert("static", value.GetType().StaticRead())
            assert("static", clrType.StaticRead())
            """);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(2, value.Calls);
    }

    [Theory]
    [InlineData(null, "assert(\"static\", value.GetType().StaticRead())")]
    [InlineData(true, "assert(\"static\", value.GetType().StaticRead())")]
    [InlineData(false, "value.GetType()")]
    [InlineData(true, "assert(true, isCallable(value.GetType))")]
    [InlineData(true, "assert(true, clrType.GetType(\"System.String\").IsNullOrEmpty(\"\"))")]
    [InlineData(true, "assert(true, assembly.GetType(\"System.String\").IsNullOrEmpty(\"\"))")]
    [InlineData(false, "clrType.GetType(\"System.String\")")]
    [InlineData(false, "assembly.GetType(\"System.String\")")]
    public void AppliesClrMemberAccessOptionToGetType(bool? allow, string source)
    {
        var options = allow.HasValue
            ? new MinamoHostOptions { AllowClrMemberAccess = allow.Value }
            : null;
        using var instance = new MinamoHost(options).CreateInstance(
            new MinamoEnvironment()
                .Expose("value", new ClrValue())
                .Expose("clrType", typeof(Type))
                .Expose("assembly", typeof(string).Assembly));

        var result = instance.Execute(source);

        if (allow == false)
        {
            AssertDenied(result);
        }
        else
        {
            Assert.True(result.Success, result.Failure?.Message);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AppliesClrPolicyToPreviouslyObtainedGetTypeCallables(bool allow)
    {
        using var source = new MinamoHost().CreateInstance();
        Assert.True(source.Execute("nil").Success);
        var context = MinamoMachine.CreateExecutionContext(source.RuntimeContext!);
        var callable = new MinamoInteropFunction(
            "SavedMethod",
            typeof(ClrValue),
            new() { typeof(object).GetMethod(nameof(object.GetType))! },
            auto: false).BindToInstance(context, new MinamoInterop(typeof(ClrValue), new ClrValue()));
        using var target = new MinamoHost(new() { AllowClrMemberAccess = allow }).CreateInstance(
            new MinamoEnvironment().Expose("saved", callable));

        var result = target.Execute("assert(\"static\", saved().StaticRead())");

        if (allow)
        {
            Assert.True(result.Success, result.Failure?.Message);
        }
        else
        {
            AssertDenied(result);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreservesNativeTypeInformationAndExplicitGetTypeCommands(bool allow)
    {
        var calls = 0;
        var host = new MinamoHost(new() { AllowClrMemberAccess = allow })
            .Module("approved", module => module.Command("GetType", _ =>
            {
                calls++;
                return new ClrValue().GetType().Name;
            }));
        using var instance = host.CreateInstance();

        var result = instance.Execute("""
            import approved
            assert("ClrValue", approved.GetType())
            assert(String, "text".GetType())
            let number = 42
            assert(Integer, number.GetType())
            struct LocalValue { value }
            assert(LocalValue, LocalValue(1).GetType())
            """);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("value.Read()")]
    [InlineData("value.Name")]
    [InlineData("value.GetType().StaticRead()")]
    [InlineData("clrType.StaticRead()")]
    public void RejectsAutomaticClrMembersWhenDisabled(string source)
    {
        var value = new ClrValue();
        using var instance = RestrictedHost().CreateInstance(
            new MinamoEnvironment().Expose("value", value).Expose("clrType", typeof(ClrValue)));

        var result = instance.Execute(source);

        AssertDenied(result);
        Assert.Equal(0, value.Calls);
    }

    [Theory]
    [InlineData(MinamoCapabilityMode.Automatic)]
    [InlineData(MinamoCapabilityMode.Restricted)]
    [InlineData(MinamoCapabilityMode.Unrestricted)]
    public void AppliesClrPolicyIndependentlyOfCapabilities(MinamoCapabilityMode mode)
    {
        using var instance = new MinamoHost(new()
        {
            AllowClrMemberAccess = false,
            CapabilityMode = mode
        })
            .AddCapabilities("*")
            .CreateInstance(new MinamoEnvironment().Expose("value", new ClrValue()));

        AssertDenied(instance.Execute("value.Read()"));
    }

    [Theory]
    [InlineData("item.Read()")]
    [InlineData("host.Registry[\"item\"].Read()")]
    [InlineData("host.Input().Read()")]
    public async Task RestrictsClrValuesFromEnvironmentRegistryAndInput(string source)
    {
        var value = new ClrValue();
        using var instance = RestrictedHost().CreateInstance(
            new MinamoEnvironment()
                .Expose("item", value)
                .UseInputAsync(_ => ValueTask.FromResult(value)));
        instance.Environment.Registry.Set("item", value);

        AssertDenied(await instance.ExecuteAsync(source));
        Assert.Equal(0, value.Calls);
    }

    [Fact]
    public void KeepsRegisteredCommandsResourcesAndNativeValuesAvailable()
    {
        var value = new ClrValue();
        var host = RestrictedHost()
            .AddResourceType<RegisteredResource>()
            .Module("approved", module =>
            {
                module.Command<ClrValue>("Get", _ => value);
                module.Command("Read", context => context.Argument<ClrValue>("value").Read(),
                    MinamoCommandParameter.Required<ClrValue>("value"));
                module.Command("Open", context => context.Resource(new RegisteredResource()));
            });
        var program = host.Compile("""
            import approved
            let opaque = approved.Get()
            assert("value", approved.Read(opaque))
            assert(42, approved.Open().Read())
            assert("String", typeName("x".GetType()))
            assert(2, [1, 2].Length())
            """).GetValueOrThrow();
        using var instance = host.CreateInstance(program);

        var result = instance.Execute();

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal(1, value.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsClrCallablesObtainedFromAnUnrestrictedInstance(bool constructor)
    {
        var value = new ClrValue();
        using var allowed = new MinamoHost().CreateInstance();
        Assert.True(allowed.Execute("nil").Success);
        var context = MinamoMachine.CreateExecutionContext(allowed.RuntimeContext!);
        var interopType = (MinamoInteropTypeInfo)context.RuntimeContext.Types[MinamoTypeCodes.Interop];
        var self = constructor
            ? new MinamoInterop(typeof(ClrValue))
            : new MinamoInterop(typeof(ClrValue), value);
        var callable = interopType.GetInstanceMember(self, constructor ? "new" : "Read", context);
        Assert.False(context.HasErrors);
        using var restricted = RestrictedHost().CreateInstance(
            new MinamoEnvironment().Expose("callable", callable));

        AssertDenied(restricted.Execute("callable()"));
        Assert.Equal(0, value.Calls);
    }

    [Fact]
    public void RetainsClrPolicyAfterReset()
    {
        var value = new ClrValue();
        using var instance = RestrictedHost().CreateInstance(
            new MinamoEnvironment().Expose("value", value));

        AssertDenied(instance.Execute("value.Read()"));
        instance.Reset();
        AssertDenied(instance.Execute("value.Read()"));
        Assert.Equal(0, value.Calls);
    }

    [Fact]
    public async Task RestrictsClrValuesPassedToSelectActions()
    {
        var value = new ClrValue();
        using var instance = RestrictedHost().CreateInstance();
        var initialized = await instance.ExecuteAsync("""
            select screen {
                action "inspect" (item) => { item.Read() }
            }
            """);
        Assert.True(initialized.Success, initialized.Failure?.Message);
        using var select = await instance.OpenSelectAsync("screen");

        var error = await Assert.ThrowsAnyAsync<Exception>(() => select.SelectAsync("inspect", value));

        Assert.Contains("Automatic CLR member access is disabled", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, value.Calls);
    }

    private static MinamoHost RestrictedHost() => new(new() { AllowClrMemberAccess = false });

    private static void AssertDenied(MinamoExecutionResult result)
    {
        Assert.False(result.Success);
        Assert.Equal(MinamoFailureKind.Runtime, result.Failure?.Kind);
        Assert.Contains("Automatic CLR member access is disabled", result.Failure?.Message, StringComparison.Ordinal);
    }

    private sealed class ClrValue
    {
        public ClrValue() { }

        public int Calls { get; private set; }
        public string Name => Read();
        public string Read()
        {
            Calls++;
            return "value";
        }

        public static string StaticRead() => "static";
    }

    [MinamoResource("RegisteredResource")]
    private sealed class RegisteredResource : MinamoResource
    {
        [MinamoCommand]
        public int Read() => 42;
    }
}
