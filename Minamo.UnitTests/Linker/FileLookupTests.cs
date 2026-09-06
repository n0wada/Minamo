using Minamo.Compiler;
using Minamo.Linker;
using Xunit;

namespace Minamo.UnitTesting.Linker;

[Trait("Suite", "Pipeline")]
public sealed class FileLookupTests
{
    [Fact]
    public void UsesAdditionalPathsInRegistrationOrder()
    {
        using var paths = new LookupPaths();
        var firstFile = paths.Write(paths.First, "shared.nami", "let source = 1");
        paths.Write(paths.Second, "shared.nami", "let source = 2");
        var lookup = FileLookup.Restricted(BuilderOptions.Default())
            .AddPath(paths.First)
            .AddPath(paths.Second)
            .Build();

        Assert.True(lookup.Find(null, "shared.nami", out var resolved));
        Assert.Equal(Path.GetFullPath(firstFile), resolved);
    }

    [Fact]
    public void DoesNotSearchLibBelowStartupPathImplicitly()
    {
        using var paths = new LookupPaths();
        paths.Write(Path.Combine(paths.First, "lib"), "helper.nami", "let answer = 42");
        var lookup = FileLookup.Restricted(BuilderOptions.Default())
            .AddStartupPath(paths.First)
            .Build();

        Assert.False(lookup.Find(null, "helper.nami", out _));
    }

    [Fact]
    public void BuildSnapshotsRegisteredPaths()
    {
        using var paths = new LookupPaths();
        paths.Write(paths.Second, "late.nami", "let answer = 42");
        var builder = FileLookup.Restricted(BuilderOptions.Default())
            .AddPath(paths.First);
        var before = builder.Build();

        builder.AddPath(paths.Second);
        var after = builder.Build();

        Assert.False(before.Find(null, "late.nami", out _));
        Assert.True(after.Find(null, "late.nami", out _));
    }

    private sealed class LookupPaths : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            "minamo-file-lookup-" + Guid.NewGuid().ToString("N"));

        internal LookupPaths()
        {
            First = Path.Combine(root, "first");
            Second = Path.Combine(root, "second");
            Directory.CreateDirectory(First);
            Directory.CreateDirectory(Second);
        }

        internal string First { get; }

        internal string Second { get; }

        internal string Write(string directory, string name, string source)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, source);
            return path;
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
