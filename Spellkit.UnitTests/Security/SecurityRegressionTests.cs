using Spellkit.Compiler;
using Spellkit.Hosting;
using Spellkit.Linker;
using Spellkit.Parser;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Reflection;
using System.Text;
using Xunit;

namespace Spellkit.UnitTesting.Security;

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

        Assert.True(SpkInteropTypeInfo.ParametersMatch(
            parameters,
            new SpkObject[] { new SpkInterop(typeof(string)), new SpkInterop(typeof(int)) }));
        Assert.False(SpkInteropTypeInfo.ParametersMatch(
            parameters,
            new SpkObject[] { new SpkInterop(typeof(string)), new SpkInterop(typeof(string)) }));
    }

    [Fact]
    public void NumericConversionEnforcesSignedAndUnsignedBounds()
    {
        Assert.True(TypeConverter.TryConvert(new SpkInteger(int.MinValue), typeof(int), out _));
        Assert.True(TypeConverter.TryConvert(new SpkInteger(int.MaxValue), typeof(int), out _));
        Assert.False(TypeConverter.TryConvert(
            new SpkInteger((long)int.MinValue - 1),
            typeof(int),
            out _));
        Assert.False(TypeConverter.TryConvert(
            new SpkInteger((long)int.MaxValue + 1),
            typeof(int),
            out _));
        Assert.True(TypeConverter.TryConvert(new SpkInteger(uint.MaxValue), typeof(uint), out _));
        Assert.False(TypeConverter.TryConvert(SpkInteger.MinusOne, typeof(uint), out _));
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
        Assert.False(lookup.Find(null, Path.Combine("linked", "linked.kit"), out _));
    }

    [Fact]
    public void ModuleLookupRejectsSymbolicFileEscapeWhenSupported()
    {
        using var paths = new TemporaryLookupPaths();
        var link = Path.Combine(paths.Root, "linked.kit");

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
        Assert.False(lookup.Find(null, "linked.kit", out _));
    }

    [Fact]
    public void LookupFactoriesHaveExplicitSearchScopes()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "spellkit-lookup-scope-" + Guid.NewGuid().ToString("N"));
        var module = "scope.kit";
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
        using var session = new SpellkitHost(new()
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
        Assert.Equal(SpellkitFailureKind.Limit, timedOut.Failure?.Kind);
        Assert.Equal(SpkExecutionLimitKind.Time, timedOut.Failure?.Limit);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = session.Execute("1", cancellation.Token);

        Assert.False(cancelled.Success);
        Assert.Equal(SpellkitFailureKind.Cancelled, cancelled.Failure?.Kind);
    }

    [Fact]
    public void HostExceptionDetailsAreHiddenFromScripts()
    {
        var logs = new List<SpellkitLogEntry>();
        using var session = new SpellkitHost(new()
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
        Assert.Contains(logs, log => log.Level == SpellkitLogLevel.Error
            && Equals(log.Properties["exceptionMessage"], "sensitive host detail"));
    }

    [Fact]
    public void ResourceHandlesCannotCrossSessionBoundaries()
    {
        using var owner = new SpellkitHost()
            .AddResourceType<ProtectedResource>()
            .CreateInstance();
        var handle = owner.Environment.CreateResource(new ProtectedResource());
        using var other = new SpellkitHost()
            .Module("foreign", module => module.Command<SpkObject>("Get", _ => handle))
            .CreateInstance();

        var result = other.Execute("import foreign\nforeign.Get().IsValid()");

        Assert.False(result.Success);
        Assert.Equal(SpellkitFailureKind.Runtime, result.Failure?.Kind);
    }

    [Fact]
    public void InvalidOpcodeIsRejectedExplicitly()
    {
        var unit = new Unit();
        unit.Layouts.Add(new MemoryLayout(0, 1, 0));
        unit.Ops.Add(new Op((OpCode)int.MaxValue));
        var units = new FastList<Unit>();
        units.Add(unit);
        var context = SpkMachine.CreateExecutionContext(new UnitComposition(units));

        var error = Assert.Throws<InvalidOperationException>(() => SpkMachine.Execute(context));

        Assert.Contains("Unknown opcode value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LargeSourceInputParsesWithoutUnboundedRecursion()
    {
        const int payloadSize = 1_000_000;
        var source = "let payload = \"" + new string('a', payloadSize) + "\"\npayload.Length()";

        var result = SpkParser.Parse(SourceBuffer.FromString(source, "<large-input>"));

        Assert.True(result.Success);
    }

    private static void TwoParameters(string text, int number) { }

    [SpellkitResource("ProtectedResource")]
    private sealed class ProtectedResource : SpellkitResource { }

    private sealed class TemporaryLookupPaths : IDisposable
    {
        public TemporaryLookupPaths()
        {
            Root = Path.Combine(Path.GetTempPath(), "spellkit-security-" + Guid.NewGuid());
            OutsideDirectory = Path.Combine(
                Path.GetTempPath(),
                "spellkit-security-outside-" + Guid.NewGuid());
            OutsideFile = Path.Combine(
                Path.GetTempPath(),
                "spellkit-security-outside-" + Guid.NewGuid() + ".kit");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(OutsideDirectory);
            File.WriteAllText(OutsideFile, "func value() => 99", Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(OutsideDirectory, "linked.kit"),
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
