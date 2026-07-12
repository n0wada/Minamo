using Spellkit.Hosting;

namespace Spellkit.Examples.StationConsole;

internal static class Program
{
    private static int Main()
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

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "emergency.kit");
        var startup = instance.ExecuteFile(scriptPath);
        if (!PrintResult("Startup automation", startup))
        {
            return 1;
        }

        Console.WriteLine($"\nBefore incident: {station.Status()}");
        station.OxygenLevel = 24;
        instance.Environment.Signals.Emit("station.alert", "engineering");

        var dispatch = instance.DispatchSignals();
        if (!PrintResult("Emergency signal", dispatch))
        {
            return 1;
        }

        Console.WriteLine($"After incident:  {station.Status()}");
        Console.WriteLine("\nThe script raised reactor output and locked only the registered door resource.");
        return 0;
    }

    private static SpellkitHost CreateHost(Station station)
    {
        var host = new SpellkitHost(new()
        {
            Limits = new()
            {
                MaxInstructions = 50_000,
                MaxExecutionTime = TimeSpan.FromSeconds(2),
                MaxHostCommands = 100,
                MaxSignals = 10,
                MaxCallDepth = 64
            },
            Log = entry =>
                Console.WriteLine($"[{entry.Level}] {entry.Message}"),
            Trace = trace =>
            {
                if (trace.Kind is SpellkitTraceKind.HostCommand)
                {
                    Console.WriteLine($"[trace] command {trace.Name}");
                }
            }
        });

        host.AddCapabilities(
            "station.read",
            "station.control",
            "station.alert.listen",
            "log.write");

        host.DisableFileImports();

        host.AddSignal(
            "station.alert",
            listenCapability: "station.alert.listen");

        host.AddResourceType<StationReactorResource>();
        host.AddResourceType<StationDoorResource>();
        host.AddModule(new StationCommands(station));

        return host;
    }

    private static bool PrintResult(string operation, ISpellkitOperationResult result)
    {
        if (result.Success)
        {
            var delivered = result is SpellkitSignalDispatchResult signals
                ? $", {signals.Delivered} signals"
                : string.Empty;
            Console.WriteLine(
                $"{operation}: OK "
                + $"({result.Metrics.Instructions} instructions, "
                + $"{result.Metrics.HostCommands} host commands"
                + delivered
                + ")");
            return true;
        }

        foreach (var failure in result.Failures)
        {
            Console.Error.WriteLine(
                $"{operation}: {failure.Kind}: {failure.Message}");
        }

        if (result is SpellkitExecutionResult execution)
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
