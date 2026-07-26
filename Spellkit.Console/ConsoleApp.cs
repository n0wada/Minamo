namespace Spellkit;

internal static class ConsoleApp
{
    private const int Success = 0;
    private const int Failure = 1;

    public static int Run(string[] args)
    {
        try
        {
            var options = CommandLine.Read(args);
            if (options.ShowHelp)
            {
                ConsoleOutput.Help();
                return Success;
            }

            if (options.ShowVersion)
            {
                ConsoleOutput.Version();
                return Success;
            }

            ConsoleOutput.NoLogo = options.NoLogo;
            ConsoleOutput.Header();

            return Run(options) ? Success : Failure;
        }
        catch (Exception ex)
        {
            ConsoleOutput.Error(ex.Message);
            return Failure;
        }
    }

    private static bool Run(CommandLineOptions options)
    {
        using var session = new ReplSession(options);

        if (options.GenerateBytecode)
        {
            return CompilationCommands.PrintBytecode(session, options);
        }

        var files = options.GetFileNames().ToArray();
        for (var i = 0; i < files.Length; i++)
        {
            if (i > 0)
            {
                session.Reset();
            }

            if (!session.EvalFile(files[i], options.MeasureTime))
            {
                return false;
            }
        }

        if (options.SelectName is not null)
        {
            return session.RunSelect(options.SelectName);
        }

        if (files.Length == 0 || options.StayInteractive)
        {
            session.Run();
        }

        return true;
    }
}
