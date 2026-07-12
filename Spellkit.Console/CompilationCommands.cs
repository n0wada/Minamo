using Spellkit.Compiler;

namespace Spellkit;

internal static class CompilationCommands
{
    private static bool WriteBytecode(ReplSession session, CommandLineOptions options)
    {
        var success = true;

        foreach (var file in options.GetFileNames())
        {
            var output = GetOutputPath(file, options.OutputDirectory!, ".il");

            if (!File.Exists(file) || !session.Compile(file, out var unit))
            {
                ConsoleOutput.Error($"Compilation of file \"{file}\" skipped.");
                success = false;
                continue;
            }

            try
            {
                File.WriteAllText(output, BytecodeFormatter.Format(unit));

                ConsoleOutput.Information($"Bytecode written to \"{output}\".");
            }
            catch (Exception ex)
            {
                ConsoleOutput.Error(ex.Message);
                success = false;
            }
        }

        return success;
    }

    public static bool PrintBytecode(ReplSession session, CommandLineOptions options)
    {
        if (options.OutputDirectory is not null)
        {
            return WriteBytecode(session, options);
        }

        var units = new List<Unit>();
        var success = true;

        foreach (var file in options.GetFileNames())
        {
            if (!session.Compile(file, out var unit))
            {
                ConsoleOutput.Error($"Compilation of file \"{file}\" skipped.");
                success = false;
                continue;
            }

            units.Add(unit);
        }

        ConsoleOutput.Output(BytecodeFormatter.Format(units));
        return success;
    }

    private static string GetOutputPath(string file, string? outputDirectory, string extension)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + extension);
        }

        Directory.CreateDirectory(outputDirectory);
        return Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(file) + extension);
    }
}
