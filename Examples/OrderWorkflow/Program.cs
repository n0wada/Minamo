using Minamo.Compiler;
using Minamo.Hosting;
using Minamo.Linker;

namespace Minamo.Examples.OrderWorkflow;

internal static class Program
{
    private static async Task<int> Main()
    {
        var scripts = Path.Combine(AppContext.BaseDirectory, "Scripts");
        var ledger = new OrderLedger();
        var host = CreateHost(scripts, ledger);
        var accepted = new object[] { "ORD-1001", "Ada", 1250.0 };
        var rejected = new object[] { "ORD-1002", "", -10.0 };
        var environment = new MinamoEnvironment()
            .UseOutput(Console.Write)
            .Expose("submittedOrders", new object[] { accepted, rejected })
            .Expose("confirmedPayments", new object[] { accepted });

        using var instance = host.CreateInstance(environment);
        if (!Succeeded("Run workflow", await instance.ExecuteFileAsync(Path.Combine(scripts, "main.nami"))))
        {
            return 1;
        }

        Console.WriteLine("\nFinal order states:");
        Console.WriteLine($"  ORD-1001: {ledger.Status("ORD-1001")}");
        Console.WriteLine($"  ORD-1002: {ledger.Status("ORD-1002")}");
        Console.WriteLine("\nInstance registry counters:");
        Console.WriteLine($"  submitted={instance.Environment.Registry.Get<long>("submitted")}");
        Console.WriteLine($"  paid={instance.Environment.Registry.Get<long>("paid")}");
        Console.WriteLine($"  shipped={instance.Environment.Registry.Get<long>("shipped")}");
        Console.WriteLine("\nHost ledger:");
        foreach (var entry in ledger.Timeline)
        {
            Console.WriteLine($"  {entry}");
        }

        return ledger.Status("ORD-1001") == "shipped via courier (express)"
            && ledger.Status("ORD-1002") == "not accepted"
            ? 0
            : 1;
    }

    private static MinamoHost CreateHost(string scripts, OrderLedger ledger)
    {
        var options = BuilderOptions.Default();
        var lookup = FileLookup.Restricted(options)
            .AddStartupPath(scripts)
            .Build();
        var host = new MinamoHost(new()
        {
            BuilderOptions = options,
            Limits = new()
            {
                MaxInstructions = 50_000,
                MaxExecutionTime = TimeSpan.FromSeconds(2),
                MaxHostCommands = 20,
                MaxCallDepth = 32
            },
            Log = entry => Console.WriteLine($"[{entry.Level}] {entry.Message}")
        })
            .UseFileLookup(lookup)
            .AddCapabilities("orders.*", "registry.read", "log.write");

        host.AddModule(new OrderCommands(ledger));
        return host;
    }

    private static bool Succeeded(string name, IMinamoOperationResult result)
    {
        if (result.Success)
        {
            Console.WriteLine($"{name}: OK");
            return true;
        }

        foreach (var failure in result.Failures)
        {
            Console.Error.WriteLine($"{name}: {failure.Kind}: {failure.Message}");
        }

        return false;
    }
}
