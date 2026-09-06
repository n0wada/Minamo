using Minamo.Compiler;
using Minamo.Hosting;
using Minamo.Linker;
using Minamo.Parser;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Reflection;
using System.Text;
using Xunit;

namespace Minamo.UnitTesting.Security;

[Trait("Category", "Security")]
[Trait("Suite", "Security")]
public sealed class SecurityRegressionTests
{
    [Fact]
    public void MultiParameterOverloadRequiresEveryParameterToMatch()
    {
        var parameters = typeof(SecurityRegressionTests)
            .GetMethod(nameof(TwoParameters), BindingFlags.NonPublic | BindingFlags.Static)!
            .GetParameters();

        Assert.True(MinamoInteropTypeInfo.ParametersMatch(
            parameters,
            new MinamoObject[] { new MinamoInterop(typeof(string)), new MinamoInterop(typeof(int)) }));
        Assert.False(MinamoInteropTypeInfo.ParametersMatch(
            parameters,
            new MinamoObject[] { new MinamoInterop(typeof(string)), new MinamoInterop(typeof(string)) }));
    }

    [Fact]
    public void NumericConversionEnforcesSignedAndUnsignedBounds()
    {
        Assert.True(TypeConverter.TryConvert(new MinamoInteger(int.MinValue), typeof(int), out _));
        Assert.True(TypeConverter.TryConvert(new MinamoInteger(int.MaxValue), typeof(int), out _));
        Assert.False(TypeConverter.TryConvert(
            new MinamoInteger((long)int.MinValue - 1),
            typeof(int),
            out _));
        Assert.False(TypeConverter.TryConvert(
            new MinamoInteger((long)int.MaxValue + 1),
            typeof(int),
            out _));
        Assert.True(TypeConverter.TryConvert(new MinamoInteger(uint.MaxValue), typeof(uint), out _));
        Assert.False(TypeConverter.TryConvert(MinamoInteger.MinusOne, typeof(uint), out _));
    }

    [Fact]
    public void ModuleLookupRejectsParentTraversal()
    {
        using var paths = new TemporaryLookupPaths();
        var lookup = paths.CreateLookup();

        Assert.False(lookup.Find(null, paths.OutsideFile, out _));
        Assert.False(lookup.Find(
            null,
            Path.Combine("..", Path.GetFileName(paths.OutsideFile)),
            out _));
        Assert.False(lookup.Find(
            null,
            Path.Combine("nested", "..", "..", Path.GetFileName(paths.OutsideFile)),
            out _));
    }

    [Fact]
    public void ModuleLookupRejectsSymbolicLinkEscapeWhenSupported()
    {
        using var paths = new TemporaryLookupPaths();
        var link = Path.Combine(paths.Root, "linked");

        try
        {
            Directory.CreateSymbolicLink(link, paths.OutsideDirectory);
        }
        catch (Exception ex) when (ex is IOException
            or PlatformNotSupportedException
            or UnauthorizedAccessException)
        {
            return;
        }

        var lookup = paths.CreateLookup();
        Assert.False(lookup.Find(null, Path.Combine("linked", "linked.nami"), out _));
    }

    [Fact]
    public void ModuleLookupRejectsSymbolicFileEscapeWhenSupported()
    {
        using var paths = new TemporaryLookupPaths();
        var link = Path.Combine(paths.Root, "linked.nami");

        try
        {
            File.CreateSymbolicLink(link, paths.OutsideFile);
        }
        catch (Exception ex) when (ex is IOException
            or PlatformNotSupportedException
            or UnauthorizedAccessException)
        {
            return;
        }

        var lookup = paths.CreateLookup();
        Assert.False(lookup.Find(null, "linked.nami", out _));
    }

    [Fact]
    public void LookupFactoriesHaveExplicitSearchScopes()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "minamo-lookup-scope-" + Guid.NewGuid().ToString("N"));
        var module = "scope.nami";
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, module), "let value = 42", Encoding.UTF8);
            var options = BuilderOptions.Default();

            var restricted = FileLookup.Restricted(options).Build();
            var explicitlyAllowed = FileLookup.Restricted(options).AddPath(root).Build();
            var standard = FileLookup.Standard(options).Build();

            Assert.False(restricted.Find(root, module, out _));
            Assert.True(explicitlyAllowed.Find(null, module, out _));
            Assert.True(standard.Find(root, module, out _));
            Assert.DoesNotContain(
                typeof(FileLookup.FileLookupBuilder).GetMethods(),
                method => method.Name == "UseExecutablePaths");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationAndTimeLimitStopCooperativeHostCommands()
    {
        var timeProvider = new ManualTimeProvider();
        using var commandStarted = new ManualResetEventSlim();
        using var session = new MinamoHost(new()
        {
            Limits = new()
            {
                MaxExecutionTime = TimeSpan.FromMilliseconds(30),
                TimeProvider = timeProvider
            }
        })
            .Module("limit", module => module.Command("Wait", context =>
            {
                commandStarted.Set();
                context.CancellationToken.WaitHandle.WaitOne();
                context.CancellationToken.ThrowIfCancellationRequested();
                return null;
            }))
            .CreateInstance();

        var execution = Task.Run(() => session.Execute("import limit\nlimit.Wait()"));
        Assert.True(commandStarted.Wait(TimeSpan.FromSeconds(5)));
        timeProvider.Advance(TimeSpan.FromMilliseconds(31));
        var timedOut = await execution;

        Assert.False(timedOut.Success);
        Assert.Equal(MinamoFailureKind.Limit, timedOut.Failure?.Kind);
        Assert.Equal(MinamoExecutionLimitKind.Time, timedOut.Failure?.Limit);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = session.Execute("1", cancellation.Token);

        Assert.False(cancelled.Success);
        Assert.Equal(MinamoFailureKind.Cancelled, cancelled.Failure?.Kind);
    }

    [Fact]
    public void HostExceptionDetailsAreHiddenFromScripts()
    {
        var logs = new List<MinamoLogEntry>();
        using var session = new MinamoHost(new()
        {
            Log = logs.Add
        })
            .Module("broken", module => module.Command(
                "Fail",
                _ => throw new InvalidOperationException("sensitive host detail")))
            .CreateInstance();

        var result = session.Execute("import broken\nbroken.Fail()");

        Assert.False(result.Success);
        Assert.DoesNotContain("sensitive host detail", result.Failure?.Message ?? string.Empty);
        Assert.Contains(logs, log => log.Level == MinamoLogLevel.Error
            && Equals(log.Properties["exceptionMessage"], "sensitive host detail"));
    }

    [Fact]
    public void ResourceHandlesCannotCrossSessionBoundaries()
    {
        using var owner = new MinamoHost()
            .AddResourceType<ProtectedResource>()
            .CreateInstance();
        var handle = owner.Environment.CreateResource(new ProtectedResource());
        using var other = new MinamoHost()
            .Module("foreign", module => module.Command<MinamoObject>("Get", _ => handle))
            .CreateInstance();

        var result = other.Execute("import foreign\nforeign.Get().IsValid()");

        Assert.False(result.Success);
        Assert.Equal(MinamoFailureKind.Runtime, result.Failure?.Kind);
    }

    [Fact]
    public void InvalidOpcodeIsRejectedExplicitly()
    {
        var unit = new Unit();
        unit.Layouts.Add(new MemoryLayout(0, 1, 0));
        unit.Ops.Add(new Op((OpCode)int.MaxValue));
        var units = new FastList<Unit>();
        units.Add(unit);
        var context = MinamoMachine.CreateExecutionContext(new UnitComposition(units));

        var error = Assert.Throws<InvalidOperationException>(() => MinamoMachine.Execute(context));

        Assert.Contains("Unknown opcode value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LargeSourceInputParsesWithoutUnboundedRecursion()
    {
        const int payloadSize = 1_000_000;
        var source = "let payload = \"" + new string('a', payloadSize) + "\"\npayload.Length()";

        var result = MinamoParser.Parse(SourceBuffer.FromString(source, "<large-input>"));

        Assert.True(result.Success);
    }

    private static void TwoParameters(string text, int number) { }

    [MinamoResource("ProtectedResource")]
    private sealed class ProtectedResource : MinamoResource { }

    private sealed class TemporaryLookupPaths : IDisposable
    {
        public TemporaryLookupPaths()
        {
            Root = Path.Combine(Path.GetTempPath(), "minamo-security-" + Guid.NewGuid());
            OutsideDirectory = Path.Combine(
                Path.GetTempPath(),
                "minamo-security-outside-" + Guid.NewGuid());
            OutsideFile = Path.Combine(
                Path.GetTempPath(),
                "minamo-security-outside-" + Guid.NewGuid() + ".nami");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(OutsideDirectory);
            File.WriteAllText(OutsideFile, "func value() => 99", Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(OutsideDirectory, "linked.nami"),
                "func value() => 100",
                Encoding.UTF8);
        }

        public string Root { get; }
        public string OutsideDirectory { get; }
        public string OutsideFile { get; }

        public FileLookup CreateLookup()
        {
            var options = BuilderOptions.Default();
            return FileLookup.Restricted(options)
                .AddStartupPath(Root)
                .Build();
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            if (Directory.Exists(OutsideDirectory))
            {
                Directory.Delete(OutsideDirectory, recursive: true);
            }

            if (File.Exists(OutsideFile))
            {
                File.Delete(OutsideFile);
            }
        }
    }
}
