using Minamo.Hosting;

namespace Minamo.Examples.StationConsole;

internal static class Program
{
    private static async Task<int> Main()
    {
        var station = new Station();
        var host = CreateHost(station);

        using var instance = host.CreateInstance(station);
        Console.WriteLine("Available station commands:");
        foreach (var command in instance.Environment.Commands.List())
        {
            Console.WriteLine($"  {command.Name} - {command.Description}");
        }

        Console.WriteLine();

        Console.WriteLine($"\nBefore incident: {station.Status()}");
        station.OxygenLevel = 24;

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "emergency.nami");
        var automation = await instance.ExecuteFileAsync(scriptPath);
        if (!PrintResult("Emergency automation", automation))
        {
            return 1;
        }

        Console.WriteLine($"After incident:  {station.Status()}");
        Console.WriteLine("\nThe script raised reactor output and locked only the registered door resource.");
        return 0;
    }

    private static MinamoHost CreateHost(Station station)
    {
        var host = new MinamoHost(new()
        {
            Limits = new()
            {
                MaxInstructions = 50_000,
                MaxExecutionTime = TimeSpan.FromSeconds(2),
                MaxHostCommands = 100,
                MaxCallDepth = 64
            },
            Log = entry =>
                Console.WriteLine($"[{entry.Level}] {entry.Message}")
        });

        host.AddCapabilities(
            "station.read",
            "station.control",
            "log.write");

        host.DisableFileImports();

        host.AddResourceType<StationReactorResource>();
        host.AddResourceType<StationDoorResource>();
        host.AddModule(new StationCommands(station));

        return host;
    }

    private static bool PrintResult(string operation, IMinamoOperationResult result)
    {
        if (result.Success)
        {
            Console.WriteLine(
                $"{operation}: OK "
                + $"({result.Metrics.Instructions} instructions, "
                + $"{result.Metrics.HostCommands} host commands"
                + ")");
            return true;
        }

        foreach (var failure in result.Failures)
        {
            Console.Error.WriteLine(
                $"{operation}: {failure.Kind}: {failure.Message}");
        }

        if (result is MinamoExecutionResult execution)
        {
            foreach (var diagnostic in execution.Diagnostics)
            {
                Console.Error.WriteLine(
                    $"  {diagnostic.File}:{diagnostic.Line}:{diagnostic.Column} "
                    + $"{diagnostic.Message}");
            }
        }

        return false;
    }
}
