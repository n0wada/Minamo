namespace Spellkit.UnitTesting;

internal static class TestRepository
{
    internal static string Root { get; } = FindRoot();
    internal static string BuildOutputRoot => Path.Combine(Root, "bin");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spellkit.sln"))
                && File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Spellkit repository root not found from '{AppContext.BaseDirectory}'.");
    }
}
