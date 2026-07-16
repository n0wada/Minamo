using Spellkit.Compiler;
using Spellkit.Hosting;
using Spellkit.Linker;

namespace Spellkit.Examples.OrderWorkflow;

internal static class Program
{
    private static int Main()
    {
        var scripts = Path.Combine(AppContext.BaseDirectory, "Scripts");
        var ledger = new OrderLedger();
        var host = CreateHost(scripts, ledger);

        using var instance = host.CreateInstance(
            new SpellkitEnvironment().UseOutput(Console.Write));
        if (!Succeeded("Load workflow", instance.ExecuteFile(Path.Combine(scripts, "main.kit"))))
        {
            return 1;
        }

        var accepted = new object[] { "ORD-1001", "Ada", 1250.0 };
        var rejected = new object[] { "ORD-1002", "", -10.0 };

        instance.Environment.Signals.Emit("order.submitted", accepted);
        instance.Environment.Signals.Emit("order.submitted", rejected);
        if (!Succeeded("Submitted orders", instance.DispatchSignals()))
        {
            return 1;
        }

        instance.Environment.Signals.Emit("order.payment.confirmed", accepted);
        if (!Succeeded("Payment confirmed", instance.DispatchSignals())
            || !Succeeded("Shipment requested", instance.DispatchSignals()))
        {
            return 1;
        }

        Console.WriteLine("\nFinal order states:");
        Console.WriteLine($"  ORD-1001: {ledger.Status("ORD-1001")}");
        Console.WriteLine($"  ORD-1002: {ledger.Status("ORD-1002")}");
        Console.WriteLine("\nScript-owned counters:");
        Console.WriteLine($"  submitted={instance.Environment.State.Get<long>("submitted")}");
        Console.WriteLine($"  paid={instance.Environment.State.Get<long>("paid")}");
        Console.WriteLine($"  shipped={instance.Environment.State.Get<long>("shipped")}");
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

    private static SpellkitHost CreateHost(string scripts, OrderLedger ledger)
    {
        var options = BuilderOptions.Default();
        var lookup = FileLookup.Restricted(options)
            .AddStartupPath(scripts)
            .Build();
        var host = new SpellkitHost(new()
        {
            BuilderOptions = options,
            Limits = new()
            {
                MaxInstructions = 50_000,
                MaxExecutionTime = TimeSpan.FromSeconds(2),
                MaxHostCommands = 20,
                MaxSignals = 20,
                MaxCallDepth = 32
            },
            Log = entry => Console.WriteLine($"[{entry.Level}] {entry.Message}")
        })
            .UseFileLookup(lookup)
            .AddCapabilities("orders.*", "workflow.*", "state.*", "log.write")
            .AddSignal("order.submitted", listenCapability: "workflow.listen")
            .AddSignal("order.payment.confirmed", listenCapability: "workflow.listen")
            .AddSignal(
                "order.shipment.requested",
                listenCapability: "workflow.listen",
                emitCapability: "workflow.emit");

        host.AddModule(new OrderCommands(ledger));
        return host;
    }

    private static bool Succeeded(string name, ISpellkitOperationResult result)
    {
        if (result.Success)
        {
            var delivered = result is SpellkitSignalDispatchResult signals
                ? $", delivered={signals.Delivered}"
                : string.Empty;
            Console.WriteLine($"{name}: OK{delivered}");
            return true;
        }

        foreach (var failure in result.Failures)
        {
            Console.Error.WriteLine($"{name}: {failure.Kind}: {failure.Message}");
        }

        return false;
    }
}
