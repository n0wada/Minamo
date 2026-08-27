using Spellkit.Compiler;
using Spellkit.Linker;
using Xunit;

namespace Spellkit.UnitTesting.Linker;

[Trait("Suite", "Pipeline")]
public sealed class FileLookupTests
{
    [Fact]
    public void UsesAdditionalPathsInRegistrationOrder()
    {
        using var paths = new LookupPaths();
        var firstFile = paths.Write(paths.First, "shared.kit", "let source = 1");
        paths.Write(paths.Second, "shared.kit", "let source = 2");
        var lookup = FileLookup.Restricted(BuilderOptions.Default())
            .AddPath(paths.First)
            .AddPath(paths.Second)
            .Build();

        Assert.True(lookup.Find(null, "shared.kit", out var resolved));
        Assert.Equal(Path.GetFullPath(firstFile), resolved);
    }

    [Fact]
    public void DoesNotSearchLibBelowStartupPathImplicitly()
    {
        using var paths = new LookupPaths();
        paths.Write(Path.Combine(paths.First, "lib"), "helper.kit", "let answer = 42");
        var lookup = FileLookup.Restricted(BuilderOptions.Default())
            .AddStartupPath(paths.First)
            .Build();

        Assert.False(lookup.Find(null, "helper.kit", out _));
    }

    [Fact]
    public void BuildSnapshotsRegisteredPaths()
    {
        using var paths = new LookupPaths();
        paths.Write(paths.Second, "late.kit", "let answer = 42");
        var builder = FileLookup.Restricted(BuilderOptions.Default())
            .AddPath(paths.First);
        var before = builder.Build();

        builder.AddPath(paths.Second);
        var after = builder.Build();

        Assert.False(before.Find(null, "late.kit", out _));
        Assert.True(after.Find(null, "late.kit", out _));
    }

    private sealed class LookupPaths : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            "spellkit-file-lookup-" + Guid.NewGuid().ToString("N"));

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
