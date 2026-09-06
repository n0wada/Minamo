using Minamo.Compiler;
using Minamo.Hosting;
using Minamo.Library.IO;
using Minamo.Library.Text;
using Minamo.Library.Time;
using Minamo.Library.Uuid;
using Minamo.Linker;

namespace Minamo.UnitTesting;

internal static class Program
{
    private const int Success = 0;
    private const int Failure = 1;

    public static int Main(string[] args)
    {
        try
        {
            var options = TestOptions.Parse(args);
            var files = FindTests(options.TestPath);
            var runner = new TestRunner(options);
            return runner.Run(files) ? Success : Failure;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return Failure;
        }
    }

    private static string[] FindTests(string path)
    {
        if (File.Exists(path))
        {
            return new[] { Path.GetFullPath(path) };
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Test path not found: {path}");
        }

        return Directory.GetFiles(path, "*.nami")
            .Select(Path.GetFullPath)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
